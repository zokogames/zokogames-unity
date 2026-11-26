# Changelog

## 1.8.0 (2026-11-25)

- Fix: the editor simulator rejected an empty `encryptedScore` in `submitScore` with `invalidArgument`; like the SDK, it now accepts it (the value for a leaderboard without an encryption key).
- Hide and show all banners: `ZokoSDK.Banner.HideAllBanners(cb)` / `HideAllBannersAsync()` and `ShowAllBanners(cb)` / `ShowAllBannersAsync()` for `banner.hideAllBanners` / `showAllBanners`. Banners are hidden, not cleared.
- Score encryption: `ZokoSDK.User.EncryptScore(score, key, cb, earnedAt)` / `EncryptScoreAsync(score, key, earnedAt)` for `user.encryptScore`. It resolves the base64 payload to pass to `SubmitScore`; guests get `userNotAuthenticated`, a bad score or key `invalidArgument`; `earnedAt` (Unix ms) seals a delayed copy, 0 is a live run. The local mock implements all three calls.
- `ZokoSDK.Game.GameId` for `game.gameId` (empty before init; the Editor mock returns `simulated-game`).
- IndexedDB mirror for `PlayerPrefs` and `persistentDataPath`: the WebGL template variable `ZOKO_MIRROR_INDEXEDDB` = true (or `window.ZOKO_MIRROR_INDEXEDDB = true`) restores the cloud copy before Unity starts and mirrors later writes (`data.mirrorIndexedDB`); `ZokoSDK.Data.StopMirrorIndexedDB()` stops it (no-op in the Editor).
- The plugin now targets the JavaScript SDK 1.8.0; the wrapper constant `ZokoSDK.Version` now reads `"1.8.0"`.

## 1.7.0 (2026-11-23)

- Room left event: `ZokoSDK.Game.Room.Left += (reason, roomId) => ...` for `game.room.addLeftListener` / `removeLeftListener`. The reason is `left`, `kicked`, `closed` or `disconnected`; the room id may be null.
- Custom analytics events: `ZokoSDK.Analytics.Track(name, params, cb)` / `TrackAsync` for `analytics.track`; the local mock checks the name.
- The plugin now targets the JavaScript SDK 1.7.0; the wrapper constant `ZokoSDK.Version` now reads `"1.7.0"`.
- `data.mirrorIndexedDB` is not bound yet: it must run in the page before the engine boots.
- `game.gameId` is still not bound.

## Unreleased

- No plugin change: targets the JavaScript SDK 1.6.4 (delayed scores are not wrapped). In local mode the mock now refuses a midgame ad with `adCooldown` until the game has called gameplay start (or 30 seconds of visible page time have passed): call `ZokoSDK.Game.GameplayStart()` when play begins, before the first midgame ad. The wrapper constant that reports the SDK version now reads `"1.6.4"` in this source; the published zips keep reporting the version they were built with until the next plugin release. Nothing to do for games already built with the plugin.
- Targets the JavaScript SDK 1.6.3 (messages and console warnings only, plus one fix: `ad.requestAd` resolves when the callbacks object cannot be read; no wrapper API change and no wrapper change is needed). The wrapper constant that reports the SDK version now reads `"1.6.3"` in this source; the published zips keep reporting the version they were built with until the next plugin release. Nothing to do for games already built with the plugin.
- Targets the JavaScript SDK 1.6.2 (additive: one-time console notices, reserved host labels at any depth, read-only `SDK.game.gameId`, which the wrapper does not expose yet; no wrapper change is needed). The wrapper constant that reports the SDK version now reads `"1.6.2"` in this source; the published zips report `"1.6.1"` until the next plugin release. Nothing to do for games already built with this plugin.

## 1.6.4 (2026-11-21)

- Targets the JavaScript SDK 1.6.1 (a security hardening of the SDK itself: init is answered only by portal pages, a local page counts only for a local game; no wrapper API change). The constant that reports the SDK release now reads `"1.6.1"` (`ZokoSDK.Version`). Nothing to do for games already built with this plugin.
- Fix: a second ad request while an ad plays is refused by the SDK with an ad error ("Another ad request is already in progress"). That refused request released the wrapper's pause of `AudioListener`, so the game heard its sound again under the ad still on screen. Only the request whose ad started releases it now; the refused request still reports its error.

## 1.6.3 (2026-11-21)

