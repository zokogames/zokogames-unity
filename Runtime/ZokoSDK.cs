using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace ZokoGames
{
    /// <summary>
    /// Static facade over the ZokoGames JavaScript SDK (<c>window.ZokoGames.SDK</c>).
    /// Every method has a callback form and an <c>...Async</c> Task form that throws <see cref="ZokoException"/>.
    /// In the Editor and in non-WebGL players a simulator answers instead of the browser.
    /// Name mapping: JS camelCase becomes PascalCase, e.g. <c>SDK.game.gameplayStart()</c> is <c>ZokoSDK.Game.GameplayStart()</c>.
    /// </summary>
    public static class ZokoSDK
    {
        /// <summary>Wrapper version; matches the JavaScript SDK release it was written against.</summary>
        public const string Version = "1.8.1";

        static IZokoTransport _transport;
        static bool _initialized;
        static bool _initializing;
        static readonly List<Action<ZokoError>> _initWaiters = new List<Action<ZokoError>>();
        static ZokoGameSettings _settings;
        static ZokoSystemInfo _systemInfo = new ZokoSystemInfo();
        static Dictionary<string, string> _inviteParams;
        static bool _accountAvailable;
        static bool _instantMultiplayer;
        static bool _offline;
        static bool _storeAvailable;
        static string _gameId = "";
        static bool _sandbox;

        public static bool IsInitialized { get { return _initialized; } }
        /// <summary>"local", "zokogames" or "disabled". "disabled" until <see cref="Init"/> completes.</summary>
        public static string Environment { get; private set; }
        /// <summary>True when initialised and the SDK is not disabled.</summary>
        public static bool IsAvailable { get { return _initialized && Environment != "disabled"; } }
        public static bool IsQaTool { get; private set; }
        /// <summary>True when the editor/native simulator is answering instead of the browser SDK.</summary>
        public static bool IsSimulated { get { return _transport is ZokoSimulator; } }

        static ZokoSDK() { Environment = "disabled"; }

        /// <summary>
        /// With Enter Play Mode Options and Domain Reload off, static state survives between Play sessions. Unity's documented fix is
        /// a SubsystemRegistration reset; it also runs once at player start, where it changes nothing.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _transport = null;
            _initialized = false;
            _initializing = false;
            _initWaiters.Clear();
            _settings = default(ZokoGameSettings);
            _systemInfo = new ZokoSystemInfo();
            _inviteParams = null;
            _accountAvailable = false;
            _instantMultiplayer = false;
            _offline = false;
            _storeAvailable = false;
            _gameId = "";
            _sandbox = false;
            Environment = "disabled";
            IsQaTool = false;
            Ad.ResetStatics();
            Game.ResetStatics();
            Game.Room.ResetStatics();
            User.ResetStatics();
            Data.ResetStatics();
            Achievements.ResetStatics();
            ZokoSDKBridge.ResetStatics();
        }

        // ------------------------------------------------------------------ init

        /// <summary>Initialise once at startup. Safe to call repeatedly; every callback fires after the first init completes.</summary>
        public static void Init(Action<ZokoError> done = null)
        {
            if (_initialized) { if (done != null) done(null); return; }
            if (done != null) _initWaiters.Add(done);
            if (_initializing) return;
            _initializing = true;

#if UNITY_WEBGL && !UNITY_EDITOR
            _transport = new ZokoWebGLTransport();
#else
            _transport = new ZokoSimulator(ZokoSDKSettings.Load());
#endif
            ZokoSDKBridge.GlobalEvent -= OnGlobalEvent;
            ZokoSDKBridge.GlobalEvent += OnGlobalEvent;
            ZokoSDKBridge.Instance.name = "ZokoSDKBridge"; // ensure the SendMessage target exists

            _transport.Call("init", null, null, r =>
            {
                _initializing = false;
                ZokoError error = null;
                if (r.Ok)
                {
                    var j = r.Result;
                    Environment = ZokoJson.Str(j, "environment", "disabled");
                    IsQaTool = ZokoJson.Bool(j, "isQaTool");
                    _settings = ZokoGameSettings.From(ZokoJson.Get(j, "settings"));
                    _systemInfo = ZokoSystemInfo.From(ZokoJson.Get(j, "systemInfo"));
                    _accountAvailable = ZokoJson.Bool(j, "isUserAccountAvailable");
                    _instantMultiplayer = ZokoJson.Bool(j, "isInstantMultiplayer");
                    _offline = ZokoJson.Bool(j, "isOffline");
                    _storeAvailable = ZokoJson.Bool(j, "storeAvailable");
                    _gameId = ZokoJson.Str(j, "gameId", "");
                    _inviteParams = ToStringMap(ZokoJson.Get(j, "inviteParams"));
                    _initialized = true;
                    Ad.ApplyPortalMute();
                    Game.RegisterScreenshotProvider();
                }
                else error = r.Error;
                var waiters = _initWaiters.ToArray();
                _initWaiters.Clear();
                foreach (var w in waiters) w(error);
            });
        }

        public static Task InitAsync()
        {
            return ToTask<object>(cb => Init(e => cb(null, e)));
        }

        static void OnGlobalEvent(string name, object data)
        {
            switch (name)
            {
                case "settingsChange":
                    _settings = ZokoGameSettings.From(data);
                    Ad.ApplyPortalMute();
                    Game.RaiseSettings(_settings);
                    break;
                case "offlineChange":
                    var off = data is bool && (bool)data;
                    if (off != _offline)
                    {
                        _offline = off;
                        Game.RaiseOffline(off);
                    }
                    break;
                case "auth":
                    var u = ZokoUser.From(data);
                    if (u != null) User.RaiseAuth(u);
                    break;
                case "achievementUnlocked":
                    var ach = ZokoAchievement.From(data);
                    if (ach != null) Achievements.RaiseUnlock(ach);
                    break;
                case "roomMessage":
                    var rm = ZokoRoomMessage.Parse(data);
                    if (rm != null) Game.Room.RaiseMessage(rm);
                    break;
                case "roomState":
                    Game.Room.RaiseState(ZokoJson.AsObject(ZokoJson.Get(data, "state")) ?? new Dictionary<string, object>(),
                        ZokoRoomStateChange.From(ZokoJson.Get(data, "change")));
                    break;
                case "roomMembers":
                    Game.Room.RaiseMembers(ZokoRoomMember.ListFrom(data));
                    break;
                case "roomHost":
                    Game.Room.RaiseHost(ZokoJson.Get(data, "hostId") as string, ZokoJson.Bool(data, "isHost"));
                    break;
                case "roomLeft":
                    Game.Room.RaiseLeft(ZokoJson.Get(data, "reason") as string, ZokoJson.Get(data, "roomId") as string);
                    break;
                case "roomError":
                    Game.Room.RaiseError(ZokoError.From(data));
                    break;
                case "screenshotRequest":
                    Game.AnswerScreenshotRequest();
                    break;
                case "mirrorError":
                    Data.RaiseMirrorError(ZokoError.From(data));
                    break;
                case "joinRoom":
                    _inviteParams = ToStringMap(data);
                    Game.RaiseJoinRoom(_inviteParams ?? new Dictionary<string, string>());
                    break;
            }
        }

        // ------------------------------------------------------------------ helpers

        static bool Ready(out ZokoError error)
        {
            if (!_initialized) { error = new ZokoError("sdkNotInitialized", "Call ZokoSDK.Init() and wait for it to finish"); return false; }
            if (Environment == "disabled") { error = new ZokoError("sdkDisabled", "The SDK is disabled in this environment"); return false; }
            error = null;
            return true;
        }

        static Dictionary<string, object> Args(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        static Dictionary<string, string> ToStringMap(object json)
        {
            var d = json as Dictionary<string, object>;
            if (d == null) return null;
            var m = new Dictionary<string, string>();
            foreach (var kv in d) m[kv.Key] = kv.Value as string ?? "";
            return m;
        }

        static void Send<T>(string method, Dictionary<string, object> args, Func<object, T> map, Action<T, ZokoError> done)
        {
            ZokoError err;
            if (!Ready(out err)) { if (done != null) done(default(T), err); return; }
            _transport.Call(method, args, null, r =>
            {
                if (done == null) return;
                if (!r.Ok) { done(default(T), r.Error); return; }
                T value;
                try { value = map(r.Result); }
                catch (Exception e) { done(default(T), new ZokoError("other", "Unreadable SDK response: " + e.Message)); return; }
                done(value, null);
            });
        }

        static void SendVoid(string method, Dictionary<string, object> args, Action<ZokoError> done)
        {
            Send<object>(method, args, x => null, (v, e) => { if (done != null) done(e); });
        }

        static Task<T> ToTask<T>(Action<Action<T, ZokoError>> start)
        {
            var tcs = new TaskCompletionSource<T>();
            start((v, e) =>
            {
                if (e != null) tcs.TrySetException(new ZokoException(e));
                else tcs.TrySetResult(v);
            });
            return tcs.Task;
        }

        static Task VoidTask(string method, Dictionary<string, object> args)
        {
            return ToTask<object>(cb => Send<object>(method, args, x => null, cb));
        }

        static ZokoResponse Sync(string method, Dictionary<string, object> args)
        {
            ZokoError err;
            if (!Ready(out err)) return new ZokoResponse { Ok = false, Error = err };
            return _transport.CallSync(method, args);
        }

        // ================================================================== Ad

        public static class Ad
        {
            /// <summary>
            /// When true (default) every ad pauses <c>AudioListener</c> from <c>adStarted</c> until it finishes or fails, then
            /// restores the previous state (or keeps it muted when the portal's mute setting is on). Set false to handle audio yourself.
            /// </summary>
            public static bool MuteAudioDuringAds = true;
            /// <summary>
            /// When true (default) the portal's mute setting (<see cref="Game.Settings"/>.MuteAudio) is applied to <c>AudioListener.pause</c>
            /// after init and on every change, without un-pausing audio while an ad plays. Set false (before Init) to handle
            /// <see cref="Game.SettingsChanged"/> yourself.
            /// </summary>
            public static bool FollowPortalMute = true;
            static bool _adAudioHeld;

            internal static void ResetStatics() { _adAudioHeld = false; _adAudioPrevPause = false; }

            internal static void ApplyPortalMute()
            {
                if (!FollowPortalMute) return;
                if (_adAudioHeld) { _adAudioPrevPause = _settings.MuteAudio; return; }
                AudioListener.pause = _settings.MuteAudio;
            }
            static bool _adAudioPrevPause;

            static void BeginAdAudio()
            {
                if (!MuteAudioDuringAds || _adAudioHeld) return;
                _adAudioHeld = true;
                _adAudioPrevPause = AudioListener.pause;
                AudioListener.pause = true;
            }

            static void EndAdAudio()
            {
                if (!_adAudioHeld) return;
                _adAudioHeld = false;
                AudioListener.pause = _adAudioPrevPause || _settings.MuteAudio;
            }

            /// <summary>
            /// Request a midgame or rewarded ad. Pause and mute in <paramref name="adStarted"/>, resume in
            /// <paramref name="adFinished"/> or <paramref name="adError"/> (exactly one of them fires last).
            /// </summary>
            public static void RequestAd(ZokoAdType type, Action adStarted = null, Action adFinished = null, Action<ZokoError> adError = null)
            {
                ZokoError err;
                if (!Ready(out err)) { if (adError != null) adError(err); return; }
                // Only a request whose ad started releases the audio hold: a second request refused while an ad plays
                // ("Another ad request is already in progress") must not unpause the game under the first ad.
                bool started = false;
                _transport.Call("ad.requestAd", Args("type", type == ZokoAdType.Rewarded ? "rewarded" : "midgame"),
                    (name, data) =>
                    {
                        if (name == "adStarted") { started = true; BeginAdAudio(); if (adStarted != null) adStarted(); }
                        else if (name == "adFinished") { if (started) EndAdAudio(); if (adFinished != null) adFinished(); }
                        else if (name == "adError") { if (started) EndAdAudio(); if (adError != null) adError(ZokoError.From(data)); }
                    },
                    r => { if (!r.Ok) { if (started) EndAdAudio(); if (adError != null) adError(r.Error); } });
            }

            /// <summary>Completes after the ad ends. Result is null when finished, or the error. Never throws.</summary>
            public static Task<ZokoError> RequestAdAsync(ZokoAdType type, Action adStarted = null)
            {
                var tcs = new TaskCompletionSource<ZokoError>();
                RequestAd(type, adStarted, () => tcs.TrySetResult(null), e => tcs.TrySetResult(e));
                return tcs.Task;
            }

            public static void HasAdblock(Action<bool, ZokoError> done)
            {
                Send<bool>("ad.hasAdblock", null, o => o is bool && (bool)o, done);
            }

            public static Task<bool> HasAdblockAsync()
            {
                return ToTask<bool>(cb => HasAdblock(cb));
            }
        }

        // ================================================================== Banner

        public static class Banner
        {
            /// <summary>Convert a Unity screen rect (pixels, origin bottom-left) to the normalised top-left rect the bridge wants.</summary>
            public static Dictionary<string, object> ToBridgeRect(Rect screenRect)
            {
                float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
                return new Dictionary<string, object>
                {
                    { "nx", screenRect.x / w },
                    { "ny", 1f - (screenRect.y + screenRect.height) / h },
                    { "nw", screenRect.width / w },
                    { "nh", screenRect.height / h },
                };
            }

            /// <summary>Static banner (728x90, 300x250, 320x50, 468x60 or 320x100), centred in <paramref name="screenRect"/>.</summary>
            public static void RequestBanner(string id, int width, int height, Rect screenRect, Action<ZokoError> done = null)
            {
                SendVoid("banner.requestBanner", Args("id", id, "width", width, "height", height, "rect", ToBridgeRect(screenRect)), done);
            }

            public static Task RequestBannerAsync(string id, int width, int height, Rect screenRect)
            {
                return VoidTask("banner.requestBanner", Args("id", id, "width", width, "height", height, "rect", ToBridgeRect(screenRect)));
            }

            /// <summary>Largest supported responsive size that fits <paramref name="screenRect"/>.</summary>
            public static void RequestResponsiveBanner(string id, Rect screenRect, Action<ZokoError> done = null)
            {
                SendVoid("banner.requestResponsiveBanner", Args("id", id, "rect", ToBridgeRect(screenRect)), done);
            }

            public static Task RequestResponsiveBannerAsync(string id, Rect screenRect)
            {
                return VoidTask("banner.requestResponsiveBanner", Args("id", id, "rect", ToBridgeRect(screenRect)));
            }

            public static void ClearBanner(string id, Action<ZokoError> done = null) { SendVoid("banner.clearBanner", Args("id", id), done); }
            public static Task ClearBannerAsync(string id) { return VoidTask("banner.clearBanner", Args("id", id)); }
            public static void ClearAllBanners(Action<ZokoError> done = null) { SendVoid("banner.clearAllBanners", null, done); }
            public static Task ClearAllBannersAsync() { return VoidTask("banner.clearAllBanners", null); }
            /// <summary>SDK 1.8.0: hide every banner without clearing it; <see cref="ShowAllBanners"/> brings them back.</summary>
            public static void HideAllBanners(Action<ZokoError> done = null) { SendVoid("banner.hideAllBanners", null, done); }
            public static Task HideAllBannersAsync() { return VoidTask("banner.hideAllBanners", null); }
            /// <summary>SDK 1.8.0: show the banners hidden by <see cref="HideAllBanners"/>.</summary>
            public static void ShowAllBanners(Action<ZokoError> done = null) { SendVoid("banner.showAllBanners", null, done); }
            public static Task ShowAllBannersAsync() { return VoidTask("banner.showAllBanners", null); }
        }

        // ================================================================== Game

        public static class Game
        {
            /// <summary>Raised when the portal changes settings (mute, chat).</summary>
            public static event Action<ZokoGameSettings> SettingsChanged;
            /// <summary>Raised with the new value when the player goes offline or back online (SDK 1.2.0+).</summary>
            public static event Action<bool> OfflineChanged;
            /// <summary>Raised when the player joins a room through an invite link while the game runs.</summary>
            public static event Action<Dictionary<string, string>> JoinRoom;

            /// <summary>Current portal-controlled settings.</summary>
            public static ZokoGameSettings Settings { get { return _settings; } }
            public static bool IsInstantMultiplayer { get { return _instantMultiplayer; } }
            /// <summary>Read-only id the portal sends at init (JS: game.gameId, SDK 1.6.2). Empty before init or when the portal does not send it.</summary>
            public static string GameId { get { return _gameId; } }
            /// <summary>True while the player has no connection (JS: game.isOffline). False on older SDKs and portals.</summary>
            public static bool IsOffline { get { return _offline; } }
            /// <summary>Parameters the game was launched with through an invite link, or null.</summary>
            public static Dictionary<string, string> InviteParams { get { return _inviteParams; } }

            internal static void ResetStatics()
            {
                SettingsChanged = null;
                OfflineChanged = null;
                JoinRoom = null;
                _screenshotProvider = null;
                _screenshotProviderOff = false;
                _screenshotRegistered = false;
            }

            internal static void RaiseSettings(ZokoGameSettings s) { var h = SettingsChanged; if (h != null) h(s); }
            internal static void RaiseOffline(bool offline) { var h = OfflineChanged; if (h != null) h(offline); }
            internal static void RaiseJoinRoom(Dictionary<string, string> p) { var h = JoinRoom; if (h != null) h(p); }

            public static void GameplayStart(Action<ZokoError> done = null) { SendVoid("game.gameplayStart", null, done); }
            public static void GameplayStop(Action<ZokoError> done = null) { SendVoid("game.gameplayStop", null, done); }
            public static void LoadingStart(Action<ZokoError> done = null) { SendVoid("game.loadingStart", null, done); }
            public static void LoadingStop(Action<ZokoError> done = null) { SendVoid("game.loadingStop", null, done); }
            public static void Happytime(Action<ZokoError> done = null) { SendVoid("game.happytime", null, done); }
            public static void LeftRoom(Action<ZokoError> done = null) { SendVoid("game.leftRoom", null, done); }
            public static Task GameplayStartAsync() { return VoidTask("game.gameplayStart", null); }
            public static Task GameplayStopAsync() { return VoidTask("game.gameplayStop", null); }
            public static Task LoadingStartAsync() { return VoidTask("game.loadingStart", null); }
            public static Task LoadingStopAsync() { return VoidTask("game.loadingStop", null); }

            /// <summary>Optional in-game load progress, 0 to 100, between LoadingStart and LoadingStop. Safe to call every frame (the SDK coalesces).</summary>
            public static void LoadingProgress(float percentage, Action<ZokoError> done = null)
            {
                SendVoid("game.loadingProgress", Args("percentage", percentage), done);
            }
            public static Task LoadingProgressAsync(float percentage) { return VoidTask("game.loadingProgress", Args("percentage", percentage)); }
            public static Task HappytimeAsync() { return VoidTask("game.happytime", null); }
            public static Task LeftRoomAsync() { return VoidTask("game.leftRoom", null); }

            /// <summary>Report level/game completion, 0 to 100.</summary>
            public static void ReportGameCompletedPercentage(float percentage, Action<ZokoError> done = null)
            {
                SendVoid("game.reportGameCompletedPercentage", Args("percentage", percentage), done);
            }
            public static Task ReportGameCompletedPercentageAsync(float percentage)
            {
                return VoidTask("game.reportGameCompletedPercentage", Args("percentage", percentage));
            }

            /// <summary>Values must be string, number or bool.</summary>
            public static void SetGameContext(IDictionary<string, object> context, Action<ZokoError> done = null)
            {
                SendVoid("game.setGameContext", Args("context", context), done);
            }
            public static Task SetGameContextAsync(IDictionary<string, object> context)
            {
                return VoidTask("game.setGameContext", Args("context", context));
            }
            public static void ClearGameContext(Action<ZokoError> done = null) { SendVoid("game.clearGameContext", null, done); }
            public static Task ClearGameContextAsync() { return VoidTask("game.clearGameContext", null); }

            public static void UpdateRoom(ZokoRoomUpdate update, Action<ZokoError> done = null)
            {
                SendVoid("game.updateRoom", Args("update", update == null ? new Dictionary<string, object>() : update.ToJson()), done);
            }
            public static Task UpdateRoomAsync(ZokoRoomUpdate update)
            {
                return VoidTask("game.updateRoom", Args("update", update == null ? new Dictionary<string, object>() : update.ToJson()));
            }

            public static void InviteLink(IDictionary<string, string> inviteParams, Action<string, ZokoError> done)
            {
                Send<string>("game.inviteLink", Args("params", inviteParams), o => o as string ?? "", done);
            }
            public static Task<string> InviteLinkAsync(IDictionary<string, string> inviteParams)
            {
                return ToTask<string>(cb => InviteLink(inviteParams, cb));
            }

            /// <summary>Deprecated alias of <see cref="IsInstantMultiplayer"/> (JS: isInstantJoin).</summary>
            public static bool IsInstantJoin { get { return _instantMultiplayer; } }

            /// <summary>Deprecated: use UpdateRoom with IsJoinable = true. Calls back with the invite link.</summary>
            public static void ShowInviteButton(IDictionary<string, string> inviteParams, Action<string, ZokoError> done = null)
            {
                Send<string>("game.showInviteButton", Args("params", inviteParams), o => o as string ?? "", done);
            }
            public static Task<string> ShowInviteButtonAsync(IDictionary<string, string> inviteParams)
            {
                return ToTask<string>(cb => ShowInviteButton(inviteParams, cb));
            }
            /// <summary>Deprecated: use UpdateRoom with IsJoinable = false.</summary>
            public static void HideInviteButton(Action<ZokoError> done = null) { SendVoid("game.hideInviteButton", null, done); }
            public static Task HideInviteButtonAsync() { return VoidTask("game.hideInviteButton", null); }

            // ---------------------------------------------------------------- chat reports and screenshots (SDK 1.4.0)

            /// <summary>
            /// Report one chat message to the portal's moderators (JS: <c>SDK.game.reportChat</c>). Calls back with
            /// <c>invalidArgument</c> for a bad field, <c>rateLimited</c> when reports come too fast and <c>unexpectedError</c>
            /// when the portal does not accept reports. Show your own "report sent" confirmation; this is not a replacement for your filters.
            /// </summary>
            public static void ReportChat(ZokoChatReport report, Action<ZokoError> done = null)
            {
                SendVoid("game.reportChat", Args("report", (report ?? new ZokoChatReport()).ToJson()), done);
            }
            public static Task ReportChatAsync(ZokoChatReport report)
            {
                return VoidTask("game.reportChat", Args("report", (report ?? new ZokoChatReport()).ToJson()));
            }

            /// <summary>Shortcut for the common case: the reported name and message, a reason (default Other) and optional room and recent lines.</summary>
            public static void ReportChat(string reportedName, string message, ZokoChatReportReason reason = ZokoChatReportReason.Other,
                string reportedUserId = null, string roomId = null, List<ZokoChatLine> context = null, Action<ZokoError> done = null)
            {
                ReportChat(new ZokoChatReport
                {
                    ReportedName = reportedName, Message = message, Reason = reason,
                    ReportedUserId = reportedUserId, RoomId = roomId, Context = context,
                }, done);
            }

            /// <summary>Longest side of the picture the wrapper sends; the SDK scales to 1280 px anyway, so larger frames only cost time.</summary>
            public const int ScreenshotMaxEdge = 1280;
            /// <summary>JPG quality (1 to 100) of the built-in encoder. The SDK recompresses to fit 1.5 MB.</summary>
            public static int ScreenshotJpgQuality = 80;

            static Func<Texture2D> _screenshotProvider;
            static bool _screenshotProviderOff;
            static bool _screenshotRegistered;

            /// <summary>
            /// Supply the picture the portal's feedback form attaches (JS: <c>SDK.game.onScreenshotRequest</c>). The provider runs at the
            /// end of a frame (so <c>ScreenCapture.CaptureScreenshotAsTexture()</c> works inside it) and returns a readable Texture2D, or
            /// null for "no picture". The wrapper encodes it as JPG, never destroys a texture you return, and answers within the SDK's 3 s limit.
            /// Pass null to go back to the built-in default (a capture of the whole screen, scaled to <see cref="ScreenshotMaxEdge"/>).
            /// </summary>
            public static void SetScreenshotProvider(Func<Texture2D> provider)
            {
                _screenshotProvider = provider;
                _screenshotProviderOff = false;
                RegisterScreenshotProvider();
            }

            /// <summary>
            /// Stop answering from Unity. The JS SDK then falls back to its own capture of the largest visible canvas, which may be blank
            /// on WebGL (the drawing buffer is cleared after each frame), so prefer the built-in default.
            /// </summary>
            public static void ClearScreenshotProvider()
            {
                _screenshotProvider = null;
                _screenshotProviderOff = true;
                if (!_screenshotRegistered) return;
                _screenshotRegistered = false;
                if (IsAvailable) Sync("game.setScreenshotProvider", Args("enabled", false));
            }

            internal static void RegisterScreenshotProvider()
            {
                if (_screenshotProviderOff || !IsAvailable) return;
                var r = Sync("game.setScreenshotProvider", Args("enabled", true));
                _screenshotRegistered = r.Ok;
            }

            /// <summary>
            /// Capture a picture now the way an answer to the portal's request is made: wait for the end of the frame, run the provider
            /// (or the built-in screen capture), scale, encode as JPG and call back with a <c>data:image/jpeg;base64,</c> URL, or null when
            /// nothing could be captured. Also works in the Editor, so you can test a provider without the portal.
            /// </summary>
            public static void CaptureScreenshot(Action<string> done)
            {
                ZokoSDKBridge.Instance.StartCoroutine(CaptureRoutine(done));
            }

            internal static void AnswerScreenshotRequest()
            {
                CaptureScreenshot(url => Sync("game.answerScreenshot", Args("dataUrl", url)));
            }

            static IEnumerator CaptureRoutine(Action<string> done)
            {
                yield return new WaitForEndOfFrame();
                string url = null;
                try { url = EncodeScreenshot(); }
                catch (Exception e) { Debug.LogWarning("[ZokoGames] screenshot failed: " + e.Message); }
                if (done != null) done(url);
            }

            static string EncodeScreenshot()
            {
                var provider = _screenshotProvider;
                var own = provider == null;
                var tex = own ? ScreenCapture.CaptureScreenshotAsTexture() : provider();
                if (tex == null) return null;
                var small = Downscale(tex);
                try
                {
                    var jpg = small.EncodeToJPG(Mathf.Clamp(ScreenshotJpgQuality, 1, 100));
                    return jpg == null || jpg.Length == 0 ? null : "data:image/jpeg;base64," + Convert.ToBase64String(jpg);
                }
                finally
                {
                    if (small != tex) UnityEngine.Object.Destroy(small);
                    if (own) UnityEngine.Object.Destroy(tex);
                }
            }

            static Texture2D Downscale(Texture2D src)
            {
                int longest = Mathf.Max(src.width, src.height);
                if (longest <= ScreenshotMaxEdge) return src;
                float k = (float)ScreenshotMaxEdge / longest;
                int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));
                var rt = RenderTexture.GetTemporary(w, h, 0);
                var prev = RenderTexture.active;
                try
                {
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
                    dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    dst.Apply();
                    return dst;
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            /// <summary>Read one invite parameter, or null.</summary>
            public static string GetInviteParam(string key)
            {
                var r = Sync("game.getInviteParam", Args("key", key));
                return r.Ok ? r.Result as string : null;
            }

            // ---------------------------------------------------------------- room data channel (SDK 1.3.0)

            /// <summary>
            /// Game data channel of the current room (JS: <c>SDK.game.room</c>): small JSON messages between the players of
            /// one room plus a host-written shared state. It is for game data, not text chat. A room exists after
            /// <see cref="UpdateRoom"/> (or after joining from an invite) and ends with <see cref="LeftRoom"/>.
            /// Payloads are JSON: pass a Dictionary / List / string / number / bool, a JSON string with <c>SendJson</c>,
            /// or a [Serializable] class with <c>SendTyped</c>. All events are raised on the Unity main thread.
            /// Limits: 8 KB per message or patch, 20 messages per second, 64 KB of state.
            /// </summary>
            public static class Room
            {
                /// <summary>A message from another player. Use <c>message.Json</c>, <c>message.As&lt;T&gt;()</c> or <c>message.Data</c>.</summary>
                public static event Action<ZokoRoomMessage> MessageReceived;
                /// <summary>The shared state changed: the full new state plus what changed.</summary>
                public static event Action<Dictionary<string, object>, ZokoRoomStateChange> StateChanged;
                /// <summary>A player joined or left (or their profile changed). Raised with the whole member list.</summary>
                public static event Action<List<ZokoRoomMember>> MembersChanged;
                /// <summary>The host changed (also the first assignment): the new host id (null when none) and whether it is this player.</summary>
                public static event Action<string, bool> HostChanged;
                /// <summary>SDK 1.7.0: the portal ended this player's membership without the game asking. Reason is <c>left</c>, <c>kicked</c>, <c>closed</c> or <c>disconnected</c>; the room id is null when unknown.</summary>
                public static event Action<string, string> Left;
                /// <summary>A failure the platform reports after a call returned, such as <c>rateLimited</c> or <c>notHost</c>.</summary>
                public static event Action<ZokoError> Error;

                internal static void ResetStatics()
                {
                    MessageReceived = null;
                    StateChanged = null;
                    MembersChanged = null;
                    HostChanged = null;
                    Left = null;
                    Error = null;
                }

                internal static void RaiseMessage(ZokoRoomMessage m) { var h = MessageReceived; if (h != null) h(m); }
                internal static void RaiseState(Dictionary<string, object> s, ZokoRoomStateChange c) { var h = StateChanged; if (h != null) h(s, c); }
                internal static void RaiseMembers(List<ZokoRoomMember> m) { var h = MembersChanged; if (h != null) h(m); }
                internal static void RaiseHost(string hostId, bool isHost) { var h = HostChanged; if (h != null) h(hostId, isHost); }
                internal static void RaiseLeft(string reason, string roomId) { var h = Left; if (h != null) h(reason, roomId); }
                internal static void RaiseError(ZokoError e) { var h = Error; if (h != null) h(e); }

                /// <summary>Platform id of this player, or null when not in a room.</summary>
                public static string SelfId
                {
                    get { var r = Sync("room.selfId", null); return r.Ok ? r.Result as string : null; }
                }

                /// <summary>True when this player is the room host (the first member).</summary>
                public static bool IsHost
                {
                    get { var r = Sync("room.isHost", null); return r.Ok && r.Result is bool && (bool)r.Result; }
                }

                /// <summary>Players currently in the room, including this one. Empty when not in a room.</summary>
                public static List<ZokoRoomMember> Members
                {
                    get { var r = Sync("room.members", null); return r.Ok ? ZokoRoomMember.ListFrom(r.Result) : new List<ZokoRoomMember>(); }
                }

                /// <summary>Copy of the shared state (host-written; empty when not in a room).</summary>
                public static Dictionary<string, object> State
                {
                    get
                    {
                        var r = Sync("room.getState", null);
                        return (r.Ok ? ZokoJson.AsObject(r.Result) : null) ?? new Dictionary<string, object>();
                    }
                }

                /// <summary>The shared state as a JSON string.</summary>
                public static string GetStateJson() { return ZokoJson.Serialize(State); }

                /// <summary>Read one shared-state value (Dictionary, List, string, double, bool) or null.</summary>
                public static object GetState(string key) { return ZokoJson.Get(State, key); }

                /// <summary>Deserialise the whole shared state into a [Serializable] type with <see cref="JsonUtility"/>.</summary>
                public static T GetState<T>() { return JsonUtility.FromJson<T>(GetStateJson()); }

                /// <summary>Send any JSON-serialisable value (Dictionary, List, string, number, bool) to every other member.</summary>
                public static void Send(object data, Action<ZokoError> done = null) { SendVoid("room.send", Args("data", data), done); }

                /// <summary>Send to one member only (an id from <see cref="Members"/>).</summary>
                public static void SendTo(string memberId, object data, Action<ZokoError> done = null)
                {
                    SendVoid("room.send", Args("data", data, "to", memberId), done);
                }

                /// <summary>Send a payload that is already a JSON string. <paramref name="to"/> null means every other member.</summary>
                public static void SendJson(string json, string to = null, Action<ZokoError> done = null)
                {
                    object data;
                    try { data = ZokoJson.Parse(json); }
                    catch (Exception e) { if (done != null) done(new ZokoError("invalidArgument", "data must be valid JSON: " + e.Message)); return; }
                    SendVoid("room.send", to == null ? Args("data", data) : Args("data", data, "to", to), done);
                }

                /// <summary>Send a [Serializable] object (serialised with <see cref="JsonUtility"/>). <paramref name="to"/> null means every other member.</summary>
                public static void SendTyped<T>(T value, string to = null, Action<ZokoError> done = null)
                {
                    SendJson(JsonUtility.ToJson(value), to, done);
                }

                public static Task SendAsync(object data, string to = null)
                {
                    return VoidTask("room.send", to == null ? Args("data", data) : Args("data", data, "to", to));
                }

                public static Task SendJsonAsync(string json, string to = null)
                {
                    return ToTask<object>(cb => SendJson(json, to, e => cb(null, e)));
                }

                public static Task SendTypedAsync<T>(T value, string to = null)
                {
                    return SendJsonAsync(JsonUtility.ToJson(value), to);
                }

                /// <summary>
                /// Host only. Merge <paramref name="patch"/> into the shared state; a null value removes the key (keys are 1 to 40 characters of
                /// A-Z a-z 0-9 _ . : -). It applies once the platform confirms it: read it in <see cref="StateChanged"/>, not right after this call.
                /// </summary>
                public static void SetState(IDictionary<string, object> patch, Action<ZokoError> done = null)
                {
                    SendVoid("room.setState", Args("patch", patch), done);
                }

                /// <summary>Set one key (value may be null to remove it).</summary>
                public static void SetState(string key, object value, Action<ZokoError> done = null)
                {
                    SendVoid("room.setState", Args("patch", new Dictionary<string, object> { { key, value } }), done);
                }

                /// <summary>Same as <see cref="SetState(IDictionary{string, object}, Action{ZokoError})"/> with the patch as a JSON object string.</summary>
                public static void SetStateJson(string patchJson, Action<ZokoError> done = null)
                {
                    object patch;
                    try { patch = ZokoJson.Parse(patchJson); }
                    catch (Exception e) { if (done != null) done(new ZokoError("invalidArgument", "patch must be valid JSON: " + e.Message)); return; }
                    SendVoid("room.setState", Args("patch", patch), done);
                }

                public static Task SetStateAsync(IDictionary<string, object> patch) { return VoidTask("room.setState", Args("patch", patch)); }
            }
        }

        // ================================================================== User

        public static class User
        {
            /// <summary>Raised when the player signs in or switches account.</summary>
            public static event Action<ZokoUser> AuthChanged;
            internal static void RaiseAuth(ZokoUser u) { var h = AuthChanged; if (h != null) h(u); }
            internal static void ResetStatics() { AuthChanged = null; }

            /// <summary>False when the game runs where portal accounts do not exist.</summary>
            public static bool IsUserAccountAvailable { get { return _accountAvailable; } }
            public static ZokoSystemInfo SystemInfo { get { return _systemInfo; } }

            /// <summary>Calls back with null user when logged out.</summary>
            public static void GetUser(Action<ZokoUser, ZokoError> done)
            {
                Send<ZokoUser>("user.getUser", null, ZokoUser.From, done);
            }
            public static Task<ZokoUser> GetUserAsync() { return ToTask<ZokoUser>(cb => GetUser(cb)); }

            /// <summary>Page is 1-based, size 1 to 50.</summary>
            public static void ListFriends(int page, int size, Action<ZokoFriendsPage, ZokoError> done)
            {
                Send<ZokoFriendsPage>("user.listFriends", Args("page", page, "size", size), ZokoFriendsPage.From, done);
            }
            public static Task<ZokoFriendsPage> ListFriendsAsync(int page, int size)
            {
                return ToTask<ZokoFriendsPage>(cb => ListFriends(page, size, cb));
            }

            /// <summary>Signed token to verify on your server.</summary>
            public static void GetUserToken(Action<string, ZokoError> done, ZokoSigningKeyType signingKeyType = ZokoSigningKeyType.Default)
            {
                var a = new Dictionary<string, object>();
                if (signingKeyType == ZokoSigningKeyType.InGameAuth) a["signingKeyType"] = "IN_GAME_AUTH";
                else if (signingKeyType == ZokoSigningKeyType.InGameAuthLegacy) a["signingKeyType"] = "IN_GAME_AUTH_LEGACY";
                Send<string>("user.getUserToken", a, o => o as string ?? "", done);
            }
            public static Task<string> GetUserTokenAsync(ZokoSigningKeyType signingKeyType = ZokoSigningKeyType.Default)
            {
                return ToTask<string>(cb => GetUserToken(cb, signingKeyType));
            }

            /// <summary>Deprecated: use <see cref="Store.GetPurchaseToken"/>.</summary>
            public static void GetXsollaUserToken(Action<string, ZokoError> done) { Store.GetPurchaseToken(done); }
            /// <summary>Deprecated: use <see cref="Store.GetPurchaseTokenAsync"/>.</summary>
            public static Task<string> GetXsollaUserTokenAsync() { return Store.GetPurchaseTokenAsync(); }

            public static void ShowAuthPrompt(Action<ZokoUser, ZokoError> done)
            {
                Send<ZokoUser>("user.showAuthPrompt", null, ZokoUser.From, done);
            }
            public static Task<ZokoUser> ShowAuthPromptAsync() { return ToTask<ZokoUser>(cb => ShowAuthPrompt(cb)); }

            /// <summary>Calls back true when the player chose to link accounts.</summary>
            public static void ShowAccountLinkPrompt(Action<bool, ZokoError> done)
            {
                Send<bool>("user.showAccountLinkPrompt", null, o => ZokoJson.Str(o, "response") == "yes", done);
            }
            public static Task<bool> ShowAccountLinkPromptAsync() { return ToTask<bool>(cb => ShowAccountLinkPrompt(cb)); }

            public static void SubmitScore(string encryptedScore, double score, Action<ZokoError> done = null)
            {
                SendVoid("user.submitScore", Args("encryptedScore", encryptedScore, "score", score), done);
            }
            public static Task SubmitScoreAsync(string encryptedScore, double score)
            {
                return VoidTask("user.submitScore", Args("encryptedScore", encryptedScore, "score", score));
            }

            /// <summary>SDK 1.8.0: seal a score with the leaderboard key; calls back the base64 payload to pass to <see cref="SubmitScore"/>. <c>userNotAuthenticated</c> for guests, <c>invalidArgument</c> for a bad score or key. <paramref name="earnedAt"/> is Unix milliseconds for a delayed copy; 0 means a live run.</summary>
            public static void EncryptScore(double score, string key, Action<string, ZokoError> done, long earnedAt = 0)
            {
                Send<string>("user.encryptScore", Args("score", score, "key", key, "earnedAt", earnedAt), o => o as string ?? "", done);
            }
            public static Task<string> EncryptScoreAsync(double score, string key, long earnedAt = 0)
            {
                return ToTask<string>(cb => EncryptScore(score, key, cb, earnedAt));
            }
        }

        // ================================================================== Data

        /// <summary>
        /// Cloud save with a PlayerPrefs-shaped API. Reads and writes are synchronous against a preloaded
        /// copy; writes are uploaded in the background. Before Init completes reads return defaults and
        /// writes are dropped with a warning. Total size is limited to 1 MiB.
        /// </summary>
        public static class Data
        {
            /// <summary>Error of the last Data call (for example dataLimitExceeded on a write, dataModuleDisabled on a read), or null when it succeeded.</summary>
            public static ZokoError LastError { get; private set; }

            public static void SetString(string key, string value) { Put(key, value ?? ""); }
            public static string GetString(string key, string defaultValue = "")
            {
                var v = Read(key);
                return v ?? defaultValue;
            }

            public static void SetInt(string key, int value) { Put(key, value.ToString(CultureInfo.InvariantCulture)); }
            public static int GetInt(string key, int defaultValue = 0)
            {
                int n;
                var v = Read(key);
                return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : defaultValue;
            }

            public static void SetFloat(string key, float value) { Put(key, value.ToString("R", CultureInfo.InvariantCulture)); }
            public static float GetFloat(string key, float defaultValue = 0f)
            {
                float f;
                var v = Read(key);
                return v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : defaultValue;
            }

            public static bool HasKey(string key) { return Read(key) != null; }

            public static void DeleteKey(string key)
            {
                var r = Sync("data.removeItem", Args("key", key));
                LastError = r.Ok ? null : r.Error;
            }

            public static void DeleteAll()
            {
                var r = Sync("data.clear", null);
                LastError = r.Ok ? null : r.Error;
            }

            /// <summary>No-op kept for PlayerPrefs.Save() parity; uploads are batched automatically.</summary>
            public static void Save() { }

            // Raw localStorage-shaped access (JS names getItem / setItem / removeItem / clear).
            public static string GetItem(string key) { return Read(key); }
            public static void SetItem(string key, string value) { Put(key, value ?? ""); }
            public static void RemoveItem(string key) { DeleteKey(key); }
            public static void Clear() { DeleteAll(); }

            // ---------------------------------------------------------------- localStorage mirror (SDK 1.4.0)

            /// <summary>Raised when a mirrored key cannot be saved, for example <c>dataLimitExceeded</c> when the 1 MB cloud save is full.</summary>
            public static event Action<ZokoError> MirrorError;
            internal static void RaiseMirrorError(ZokoError e) { var h = MirrorError; if (h != null) h(e); }
            internal static void ResetStatics() { MirrorError = null; LastError = null; }

            /// <summary>
            /// Copy the browser page's <c>localStorage</c> writes into the cloud save and restore newer cloud values into it (JS:
            /// <c>SDK.data.mirrorLocalStorage</c>). Only for JavaScript-side storage: a browser plugin, a .jslib or page script that saves with
            /// <c>localStorage</c>. Unity WebGL <c>PlayerPrefs</c> are NOT in localStorage: they live in the page's IndexedDB
            /// (<c>/idbfs</c>), so this mirror never sees them. Use <see cref="PushPlayerPrefs"/> and <see cref="PullPlayerPrefs"/> for
            /// PlayerPrefs, or the <c>Set*</c>/<c>Get*</c> calls above. Call it right after <c>InitAsync</c>, before the game reads its saves.
            /// With no arguments every key is mirrored; <paramref name="keys"/> (exact names), <paramref name="prefix"/> and
            /// <paramref name="pattern"/> (a JavaScript regular expression source) narrow it. Returns false and sets <see cref="LastError"/>
            /// when the SDK is not ready. The Editor and non-WebGL players have no localStorage, so there it only logs.
            /// </summary>
            public static bool MirrorLocalStorage(string prefix = null, IList<string> keys = null, string pattern = null)
            {
                var a = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(prefix)) a["prefix"] = prefix;
                if (keys != null && keys.Count > 0) a["keys"] = new List<string>(keys);
                if (!string.IsNullOrEmpty(pattern)) a["pattern"] = pattern;
                var r = Sync("data.mirrorLocalStorage", a);
                LastError = r.Ok ? null : r.Error;
                return r.Ok;
            }

            /// <summary>Stop mirroring (JS: the function <c>mirrorLocalStorage</c> returns).</summary>
            public static void StopMirrorLocalStorage()
            {
                var r = Sync("data.stopMirrorLocalStorage", null);
                LastError = r.Ok ? null : r.Error;
            }

            /// <summary>
            /// Stop the IndexedDB mirror started by the WebGL template variable <c>ZOKO_MIRROR_INDEXEDDB</c> (JS: the stop function of
            /// <c>data.mirrorIndexedDB</c>, SDK 1.7.0). A no-op in the Editor and on non-WebGL players.
            /// </summary>
            public static void StopMirrorIndexedDB()
            {
                var r = Sync("data.stopMirrorIndexedDB", null);
                LastError = r.Ok ? null : r.Error;
            }

            /// <summary>
            /// Copy the listed <c>PlayerPrefs</c> keys into cloud save (string, int and float only; PlayerPrefs cannot report a key's type,
            /// so you say it). Keys PlayerPrefs does not hold are skipped. Returns the number of keys written. Call it when the player
            /// saves or leaves the level; writes are batched like every other Data call.
            /// </summary>
            public static int PushPlayerPrefs(IDictionary<string, ZokoPrefType> keys)
            {
                var written = 0;
                if (keys == null) return 0;
                foreach (var pair in keys)
                {
                    if (!PlayerPrefs.HasKey(pair.Key)) continue;
                    switch (pair.Value)
                    {
                        case ZokoPrefType.Int: SetInt(pair.Key, PlayerPrefs.GetInt(pair.Key)); break;
                        case ZokoPrefType.Float: SetFloat(pair.Key, PlayerPrefs.GetFloat(pair.Key)); break;
                        default: SetString(pair.Key, PlayerPrefs.GetString(pair.Key)); break;
                    }
                    written++;
                }
                return written;
            }

            /// <summary>
            /// Restore the listed keys from cloud save into <c>PlayerPrefs</c>, normally right after <c>InitAsync</c>. A key already in
            /// PlayerPrefs is kept unless <paramref name="overwrite"/> is true (cloud save wins). Returns the number of keys restored.
            /// </summary>
            public static int PullPlayerPrefs(IDictionary<string, ZokoPrefType> keys, bool overwrite = false)
            {
                var restored = 0;
                if (keys == null) return 0;
                foreach (var pair in keys)
                {
                    if (!HasKey(pair.Key) || (!overwrite && PlayerPrefs.HasKey(pair.Key))) continue;
                    switch (pair.Value)
                    {
                        case ZokoPrefType.Int: PlayerPrefs.SetInt(pair.Key, GetInt(pair.Key)); break;
                        case ZokoPrefType.Float: PlayerPrefs.SetFloat(pair.Key, GetFloat(pair.Key)); break;
                        default: PlayerPrefs.SetString(pair.Key, GetString(pair.Key)); break;
                    }
                    restored++;
                }
                if (restored > 0) PlayerPrefs.Save();
                return restored;
            }

            static string Read(string key)
            {
                var r = Sync("data.getItem", Args("key", key));
                // Every read sets LastError, like every write: a failed call before it must not make a missing key
                // (null, no error) look like a refused read.
                LastError = r.Ok ? null : r.Error;
                if (!r.Ok) return null;
                return r.Result as string;
            }

            static void Put(string key, string value)
            {
                var r = Sync("data.setItem", Args("key", key, "value", value));
                LastError = r.Ok ? null : r.Error;
                if (!r.Ok) Debug.LogWarning("[ZokoGames] Data write failed: " + r.Error);
            }
        }

        // ================================================================== Clipboard

        /// <summary>
        /// Copy text to the player's clipboard. Call it from a click or tap handler (browsers refuse otherwise). In a WebGL build this uses
        /// the page clipboard; elsewhere it uses <c>GUIUtility.systemCopyBuffer</c>. Returns false when the copy was refused.
        /// </summary>
        public static bool CopyToClipboard(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ZokoWebGLTransport.CopyText(text);
#else
            try { GUIUtility.systemCopyBuffer = text ?? ""; return true; }
            catch (Exception) { return false; }
#endif
        }

        // ================================================================== Store

        /// <summary>In-game purchases. Check <see cref="IsAvailable"/> before showing purchase UI and verify ownership on your server.</summary>
        public static class Store
        {
            /// <summary>True when this host offers purchases (says nothing about sign-in).</summary>
            public static bool IsAvailable { get { return _storeAvailable; } }

            /// <summary>Route later store calls to the provider sandbox (test payments).</summary>
            public static ZokoError SetSandbox(bool sandbox)
            {
                var r = Sync("store.setSandbox", Args("sandbox", sandbox));
                if (r.Ok) _sandbox = sandbox;
                return r.Ok ? null : r.Error;
            }

            static Dictionary<string, object> Page(int limit, int offset)
            {
                return Args("limit", limit, "offset", offset, "sandbox", _sandbox);
            }

            /// <summary>limit 1..50, offset 0 or more.</summary>
            public static void GetCatalogItems(Action<List<ZokoStoreItem>, ZokoError> done, int limit = 50, int offset = 0)
            {
                Send<List<ZokoStoreItem>>("store.getCatalogItems", Page(limit, offset), o =>
                {
                    var list = new List<ZokoStoreItem>();
                    var arr = o as List<object>;
                    if (arr != null) foreach (var x in arr) list.Add(ZokoStoreItem.From(x));
                    return list;
                }, done);
            }
            public static Task<List<ZokoStoreItem>> GetCatalogItemsAsync(int limit = 50, int offset = 0)
            {
                return ToTask<List<ZokoStoreItem>>(cb => GetCatalogItems(cb, limit, offset));
            }

            public static void GetPlayerInventory(Action<List<ZokoInventoryItem>, ZokoError> done, int limit = 50, int offset = 0)
            {
                Send<List<ZokoInventoryItem>>("store.getPlayerInventory", Page(limit, offset), o =>
                {
                    var list = new List<ZokoInventoryItem>();
                    var arr = o as List<object>;
                    if (arr != null) foreach (var x in arr) list.Add(ZokoInventoryItem.From(x));
                    return list;
                }, done);
            }
            public static Task<List<ZokoInventoryItem>> GetPlayerInventoryAsync(int limit = 50, int offset = 0)
            {
                return ToTask<List<ZokoInventoryItem>>(cb => GetPlayerInventory(cb, limit, offset));
            }

            /// <summary>Opens the portal checkout (one purchase at a time). Calls back with the purchased item id.</summary>
            public static void BuyItem(string itemId, Action<string, ZokoError> done)
            {
                Send<string>("store.buyItem", Args("itemId", itemId), o => ZokoJson.Str(o, "itemId"), done);
            }
            public static Task<string> BuyItemAsync(string itemId) { return ToTask<string>(cb => BuyItem(itemId, cb)); }

            /// <summary>Signed token listing what the player owns, for your server to verify (userNotAuthenticated for guests).</summary>
            public static void GetPurchaseToken(Action<string, ZokoError> done)
            {
                Send<string>("store.getPurchaseToken", null, o => o as string ?? "", done);
            }
            public static Task<string> GetPurchaseTokenAsync() { return ToTask<string>(cb => GetPurchaseToken(cb)); }

            public static void ConsumeItem(string sku, int quantity, Action<ZokoError> done = null)
            {
                SendVoid("store.consumeItem", Args("sku", sku, "quantity", quantity), done);
            }
            public static Task ConsumeItemAsync(string sku, int quantity) { return VoidTask("store.consumeItem", Args("sku", sku, "quantity", quantity)); }
        }

        // ================================================================== Achievements

        /// <summary>Game achievements. <see cref="Unlock"/> resolves with Ok false for guests and unknown keys.</summary>
        public static class Achievements
        {
            /// <summary>Raised when an achievement unlocks for the player.</summary>
            public static event Action<ZokoAchievement> OnUnlock;
            internal static void RaiseUnlock(ZokoAchievement a) { var h = OnUnlock; if (h != null) h(a); }
            internal static void ResetStatics() { OnUnlock = null; }

            public static void List(Action<List<ZokoAchievement>, ZokoError> done)
            {
                Send<List<ZokoAchievement>>("achievements.list", null, o =>
                {
                    var list = new List<ZokoAchievement>();
                    var arr = o as List<object>;
                    if (arr != null) foreach (var x in arr) list.Add(ZokoAchievement.From(x));
                    return list;
                }, done);
            }
            public static Task<List<ZokoAchievement>> ListAsync() { return ToTask<List<ZokoAchievement>>(cb => List(cb)); }

            /// <summary>The key must match ^[a-z0-9_-]{1,40}$, otherwise the call fails with invalidArgument.</summary>
            public static void Unlock(string key, Action<ZokoUnlockResult, ZokoError> done)
            {
                Send<ZokoUnlockResult>("achievements.unlock", Args("key", key), ZokoUnlockResult.From, done);
            }
            public static Task<ZokoUnlockResult> UnlockAsync(string key) { return ToTask<ZokoUnlockResult>(cb => Unlock(key, cb)); }
        }

        // ================================================================== Analytics

        public static class Analytics
        {
            /// <summary>Record a completed purchase. <paramref name="order"/> is any JSON-serialisable value (dictionary, list, string, number).</summary>
            public static void TrackOrder(string provider, object order, Action<ZokoError> done = null)
            {
                SendVoid("analytics.trackOrder", Args("provider", provider, "order", order), done);
            }
            public static Task TrackOrderAsync(string provider, object order)
            {
                return VoidTask("analytics.trackOrder", Args("provider", provider, "order", order));
            }

            /// <summary>SDK 1.7.0: record a custom event. The name must match ^[a-z][a-z0-9_]{0,39}$; <paramref name="parameters"/> is a flat dictionary of string, number or boolean values (at most 10), or null. Fire and forget.</summary>
            public static void Track(string name, Dictionary<string, object> parameters = null, Action<ZokoError> done = null)
            {
                SendVoid("analytics.track", parameters == null ? Args("name", name) : Args("name", name, "params", parameters), done);
            }
            public static Task TrackAsync(string name, Dictionary<string, object> parameters = null)
            {
                return VoidTask("analytics.track", parameters == null ? Args("name", name) : Args("name", name, "params", parameters));
            }
        }
    }
}
