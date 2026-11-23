using System;
using System.Collections.Generic;

namespace ZokoGames
{
    public enum ZokoAdType { Midgame, Rewarded }

    /// <summary>The type of a PlayerPrefs key for <c>ZokoSDK.Data.PushPlayerPrefs</c> / <c>PullPlayerPrefs</c> (PlayerPrefs cannot report it).</summary>
    public enum ZokoPrefType { String, Int, Float }

    /// <summary>Why a chat message is reported (<c>SDK.game.reportChat</c> <c>reason</c>).</summary>
    public enum ZokoChatReportReason { Abuse, Spam, Cheating, Other }

    public enum ZokoSigningKeyType { Default, InGameAuth, InGameAuthLegacy }

    /// <summary>Plain error object every SDK failure uses (mirrors <c>{ code, message }</c>).</summary>
    public sealed class ZokoError
    {
        public string Code;
        public string Message;
        public string ContainerId;

        public ZokoError(string code, string message, string containerId = null)
        {
            Code = code; Message = message; ContainerId = containerId;
        }

        public override string ToString() { return Code + ": " + Message; }

        /// <summary>True when this error carries the given code, e.g. <c>err.Is(ZokoErrorCodes.Store.PurchaseCancelled)</c>.</summary>
        public bool Is(string code) { return Code == code; }

        internal static ZokoError From(object json)
        {
            if (json == null) return new ZokoError("other", "Unknown error");
            return new ZokoError(
                ZokoJson.Str(json, "code", "other"),
                ZokoJson.Str(json, "message", ""),
                ZokoJson.Get(json, "containerId") as string);
        }
    }

    /// <summary>Every error code an SDK call can raise, grouped by module (the JS SDK's <c>ERROR_CODES</c>).</summary>
    public static class ZokoErrorCodes
    {
        public static class Ad
        {
            public const string AdsDisabled = "adsDisabled", Unfilled = "unfilled", Adblock = "adblock", AdCooldown = "adCooldown", Other = "other";
            public static readonly string[] All = { AdsDisabled, Unfilled, Adblock, AdCooldown, Other };
        }
        public static class Banner
        {
            public const string BannersDisabled = "bannersDisabled", Unfilled = "unfilled", MissingId = "missingId", NotVisible = "notVisible",
                NoAvailableSizes = "noAvailableSizes", NotCreated = "notCreated", VideoAdPlaying = "videoAdPlaying", InvalidSize = "invalidSize",
                BannerCooldown = "bannerCooldown", MaxRefreshReached = "maxRefreshReached", Other = "other";
            public static readonly string[] All = { BannersDisabled, Unfilled, MissingId, NotVisible, NoAvailableSizes, NotCreated, VideoAdPlaying,
                InvalidSize, BannerCooldown, MaxRefreshReached, Other };
        }
        public static class User
        {
            public const string UserNotAuthenticated = "userNotAuthenticated", UserAlreadySignedIn = "userAlreadySignedIn", UserCancelled = "userCancelled",
                ShowAuthPromptInProgress = "showAuthPromptInProgress", ShowAccountLinkPromptInProgress = "showAccountLinkPromptInProgress",
                RateLimited = "rateLimited", RequestInProgress = "requestInProgress", UnexpectedError = "unexpectedError";
            public static readonly string[] All = { UserNotAuthenticated, UserAlreadySignedIn, UserCancelled, ShowAuthPromptInProgress,
                ShowAccountLinkPromptInProgress, RateLimited, RequestInProgress, UnexpectedError };
        }
        public static class Data
        {
            public const string DataLimitExceeded = "dataLimitExceeded", DataModuleDisabled = "dataModuleDisabled", Other = "other";
            public static readonly string[] All = { DataLimitExceeded, DataModuleDisabled, Other };
        }
        public static class Store
        {
            public const string CatalogFetchFailed = "catalogFetchFailed", InventoryFetchFailed = "inventoryFetchFailed", PurchaseInitFailed = "purchaseInitFailed",
                PurchaseInProgress = "purchaseInProgress", PurchaseCancelled = "purchaseCancelled", ConsumeItemFailed = "consumeItemFailed",
                ItemNotConsumable = "itemNotConsumable", ItemNotInInventory = "itemNotInInventory", InsufficientItemQuantity = "insufficientItemQuantity",
                UserNotAuthenticated = "userNotAuthenticated", InvalidArgument = "invalidArgument", RequestInProgress = "requestInProgress",
                UnexpectedError = "unexpectedError", Other = "other";
            public static readonly string[] All = { CatalogFetchFailed, InventoryFetchFailed, PurchaseInitFailed, PurchaseInProgress, PurchaseCancelled,
                ConsumeItemFailed, ItemNotConsumable, ItemNotInInventory, InsufficientItemQuantity, UserNotAuthenticated, InvalidArgument,
                RequestInProgress, UnexpectedError, Other };
        }
        public static class Room
        {
            public const string NotInRoom = "notInRoom", NotHost = "notHost", PayloadTooLarge = "payloadTooLarge", StateTooLarge = "stateTooLarge",
                UnknownMember = "unknownMember", RateLimited = "rateLimited", RoomUnavailable = "roomUnavailable", InvalidArgument = "invalidArgument";
            public static readonly string[] All = { NotInRoom, NotHost, PayloadTooLarge, StateTooLarge, UnknownMember, RateLimited, RoomUnavailable, InvalidArgument };
        }
        public static class General
        {
            public const string SdkNotInitialized = "sdkNotInitialized", SdkNotLoaded = "sdkNotLoaded", SdkDisabled = "sdkDisabled", InvalidArgument = "invalidArgument", Timeout = "timeout";
            public static readonly string[] All = { SdkNotInitialized, SdkNotLoaded, SdkDisabled, InvalidArgument, Timeout };
        }
    }

