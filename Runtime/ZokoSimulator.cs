using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ZokoGames
{
    /// <summary>
    /// Used in the Editor and in every non-WebGL build: answers the same wire methods as the JS bridge
    /// with plausible responses so the game runs without a browser. Tunable via <see cref="ZokoSDKSettings"/>.
    /// Cloud-save data persists in PlayerPrefs under "zoko-sdk-sim-data".
    /// </summary>
    internal sealed class ZokoSimulator : IZokoTransport
    {
        const string DataKey = "zoko-sdk-sim-data";
        const int DataLimitBytes = 1048576;
        static readonly string[] StaticSizes = { "728x90", "300x250", "320x50", "468x60", "320x100" };

        readonly ZokoSDKSettings _s;
        readonly Dictionary<string, string> _data = new Dictionary<string, string>();
        readonly HashSet<string> _banners = new HashSet<string>();
        readonly Dictionary<string, DateTime> _bannerAt = new Dictionary<string, DateTime>();
        bool _loggedIn;
        bool _adPlaying;
        bool _authOpen;
        bool _initialised;
        // The simulator never receives an invite (same as the JS simulator), so this stays null; the explicit initialiser keeps CS0649 quiet.
        readonly Dictionary<string, string> _inviteParams = null;
        // Simulated room (SDK 1.3.0): one member (this player, the host); messages loop back so a single editor instance can be tested.
        const string RoomSelfId = "sim-player";
        const int RoomMaxBytes = 8 * 1024;
        const int RoomMaxStateBytes = 64 * 1024;
        readonly Dictionary<string, object> _roomState = new Dictionary<string, object>();
        long _roomRev;
        long _roomSeq;
        readonly Dictionary<string, Dictionary<string, object>> _achievements = new Dictionary<string, Dictionary<string, object>>();

        public ZokoSimulator(ZokoSDKSettings settings)
        {
            _s = settings;
            _loggedIn = settings.loggedIn && settings.accountAvailable;
            try
            {
                var saved = ZokoJson.Parse(PlayerPrefs.GetString(DataKey, "")) as Dictionary<string, object>;
                if (saved != null)
                    foreach (var kv in saved) _data[kv.Key] = kv.Value as string ?? "";
            }
            catch (Exception) { /* corrupt simulated save: start empty */ }
        }

        void Log(string m) { if (_s.verboseLog) Debug.Log("[ZokoGames simulator] " + m); }

        Dictionary<string, object> UserJson()
        {
            return new Dictionary<string, object>
            {
                { "username", _s.username },
                { "profilePictureUrl", "" },
                { "__dangerousUserId", _s.userId },
            };
        }

        Dictionary<string, object> SettingsJson()
        {
            return new Dictionary<string, object> { { "disableChat", _s.disableChat || _s.kidsMode }, { "kidsMode", _s.kidsMode }, { "muteAudio", _s.muteAudio } };
        }

        /// <summary>The same limits the JS SDK enforces on <c>game.reportChat</c>; returns the problem or null.</summary>
        static string ValidateChatReport(Dictionary<string, object> r)
        {
            var name = ZokoJson.Get(r, "reportedName") as string;
            if (string.IsNullOrEmpty(name) || name.Length > 40) return "reportedName must be 1 to 40 characters";
            var msg = ZokoJson.Get(r, "message") as string;
            if (string.IsNullOrEmpty(msg) || msg.Length > 500) return "message must be 1 to 500 characters";
            var reason = ZokoJson.Get(r, "reason") as string;
            if (reason != "abuse" && reason != "spam" && reason != "cheating" && reason != "other") return "reason must be one of abuse, spam, cheating, other";
            var room = ZokoJson.Get(r, "roomId") as string;
            if (room != null && (room.Length == 0 || room.Length > 64)) return "roomId must be 1 to 64 characters";
            return null;
        }

        static ZokoResponse Invalid(string m) { return ZokoResponse.Fail("invalidArgument", m); }

        // ---- sync ----
        public ZokoResponse CallSync(string method, Dictionary<string, object> a)
        {
            if (_s.simulateDisabled) return ZokoResponse.Fail("sdkDisabled", "The SDK is disabled in this environment");
            if (!_initialised) return ZokoResponse.Fail("sdkNotInitialized", "Call ZokoSDK.Init first");
            switch (method)
            {
                case "game.getSettings": return ZokoResponse.Success(SettingsJson());
                case "game.getInviteParams": return ZokoResponse.Success(_inviteParams);
                case "game.getInviteParam":
                {
                    string v;
                    string k = ZokoJson.Str(a, "key");
                    return ZokoResponse.Success(_inviteParams != null && _inviteParams.TryGetValue(k, out v) ? v : null);
                }
                case "data.getItem":
                {
                    string v;
                    return ZokoResponse.Success(_data.TryGetValue(ZokoJson.Str(a, "key"), out v) ? v : null);
                }
                case "data.setItem":
                {
                    string key = ZokoJson.Str(a, "key");
                    string val = ZokoJson.Str(a, "value");
                    string old;
                    if (_data.TryGetValue(key, out old) && old == val) return ZokoResponse.Success(null);
                    var next = new Dictionary<string, string>(_data);
                    next[key] = val;
                    if (Encoding.UTF8.GetByteCount(ZokoJson.Serialize(next)) > DataLimitBytes)
                        return ZokoResponse.Fail("dataLimitExceeded", "Saved data would exceed " + DataLimitBytes + " bytes");
                    _data[key] = val;
                    PersistData();
                    return ZokoResponse.Success(null);
                }
                case "data.removeItem":
                    if (_data.Remove(ZokoJson.Str(a, "key"))) PersistData();
                    return ZokoResponse.Success(null);
                case "data.clear":
                    _data.Clear();
                    PersistData();
                    return ZokoResponse.Success(null);
                case "game.setScreenshotProvider":
                    // There is no portal to ask in the Editor; ZokoSDK.Game.CaptureScreenshot runs the same capture on demand.
                    Log("game.setScreenshotProvider enabled=" + (ZokoJson.Get(a, "enabled") as bool? ?? true));
                    return ZokoResponse.Success(null);
                case "game.answerScreenshot":
                    Log("game.answerScreenshot " + (ZokoJson.Get(a, "dataUrl") is string ? "(picture)" : "(none)"));
                    return ZokoResponse.Success(null);
                case "data.mirrorLocalStorage":
                    // No browser localStorage outside WebGL: nothing to mirror, but the call is accepted so the same code runs everywhere.
                    Log("data.mirrorLocalStorage ignored (no localStorage in the Editor or on this platform)");
                    return ZokoResponse.Success(null);
                case "data.stopMirrorLocalStorage": return ZokoResponse.Success(null);
                case "room.selfId": return ZokoResponse.Success(RoomSelfId);
                case "room.isHost": return ZokoResponse.Success(true);
                case "room.members":
                    return ZokoResponse.Success(new List<object>
                    {
                        new Dictionary<string, object> { { "id", RoomSelfId }, { "username", _s.username }, { "profilePictureUrl", "" } },
                    });
                case "room.getState": return ZokoResponse.Success(new Dictionary<string, object>(_roomState));
                case "store.isAvailable": return ZokoResponse.Success(_s.storeAvailable);
                case "store.setSandbox":
                    if (!_s.storeAvailable) return ZokoResponse.Fail("other", "The store is not available");
                    return ZokoResponse.Success(null);
            }
            return ZokoResponse.Fail("unknownMethod", "Unknown sync method " + method);
        }

        void PersistData()
        {
            PlayerPrefs.SetString(DataKey, ZokoJson.Serialize(_data));
            PlayerPrefs.Save();
        }

        // ---- async ----
        public void Call(string method, Dictionary<string, object> a, Action<string, object> onEvent, Action<ZokoResponse> done)
        {
            ZokoSDKBridge.Instance.StartCoroutine(Run(method, a ?? new Dictionary<string, object>(), onEvent, done));
        }

        IEnumerator Run(string method, Dictionary<string, object> a, Action<string, object> onEvent, Action<ZokoResponse> done)
        {
            yield return null; // always answer asynchronously, like the browser does

            if (method == "init")
            {
                _initialised = true;
                if (_s.simulateDisabled)
                {
                    done(ZokoResponse.Success(new Dictionary<string, object>
                    {
                        { "environment", "disabled" }, { "isQaTool", false }, { "settings", SettingsJson() },
                        { "systemInfo", null }, { "isUserAccountAvailable", false }, { "isInstantMultiplayer", false }, { "isOffline", false },
                        { "inviteParams", null }, { "storeAvailable", false },
                    }));
                    yield break;
                }
                Log("init (environment local)");
                done(ZokoResponse.Success(new Dictionary<string, object>
                {
                    { "environment", "local" },
                    { "isQaTool", false },
                    { "settings", SettingsJson() },
                    { "systemInfo", new Dictionary<string, object>
                        {
                            { "countryCode", _s.countryCode }, { "locale", _s.locale },
                            { "device", new Dictionary<string, object> { { "type", "desktop" } } },
                            { "os", new Dictionary<string, object> { { "name", SystemInfo.operatingSystem }, { "version", "" } } },
                            { "browser", new Dictionary<string, object> { { "name", "Unity Editor" }, { "version", Application.unityVersion } } },
                            { "applicationType", "web" },
                        } },
                    { "isUserAccountAvailable", _s.accountAvailable },
                    { "isInstantMultiplayer", _s.instantMultiplayer },
                    { "isOffline", _s.offline },
                    { "inviteParams", null },
                    { "storeAvailable", _s.storeAvailable },
                }));
                yield break;
            }

            if (_s.simulateDisabled) { done(ZokoResponse.Fail("sdkDisabled", "The SDK is disabled in this environment")); yield break; }
            if (!_initialised) { done(ZokoResponse.Fail("sdkNotInitialized", "Call ZokoSDK.Init first")); yield break; }

            switch (method)
            {
                case "ad.requestAd":
                {
                    string type = ZokoJson.Str(a, "type");
                    if (type != "midgame" && type != "rewarded") { done(Invalid("ad type must be \"midgame\" or \"rewarded\"")); yield break; }
                    if (_adPlaying)
                    {
                        onEvent("adError", Err("other", "Another ad request is already in progress"));
                        done(ZokoResponse.Success(null));
                        yield break;
                    }
                    if (_s.adBehaviour == ZokoSDKSettings.AdBehaviour.NoFill)
                    {
                        onEvent("adError", Err("unfilled", "No ad was available"));
                        done(ZokoResponse.Success(null));
                        yield break;
                    }
                    _adPlaying = true;
                    onEvent("adStarted", null);
                    ZokoSDKBridge.Instance.OverlayText = "SIMULATED " + type.ToUpperInvariant() + " AD";
                    Log("ad started (" + type + ")");
                    yield return new WaitForSecondsRealtime(_s.adDurationSeconds);
                    ZokoSDKBridge.Instance.OverlayText = null;
                    _adPlaying = false;
                    if (_s.adBehaviour == ZokoSDKSettings.AdBehaviour.Error) onEvent("adError", Err("other", "Simulated ad error"));
                    else onEvent("adFinished", null);
                    done(ZokoResponse.Success(null));
                    yield break;
                }
                case "ad.hasAdblock": done(ZokoResponse.Success(_s.adblock)); yield break;

                case "banner.requestBanner":
                case "banner.requestResponsiveBanner":
                {
                    string id = ZokoJson.Str(a, "id");
                    if (id == "") { done(ZokoResponse.Fail("missingId", "A container id is required")); yield break; }
                    if (_adPlaying) { done(ZokoResponse.Fail("videoAdPlaying", "A video ad is playing", id)); yield break; }
                    if (method == "banner.requestBanner")
                    {
                        string size = (int)ZokoJson.Num(a, "width") + "x" + (int)ZokoJson.Num(a, "height");
                        if (Array.IndexOf(StaticSizes, size) < 0)
                        {
                            done(ZokoResponse.Fail("invalidSize", size + " is not a supported banner size", id));
                            yield break;
                        }
                    }
                    DateTime last;
                    if (_bannerAt.TryGetValue(id, out last) && (DateTime.UtcNow - last).TotalSeconds < 30)
                    {
                        done(ZokoResponse.Fail("bannerCooldown", "Banners can refresh at most every 30 seconds", id));
                        yield break;
                    }
                    _bannerAt[id] = DateTime.UtcNow;
                    _banners.Add(id);
                    Log("banner shown: " + id);
                    done(ZokoResponse.Success(null));
                    yield break;
                }
                case "banner.clearBanner":
                    _banners.Remove(ZokoJson.Str(a, "id"));
                    _bannerAt.Remove(ZokoJson.Str(a, "id"));
                    done(ZokoResponse.Success(null));
                    yield break;
                case "banner.clearAllBanners":
                    _banners.Clear();
                    _bannerAt.Clear();
                    done(ZokoResponse.Success(null));
                    yield break;

                case "game.gameplayStart":
                case "game.gameplayStop":
                case "game.loadingStart":
                case "game.loadingStop":
                case "game.hideInviteButton":
                case "game.happytime":
                case "game.leftRoom":
                case "game.clearGameContext":
                    Log(method);
                    done(ZokoResponse.Success(null));
                    yield break;
                case "game.loadingProgress":
                {
                    double lp = ZokoJson.Num(a, "percentage", -1);
                    done(lp < 0 || lp > 100 ? Invalid("percentage must be a number between 0 and 100") : ZokoResponse.Success(null));
                    yield break;
                }
                case "game.reportGameCompletedPercentage":
                {
                    double p = ZokoJson.Num(a, "percentage", -1);
                    done(p < 0 || p > 100 ? Invalid("percentage must be a number between 0 and 100") : ZokoResponse.Success(null));
                    yield break;
                }
                case "game.setGameContext":
                    done(ZokoJson.Get(a, "context") is Dictionary<string, object>
                        ? ZokoResponse.Success(null)
                        : Invalid("context must be an object of string, number or boolean values"));
                    yield break;
                case "game.reportChat":
                {
                    var rep = ZokoJson.Get(a, "report") as Dictionary<string, object>;
                    string bad = rep == null ? "report must be an object" : ValidateChatReport(rep);
                    if (bad == null) Log("game.reportChat " + ZokoJson.Serialize(rep));
                    done(bad == null ? ZokoResponse.Success(null) : Invalid(bad));
                    yield break;
                }
                case "game.updateRoom":
                    done(ZokoJson.Get(a, "update") is Dictionary<string, object> ? ZokoResponse.Success(null) : Invalid("update must be an object"));
                    yield break;
                case "game.showInviteButton":
                case "game.inviteLink":
                {
                    var sb = new StringBuilder("https://zokogames.com/play/simulated");
                    var p = ZokoJson.Get(a, "params") as Dictionary<string, object>;
                    if (p != null)
                    {
                        bool first = true;
                        foreach (var kv in p)
                        {
                            sb.Append(first ? '?' : '&').Append(Uri.EscapeDataString(kv.Key)).Append('=')
                              .Append(Uri.EscapeDataString(kv.Value as string ?? ""));
                            first = false;
                        }
                    }
                    done(ZokoResponse.Success(sb.ToString()));
                    yield break;
                }

                case "user.getUser":
                    done(ZokoResponse.Success(_loggedIn ? UserJson() : null));
                    yield break;
                case "user.listFriends":
                {
                    int page = (int)ZokoJson.Num(a, "page"), size = (int)ZokoJson.Num(a, "size");
                    if (page < 1) { done(Invalid("page must be an integer >= 1")); yield break; }
                    if (size < 1 || size > 50) { done(Invalid("size must be an integer between 1 and 50")); yield break; }
                    var friends = new List<object>();
                    int start = (page - 1) * size;
                    for (int i = start; i < Math.Min(start + size, _s.friendCount); i++)
                        friends.Add(new Dictionary<string, object>
                        {
                            { "id", "sim-friend-" + (i + 1) }, { "username", "Friend " + (i + 1) }, { "profilePictureUrl", "" },
                        });
                    done(ZokoResponse.Success(new Dictionary<string, object>
                    {
                        { "friends", friends }, { "page", page }, { "size", size },
                        { "hasMore", start + size < _s.friendCount }, { "total", _s.friendCount },
                    }));
                    yield break;
                }
                case "user.getUserToken":
                    if (!_loggedIn) { done(ZokoResponse.Fail("userNotAuthenticated", "No user is signed in")); yield break; }
                    done(ZokoResponse.Success("simulated." + Convert.ToBase64String(Encoding.UTF8.GetBytes(_s.userId)) + ".unsigned"));
                    yield break;
                case "store.getPurchaseToken":
                case "user.getXsollaUserToken":
                    if (!_loggedIn) { done(ZokoResponse.Fail("userNotAuthenticated", "No user is signed in")); yield break; }
                    done(ZokoResponse.Success("simulated-purchase." + Convert.ToBase64String(Encoding.UTF8.GetBytes(_s.userId))));
                    yield break;
                case "user.showAuthPrompt":
                    if (_authOpen) { done(ZokoResponse.Fail("showAuthPromptInProgress", "The auth prompt is already open")); yield break; }
                    if (_loggedIn) { done(ZokoResponse.Fail("userAlreadySignedIn", "A user is already signed in")); yield break; }
                    _authOpen = true;
                    yield return new WaitForSecondsRealtime(0.4f);
                    _authOpen = false;
                    _loggedIn = true;
                    ZokoSDKBridge.Instance.SendMessage("OnEvent",
                        ZokoJson.Serialize(new Dictionary<string, object> { { "id", 0 }, { "name", "auth" }, { "data", UserJson() } }));
                    done(ZokoResponse.Success(UserJson()));
                    yield break;
                case "user.showAccountLinkPrompt":
                    done(ZokoResponse.Success(new Dictionary<string, object> { { "response", _loggedIn ? "yes" : "no" } }));
                    yield break;
                case "user.submitScore":
                    done(ZokoJson.Str(a, "encryptedScore") == "" ? Invalid("submitScore needs { encryptedScore: string, score: number }") : ZokoResponse.Success(null));
                    yield break;

                case "store.getCatalogItems":
                case "store.getPlayerInventory":
                case "store.buyItem":
                case "store.consumeItem":
                    done(StoreCall(method, a));
                    yield break;

                case "achievements.list":
                    done(ZokoResponse.Success(new List<object>(_achievements.Values)));
                    yield break;
                case "achievements.unlock":
                {
                    string key = ZokoJson.Str(a, "key");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9_-]{1,40}$")) { done(Invalid("key must match ^[a-z0-9_-]{1,40}$")); yield break; }
                    if (!_loggedIn) { done(ZokoResponse.Success(new Dictionary<string, object> { { "ok", false }, { "reason", "guest" } })); yield break; }
                    Dictionary<string, object> have;
                    if (_achievements.TryGetValue(key, out have))
                    {
                        done(ZokoResponse.Success(new Dictionary<string, object> { { "ok", true }, { "unlocked", false }, { "achievement", have } }));
                        yield break;
                    }
                    var ach = new Dictionary<string, object>
                    {
                        { "key", key }, { "name", key }, { "description", "" }, { "iconUrl", null }, { "points", 0 }, { "hidden", false },
                        { "unlocked", true }, { "unlockedAt", DateTime.UtcNow.ToString("o") },
                    };
                    _achievements[key] = ach;
                    Log("simulated achievement unlocked: " + key);
                    ZokoSDKBridge.Instance.SendMessage("OnEvent",
                        ZokoJson.Serialize(new Dictionary<string, object> { { "id", 0 }, { "name", "achievementUnlocked" }, { "data", ach } }));
                    done(ZokoResponse.Success(new Dictionary<string, object> { { "ok", true }, { "unlocked", true }, { "achievement", ach } }));
                    yield break;
                }
                case "room.send":
                {
                    var data = ZokoJson.Get(a, "data");
                    int size = Encoding.UTF8.GetByteCount(ZokoJson.Serialize(data));
                    if (size > RoomMaxBytes) { done(ZokoResponse.Fail("payloadTooLarge", "data is " + size + " bytes; the limit is " + RoomMaxBytes)); yield break; }
                    var toVal = ZokoJson.Get(a, "to");
                    string to = toVal as string;
                    if (toVal != null && (to == null || to == "")) { done(Invalid("to must be a member id")); yield break; }
                    if (to != null && to != RoomSelfId) { done(ZokoResponse.Fail("unknownMember", "No member " + to + " in the simulated room")); yield break; }
                    Log("room.send " + ZokoJson.Serialize(data));
                    done(ZokoResponse.Success(null));
                    // Loop back locally (a real portal never echoes your own messages).
                    RaiseGlobal("roomMessage", new Dictionary<string, object>
                    {
                        { "data", data }, { "direct", to != null }, { "from", RoomSelfId }, { "seq", ++_roomSeq },
                        { "ts", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds },
                    });
                    yield break;
                }
                case "room.setState":
                {
                    var patch = ZokoJson.Get(a, "patch") as Dictionary<string, object>;
                    if (patch == null || patch.Count == 0) { done(Invalid("patch needs at least one key")); yield break; }
                    var keys = new List<object>();
                    foreach (var kv in patch)
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(kv.Key, "^[A-Za-z0-9_.:-]{1,40}$") || kv.Key == "__proto__" || kv.Key == "constructor" || kv.Key == "prototype")
                        {
                            done(Invalid("keys are 1-40 characters of A-Z a-z 0-9 _ . : -"));
                            yield break;
                        }
                        keys.Add(kv.Key);
                    }
                    int psize = Encoding.UTF8.GetByteCount(ZokoJson.Serialize(patch));
                    if (psize > RoomMaxBytes) { done(ZokoResponse.Fail("payloadTooLarge", "patch is " + psize + " bytes; the limit is " + RoomMaxBytes)); yield break; }
                    var next = new Dictionary<string, object>(_roomState);
                    foreach (var kv in patch)
                    {
                        if (kv.Value == null) next.Remove(kv.Key);
                        else next[kv.Key] = kv.Value;
                    }
                    if (Encoding.UTF8.GetByteCount(ZokoJson.Serialize(next)) > RoomMaxStateBytes)
                    {
                        done(ZokoResponse.Fail("stateTooLarge", "The shared state would exceed " + RoomMaxStateBytes + " bytes"));
                        yield break;
                    }
                    _roomState.Clear();
                    foreach (var kv in next) _roomState[kv.Key] = kv.Value;
                    _roomRev++;
                    done(ZokoResponse.Success(null));
                    RaiseGlobal("roomState", new Dictionary<string, object>
                    {
                        { "state", new Dictionary<string, object>(_roomState) },
                        { "change", new Dictionary<string, object> { { "by", RoomSelfId }, { "keys", keys }, { "rev", _roomRev } } },
                    });
                    yield break;
                }
                case "analytics.track":
                    done(System.Text.RegularExpressions.Regex.IsMatch(ZokoJson.Str(a, "name") ?? "", "^[a-z][a-z0-9_]{0,39}$") ? ZokoResponse.Success(null) : Invalid("name must match ^[a-z][a-z0-9_]{0,39}$"));
                    yield break;
                case "analytics.trackOrder":
                    done(ZokoJson.Str(a, "provider") == "" ? Invalid("provider must be a non-empty string") : ZokoResponse.Success(null));
                    yield break;
            }

            // Sync methods are also reachable through Call.
            done(CallSync(method, a));
        }

        // ---- store (simulated catalogue; inventory persists in PlayerPrefs) ----
        const string InventoryKey = "zoko-sdk-sim-inventory";
        static readonly string[][] Catalog =
        {
            new[] { "sim_coins_100", "100 Coins", "A small pile of coins", "1" },
            new[] { "sim_coins_500", "500 Coins", "A big pile of coins", "1" },
            new[] { "sim_no_ads", "Remove Ads", "One-time purchase", "0" },
        };

        Dictionary<string, object> LoadInventory()
        {
            try { return ZokoJson.Parse(PlayerPrefs.GetString(InventoryKey, "{}")) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
            catch (Exception) { return new Dictionary<string, object>(); }
        }

        void SaveInventory(Dictionary<string, object> inv)
        {
            PlayerPrefs.SetString(InventoryKey, ZokoJson.Serialize(inv));
            PlayerPrefs.Save();
        }

        ZokoResponse StoreCall(string method, Dictionary<string, object> a)
        {
            if (!_s.storeAvailable) return ZokoResponse.Fail("other", "The store is not available");
            int limit = a.ContainsKey("limit") ? (int)ZokoJson.Num(a, "limit") : 50;
            int offset = (int)ZokoJson.Num(a, "offset");
            if (limit < 1 || limit > 50) return Invalid("limit must be an integer between 1 and 50");
            if (offset < 0) return Invalid("offset must be an integer >= 0");
            var inv = LoadInventory();
            switch (method)
            {
                case "store.getCatalogItems":
                {
                    var list = new List<object>();
                    for (int i = offset; i < Math.Min(offset + limit, Catalog.Length); i++)
                        list.Add(new Dictionary<string, object>
                        {
                            { "sku", Catalog[i][0] }, { "name", Catalog[i][1] }, { "description", Catalog[i][2] }, { "imageUrl", "" },
                            { "price", new Dictionary<string, object> { { "amount", 0.99 + i }, { "currency", "USD" } } },
                            { "consumable", Catalog[i][3] == "1" },
                        });
                    return ZokoResponse.Success(list);
                }
                case "store.getPlayerInventory":
                {
                    var list = new List<object>();
                    foreach (var c in Catalog)
                    {
                        if (!inv.ContainsKey(c[0])) continue;
                        list.Add(new Dictionary<string, object>
                        {
                            { "sku", c[0] }, { "name", c[1] }, { "description", c[2] }, { "imageUrl", "" }, { "quantity", inv[c[0]] },
                        });
                    }
                    return ZokoResponse.Success(list.GetRange(Math.Min(offset, list.Count), Math.Min(limit, Math.Max(0, list.Count - offset))));
                }
                case "store.buyItem":
                {
                    string id = ZokoJson.Str(a, "itemId");
                    if (id == "") return Invalid("itemId must be a non-empty string");
                    bool known = false;
                    foreach (var c in Catalog) if (c[0] == id) known = true;
                    if (!known) return ZokoResponse.Fail("purchaseInitFailed", "No catalogue item " + id);
                    inv[id] = (inv.ContainsKey(id) ? ZokoJson.Num(inv, id) : 0) + 1;
                    SaveInventory(inv);
                    Log("simulated purchase of " + id);
                    return ZokoResponse.Success(new Dictionary<string, object> { { "itemId", id } });
                }
                default: // store.consumeItem
                {
                    string sku = ZokoJson.Str(a, "sku");
                    double q = ZokoJson.Num(a, "quantity");
                    if (sku == "") return Invalid("sku must be a non-empty string");
                    if (q < 1) return Invalid("quantity must be an integer >= 1");
                    if (!inv.ContainsKey(sku) || ZokoJson.Num(inv, sku) < q) return ZokoResponse.Fail("insufficientItemQuantity", "Not enough of " + sku);
                    double left = ZokoJson.Num(inv, sku) - q;
                    if (left <= 0) inv.Remove(sku); else inv[sku] = left;
                    SaveInventory(inv);
                    return ZokoResponse.Success(null);
                }
            }
        }

        static void RaiseGlobal(string name, object data)
        {
            ZokoSDKBridge.Instance.SendMessage("OnEvent",
                ZokoJson.Serialize(new Dictionary<string, object> { { "id", 0 }, { "name", name }, { "data", data } }));
        }

        static Dictionary<string, object> Err(string code, string message)
        {
            return new Dictionary<string, object> { { "code", code }, { "message", message } };
        }
    }
}