- Still targets the JavaScript SDK 1.6.0 (`ZokoSDK.Version` stays `"1.6.0"`); this release fixes plugin code only.
- Security: `ZokoJson.Parse` stops at 64 levels of nesting (`ZokoJson.MaxDepth`) and throws `FormatException` past it. The
  parser reads messages from the portal and data written by other players (room state, invites); deeply nested input used to
  recurse until the stack overflowed, which no `catch` stops. Every caller already handles `FormatException`: a bad message is
  logged and dropped, as before. No public API change.
- Verified: an EditMode test in a Unity 6 (6000.3.28f1) game parses 64 levels, refuses 65, and refuses 100,000 unclosed `[` and
  100,000 nested objects with `FormatException`.

## 1.6.2 (2026-11-21)

- Still targets the JavaScript SDK 1.6.0 (`ZokoSDK.Version` stays `"1.6.0"`); this release fixes plugin code only.
- Fix: `ZokoSDK.Data.LastError` was set by a failed read but never cleared by a successful one, so after one refused call a
  missing key (null, no error) looked like a refused read. Every `Data` read now sets `LastError` like every write does: null on
  success, the error otherwise.
- The shared bridge core (`.jslib`) validates like the SDK: store page `limit` / `offset` reach the SDK unchanged (an invalid
  one is now `invalidArgument` instead of the default page), `GetUserToken` passes the key type on (`""` keeps the default),
  a room `Send` target must be a member id, `SetState` refuses a `remove` that is not a list of keys and a `patch` that is not an
  object, and `Store.SetSandbox` takes only a boolean.
- Verified: run in Unity 6 (6000.3.28f1) as an embedded package in a full URP game. Import and compile with no warnings; a
  release WebGL build (Brotli, IL2CPP high stripping) ran in Chrome against the production SDK, in the portal frame as a guest
  and in local mode, with no console errors.

## 1.6.1 (2026-11-20)

- Still targets the JavaScript SDK 1.6.0 (`ZokoSDK.Version` stays `"1.6.0"`); this release fixes plugin code only. Checked against Unity's documentation, not yet compiled in an editor.
- Fix: `ZokoGames.Runtime.asmdef` now references `UnityEngine.UI` (`ZokoBanner` uses `UnityEngine.UI.Graphic`, so the runtime assembly did not compile), and `package.json` declares the dependency `com.unity.ugui` 1.0.0.
- Fix: with Enter Play Mode Options and Domain Reload off, static state survived between Play sessions (`Init` returned at once, event handlers piled up). `ZokoSDK` and `ZokoSDKBridge` now reset their statics on `RuntimeInitializeLoadType.SubsystemRegistration`. No public API change.
- Fix: `ZokoSDKBridge` calls `DontDestroyOnLoad` only when `Application.isPlaying`.
- New: every file and folder ships with a `.meta` file (stable GUIDs from `tools/unity-meta.mjs`; the `.jslib` is enabled for WebGL only), so the package no longer depends on Unity writing them on import.
- Docs: the install steps name the uGUI dependency.
- Verified: every C# file (Runtime in its editor and WebGL-player configurations, Editor, both samples) compiles with the .NET 8 C# compiler at C# 9 with warnings as errors, against hand-written stubs of the Unity API (`tools/engine-verify/verify-unity.sh`); the jslib exports, asmdefs, `package.json` and `.meta` GUIDs pass `tools/engine-verify/unity-static.mjs`. Still not run in the Unity editor.
- Fix: the simulator's `_inviteParams` field was never assigned, so every editor compile printed warning CS0649 in the Console. It is now an explicit read-only `null` (the simulator never receives an invite, as before).

## 1.6.0 (2026-11-19)

- Matches the JavaScript SDK 1.6.0 (additive): `ZokoRoomUpdate.IsPublic` (`bool?`, null keeps it unchanged): the host's friends can join from their friends list. Private by default; only the room host's call counts; ignored in kids mode.
- Adds `ZokoSDK.CopyToClipboard(text)` (WebGL: new `.jslib` entry point `ZokoSDK_CopyText`; elsewhere `GUIUtility.systemCopyBuffer`) and the `PlayerPrefs` mirror `ZokoSDK.Data.PushPlayerPrefs` / `PullPlayerPrefs` with the `ZokoPrefType` enum. Additive; no change to the shared bridge core.

## 1.5.0 (2026-11-19)