    /// <summary>Thrown by the <c>...Async</c> methods.</summary>
    public sealed class ZokoException : Exception
    {
        public ZokoError Error { get; private set; }
        public string Code { get { return Error.Code; } }
        public ZokoException(ZokoError error) : base(error.ToString()) { Error = error; }
    }

    public sealed class ZokoUser
    {
        public string Username;
        public string ProfilePictureUrl;
        /// <summary>Do not trust on the client; verify the token on your server instead.</summary>
        public string UserId;

        internal static ZokoUser From(object j)
        {
            if (!(j is Dictionary<string, object>)) return null;
            return new ZokoUser
            {
                Username = ZokoJson.Str(j, "username"),
                ProfilePictureUrl = ZokoJson.Str(j, "profilePictureUrl"),
                UserId = ZokoJson.Str(j, "__dangerousUserId"),
            };
        }
    }

    public sealed class ZokoFriend
    {
        public string Id;
        public string Username;
        public string ProfilePictureUrl;
    }

    public sealed class ZokoFriendsPage
    {
        public List<ZokoFriend> Friends = new List<ZokoFriend>();
        public int Page;
        public int Size;
        public int Total;
        public bool HasMore;

        internal static ZokoFriendsPage From(object j)
        {
            var p = new ZokoFriendsPage
            {
                Page = (int)ZokoJson.Num(j, "page"),
                Size = (int)ZokoJson.Num(j, "size"),
                Total = (int)ZokoJson.Num(j, "total"),
                HasMore = ZokoJson.Bool(j, "hasMore"),
            };
            var arr = ZokoJson.Get(j, "friends") as List<object>;
            if (arr != null)
                foreach (var f in arr)
                    p.Friends.Add(new ZokoFriend
                    {
                        Id = ZokoJson.Str(f, "id"),
                        Username = ZokoJson.Str(f, "username"),
                        ProfilePictureUrl = ZokoJson.Str(f, "profilePictureUrl"),
                    });
            return p;
        }
    }

    public struct ZokoGameSettings
    {
        public bool DisableChat;
        /// <summary>
        /// SDK 1.5.0: true when the game runs in kids mode (no chat, rooms, sign-in prompts or personalised ads). False when
        /// the portal predates the field, so keep honouring <see cref="DisableChat"/> as well.
        /// </summary>
        public bool KidsMode;
        public bool MuteAudio;

        internal static ZokoGameSettings From(object j)
        {
            return new ZokoGameSettings { DisableChat = ZokoJson.Bool(j, "disableChat"), KidsMode = ZokoJson.Bool(j, "kidsMode"), MuteAudio = ZokoJson.Bool(j, "muteAudio") };
        }
    }

    public sealed class ZokoSystemInfo
    {
        public string CountryCode = "";
        public string Locale = "";
        /// <summary>"desktop", "tablet" or "mobile".</summary>
        public string DeviceType = "desktop";
        public string OsName = "";
        public string OsVersion = "";
        public string BrowserName = "";
        public string BrowserVersion = "";
        /// <summary>"web", "pwa", "google_play_store" or "apple_store".</summary>
        public string ApplicationType = "web";

        internal static ZokoSystemInfo From(object j)
        {
            var s = new ZokoSystemInfo();
            if (!(j is Dictionary<string, object>)) return s;
            s.CountryCode = ZokoJson.Str(j, "countryCode");
            s.Locale = ZokoJson.Str(j, "locale");
            s.DeviceType = ZokoJson.Str(ZokoJson.Get(j, "device"), "type", "desktop");
            s.OsName = ZokoJson.Str(ZokoJson.Get(j, "os"), "name");
            s.OsVersion = ZokoJson.Str(ZokoJson.Get(j, "os"), "version");
            s.BrowserName = ZokoJson.Str(ZokoJson.Get(j, "browser"), "name");
            s.BrowserVersion = ZokoJson.Str(ZokoJson.Get(j, "browser"), "version");
            s.ApplicationType = ZokoJson.Str(j, "applicationType", "web");
            return s;
        }
    }

    /// <summary>Instant-multiplayer room update; leave a field null to keep it unchanged.</summary>
    public sealed class ZokoRoomUpdate
    {
        public string RoomId;
        public bool? IsJoinable;
        /// <summary>SDK 1.6.0: let the host's friends join from their friends list. Private by default; host only; ignored in kids mode.</summary>
        public bool? IsPublic;
        public Dictionary<string, string> InviteParams;