- Matches the JavaScript SDK 1.5.0 (additive): `game.settings` carries `kidsMode` (true when the game runs in kids mode: no chat, rooms, sign-in prompts or personalised ads). A portal that predates it does not send the field; keep honouring `disableChat`. The simulator/editor mock reports `kidsMode: false` unless kids mode is switched on, and then also turns chat off, like the portal. The SDK's new `LeaderboardEntry` type (leaderboard rows: `isYou`, opaque per-game `userId`) is type only and has no wrapper binding, as the SDK has no leaderboard read call. No existing API changes.
- `ZokoGameSettings.KidsMode` (bool, false when absent). Simulator: `ZokoSDKSettings.kidsMode`.

## 1.4.1 (2026-11-19)

- Matches the JavaScript SDK 1.4.1 (a fix release with no API change: inside the portal the handshake waits up to 15 s instead of 1.5 s, per-key user tokens, game calls that resolve with a warning when the portal does not answer, cheaper cloud-save writes with retried syncs). No wrapper API change from the SDK side.

- After an ad, the sign-in or account-link prompt or a checkout, keyboard focus is handed back to the game frame and its canvas (shared bridge core), so keys work without a click.
- New `ZokoSDK.Ad.FollowPortalMute` (default true): the portal's mute setting is applied to `AudioListener.pause` after init and on every change, and an ad no longer un-pauses audio the portal muted. Samples no longer set `AudioListener.pause` themselves.
- An unreadable SDK response now fails with the documented code `other` instead of the undocumented `invalidResponse`.
- README: rows for `ad.requestAd` callbacks, `ERROR_CODE_DESCRIPTIONS`, `SDK_VERSION`; `ZokoErrorCodes.Room` listed. New `TESTING.md` (manual smoke checklist).

## 1.4.0

- Matches the JavaScript SDK 1.4.0 (`game.reportChat`, `game.onScreenshotRequest`, `data.mirrorLocalStorage`, all additive). Real engine bindings for all three: chat reports, a screenshot provider (with a default end-of-frame capture) and the localStorage mirror, each with editor/mock behaviour and tests. The mirror only sees browser localStorage, not the IndexedDB saves most engines use. No existing API changes.

## 1.3.1

- Matches the JavaScript SDK 1.3.1 (a fix release: the ad request times out after 30 s when the host never answers, malformed host messages are ignored, `SDK.init()` is safe without `window`, loading the script twice keeps the first instance, and `ERROR_CODE_DESCRIPTIONS` is exported). No wrapper API change.

## 1.3.0

- Matches the JavaScript SDK 1.3.0: adds the room data channel (`game.room`): `ZokoSDK.Game.Room` with `Send` / `SendTo` / `SendJson` / `SendTyped<T>` / `SetState` / `SetStateJson` (callback and `...Async` forms), `Members`, `State`, `GetState<T>()`, `IsHost`, `SelfId`, the C# events `MessageReceived`, `StateChanged`, `MembersChanged`, `HostChanged`, `Error`, the `ZokoRoomMember` / `ZokoRoomMessage` / `ZokoRoomStateChange` types and `ZokoErrorCodes.Room`. The editor simulator loops messages and state changes back to the one simulated member. No `.jslib` change: the shared bridge carries the new events.

## 1.2.0

- Adds the `achievements` module (additive): `Achievements.List` / `Unlock` with `...Async` variants, the `Achievements.OnUnlock` event, `ZokoAchievement` and `ZokoUnlockResult`, and the editor simulator.
- Matches the JavaScript SDK 1.2.0: adds offline awareness, a read-only offline flag plus a change notification (see the README coverage table for `game.isOffline`, `game.addOfflineListener`, `game.removeOfflineListener`).

## 1.1.1

- Matches the JavaScript SDK 1.1.1: the banner refresh floor now holds across clear and request cycles, and local mode reads invite parameters from the page URL.

## 1.1.0

- Matches the JavaScript SDK 1.1.0: `store.getPurchaseToken` (`user.getXsollaUserToken` stays as a deprecated alias) and the `ERROR_CODES` tables, with a purchases example.

## 1.0.0

- Full coverage of the JavaScript SDK 1.0.0: ad, banner, game, user, data, store, analytics, events and error codes.
- New in this release: `game.loadingProgress`, `game.showInviteButton` / `hideInviteButton`, `game.isInstantJoin`,
  `store.getPurchaseToken` (`user.getXsollaUserToken` kept as a deprecated alias), the `ERROR_CODES` tables, the whole `store` module (catalogue, inventory, buy, consume, sandbox).
- Added the `Version` constant, store simulator, loading progress in the WebGL template, and the Demo store key.
- Coverage is enforced: `node tools/check-coverage.mjs` fails when this README's API table misses a JS SDK member.