        internal Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            if (RoomId != null) d["roomId"] = RoomId;
            if (IsJoinable.HasValue) d["isJoinable"] = IsJoinable.Value;
            if (IsPublic.HasValue) d["isPublic"] = IsPublic.Value;
            if (InviteParams != null) d["inviteParams"] = InviteParams;
            return d;
        }
    }

    /// <summary>One line of chat shown to the moderators next to a report (oldest first).</summary>
    public sealed class ZokoChatLine
    {
        public string Name = "";
        public string Text = "";
        public ZokoChatLine() { }
        public ZokoChatLine(string name, string text) { Name = name; Text = text; }
    }

    /// <summary>Arguments of <c>ZokoSDK.Game.ReportChat</c> (JS: <c>SDK.game.reportChat</c>). The SDK validates the limits and answers <c>invalidArgument</c>.</summary>
    public sealed class ZokoChatReport
    {
        /// <summary>Display name of the reported player, 1 to 40 characters.</summary>
        public string ReportedName = "";
        /// <summary>The reported message, 1 to 500 characters.</summary>
        public string Message = "";
        public ZokoChatReportReason Reason = ZokoChatReportReason.Other;
        /// <summary>The reported player's platform id when known (for example a room member id), or null.</summary>
        public string ReportedUserId;
        /// <summary>The room the message was sent in (at most 64 characters), or null.</summary>
        public string RoomId;
        /// <summary>Up to 10 recent lines, oldest first (longer lists are cut to the newest 10), or null.</summary>
        public List<ZokoChatLine> Context;

        internal Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>
            {
                { "reportedName", ReportedName ?? "" },
                { "message", Message ?? "" },
                { "reason", Reason.ToString().ToLowerInvariant() },
            };
            if (ReportedUserId != null) d["reportedUserId"] = ReportedUserId;
            if (RoomId != null) d["roomId"] = RoomId;
            if (Context != null)
            {
                var lines = new List<object>();
                foreach (var l in Context)
                    if (l != null) lines.Add(new Dictionary<string, object> { { "name", l.Name ?? "" }, { "text", l.Text ?? "" } });
                d["context"] = lines;
            }
            return d;
        }
    }

    /// <summary>A player in the current room (<c>SDK.game.room.members()</c>). <see cref="Id"/> is platform-assigned.</summary>
    public sealed class ZokoRoomMember
    {
        public string Id = "";
        public string Username = "";
        public string ProfilePictureUrl = "";

        internal static ZokoRoomMember From(object j)
        {
            if (!(j is Dictionary<string, object>)) return null;
            return new ZokoRoomMember
            {
                Id = ZokoJson.Str(j, "id"), Username = ZokoJson.Str(j, "username"), ProfilePictureUrl = ZokoJson.Str(j, "profilePictureUrl"),
            };
        }

        internal static List<ZokoRoomMember> ListFrom(object j)
        {
            var list = new List<ZokoRoomMember>();
            var arr = j as List<object>;
            if (arr != null)
                foreach (var x in arr)
                {
                    var m = From(x);
                    if (m != null) list.Add(m);
                }
            return list;
        }
    }

    /// <summary>A message from another player (<c>SDK.game.room.onMessage</c>). Never raised for your own messages on a real portal.</summary>
    public sealed class ZokoRoomMessage
    {
        /// <summary>Platform id of the sender, set by the platform.</summary>
        public string From = "";
        /// <summary>True when the message was sent to you alone.</summary>
        public bool Direct;
        /// <summary>Increases for every message in the room; order by it.</summary>
        public long Seq;
        /// <summary>Server time in milliseconds.</summary>
        public long Ts;
        /// <summary>Parsed payload: Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool or null.</summary>
        public object Data;

        /// <summary>The payload as a JSON string (for JsonUtility, Newtonsoft or your own parser).</summary>
        public string Json { get { return ZokoJson.Serialize(Data); } }

        /// <summary>Deserialise the payload into a [Serializable] type with <see cref="UnityEngine.JsonUtility"/>.</summary>
        public T As<T>() { return UnityEngine.JsonUtility.FromJson<T>(Json); }

        /// <summary>Read a field of an object payload, or null.</summary>
        public object Get(string key) { return ZokoJson.Get(Data, key); }

        internal static ZokoRoomMessage Parse(object j)
        {
            if (!(j is Dictionary<string, object>)) return null;
            return new ZokoRoomMessage
            {
                From = ZokoJson.Str(j, "from"), Direct = ZokoJson.Bool(j, "direct"), Seq = (long)ZokoJson.Num(j, "seq"),
                Ts = (long)ZokoJson.Num(j, "ts"), Data = ZokoJson.Get(j, "data"),
            };
        }
    }

    /// <summary>What a shared-state update changed (<c>SDK.game.room.onState</c>).</summary>
    public sealed class ZokoRoomStateChange
    {
        /// <summary>Player id of the host who wrote it, or null when unknown.</summary>
        public string By;
        /// <summary>Keys that were set, changed or removed (all keys for a full resync).</summary>
        public List<string> Keys = new List<string>();
        public long Rev;

        internal static ZokoRoomStateChange From(object j)
        {
            var c = new ZokoRoomStateChange { By = ZokoJson.Get(j, "by") as string, Rev = (long)ZokoJson.Num(j, "rev") };
            var arr = ZokoJson.Get(j, "keys") as List<object>;
            if (arr != null) foreach (var k in arr) c.Keys.Add(k as string ?? "");
            return c;
        }
    }

    public sealed class ZokoPrice
    {
        public double Amount;
        public string Currency = "";
    }

    /// <summary>A purchasable catalogue entry.</summary>
    public sealed class ZokoStoreItem
    {
        public string Sku = "";
        public string Name = "";
        public string Description = "";
        public string ImageUrl = "";
        /// <summary>Null when the item has no listed price.</summary>
        public ZokoPrice Price;
        /// <summary>True when <see cref="ZokoSDK.Store.ConsumeItem(string,int,System.Action{ZokoError})"/> may be used on it.</summary>
        public bool Consumable;

        internal static ZokoStoreItem From(object j)
        {
            var i = new ZokoStoreItem
            {
                Sku = ZokoJson.Str(j, "sku"), Name = ZokoJson.Str(j, "name"), Description = ZokoJson.Str(j, "description"),
                ImageUrl = ZokoJson.Str(j, "imageUrl"), Consumable = ZokoJson.Bool(j, "consumable"),
            };
            var pr = ZokoJson.Get(j, "price");
            if (pr is Dictionary<string, object>) i.Price = new ZokoPrice { Amount = ZokoJson.Num(pr, "amount"), Currency = ZokoJson.Str(pr, "currency") };
            return i;
        }
    }

    /// <summary>An owned item. <see cref="Quantity"/> is null when the payment provider does not count it.</summary>
    public sealed class ZokoInventoryItem
    {
        public string Sku = "";
        public string Name = "";
        public string Description = "";
        public string ImageUrl = "";
        public int? Quantity;

        internal static ZokoInventoryItem From(object j)
        {
            var i = new ZokoInventoryItem
            {
                Sku = ZokoJson.Str(j, "sku"), Name = ZokoJson.Str(j, "name"), Description = ZokoJson.Str(j, "description"),
                ImageUrl = ZokoJson.Str(j, "imageUrl"),
            };
            var q = ZokoJson.Get(j, "quantity");
            if (q is double) i.Quantity = (int)(double)q;
            else if (q is long) i.Quantity = (int)(long)q;
            else if (q is int) i.Quantity = (int)q;
            return i;
        }
    }

    /// <summary>A game achievement and the player's progress on it.</summary>
    public sealed class ZokoAchievement
    {
        public string Key = "";
        public string Name = "";
        public string Description = "";
        /// <summary>Null when the achievement has no icon.</summary>
        public string IconUrl;
        public int Points;
        public bool Hidden;
        public bool Unlocked;
        /// <summary>ISO timestamp, null while locked.</summary>
        public string UnlockedAt;

        internal static ZokoAchievement From(object j)
        {
            if (!(j is Dictionary<string, object>)) return null;
            return new ZokoAchievement
            {
                Key = ZokoJson.Str(j, "key"), Name = ZokoJson.Str(j, "name"), Description = ZokoJson.Str(j, "description"),
                IconUrl = ZokoJson.Get(j, "iconUrl") as string, Points = (int)ZokoJson.Num(j, "points"),
                Hidden = ZokoJson.Bool(j, "hidden"), Unlocked = ZokoJson.Bool(j, "unlocked"),
                UnlockedAt = ZokoJson.Get(j, "unlockedAt") as string,
            };
        }
    }

    /// <summary>Outcome of <see cref="ZokoSDK.Achievements.Unlock"/>. Guests and unknown keys resolve with <see cref="Ok"/> false.</summary>
    public sealed class ZokoUnlockResult
    {
        public bool Ok;
        /// <summary>True only when this call unlocked it; false when it was already unlocked.</summary>
        public bool Unlocked;
        /// <summary>Set when <see cref="Ok"/> is true.</summary>
        public ZokoAchievement Achievement;
        /// <summary>"guest", "not_found", "not_live", "rate_limited" or "error" when <see cref="Ok"/> is false.</summary>
        public string Reason = "";

        internal static ZokoUnlockResult From(object j)
        {
            return new ZokoUnlockResult
            {
                Ok = ZokoJson.Bool(j, "ok"), Unlocked = ZokoJson.Bool(j, "unlocked"),
                Achievement = ZokoAchievement.From(ZokoJson.Get(j, "achievement")), Reason = ZokoJson.Str(j, "reason"),
            };
        }
    }

    internal sealed class ZokoResponse
    {
        public bool Ok;
        public object Result;
        public ZokoError Error;

        public static ZokoResponse Success(object result) { return new ZokoResponse { Ok = true, Result = result }; }
        public static ZokoResponse Fail(string code, string message, string containerId = null)
        {
            return new ZokoResponse { Ok = false, Error = new ZokoError(code, message, containerId) };
        }
    }

    internal interface IZokoTransport
    {
        /// <summary>Asynchronous call; <paramref name="onEvent"/> receives per-request events (adStarted...).</summary>
        void Call(string method, Dictionary<string, object> args, Action<string, object> onEvent, Action<ZokoResponse> done);
        /// <summary>Synchronous call (data and cached getters).</summary>
        ZokoResponse CallSync(string method, Dictionary<string, object> args);
    }
}
