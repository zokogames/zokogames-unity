// ZokoGames SDK bridge for Unity WebGL.
// Talks to window.ZokoGames.SDK (loaded by the WebGL template) and answers the hidden
// ZokoSDKBridge GameObject through SendMessage. Every async call carries a request id.
mergeInto(LibraryManager.library, {
  // <zoko-core>
  /*
   * ZokoGames engine bridge core (ES5, no dependencies).
   *
   * Single source of truth. tools/sync-core.mjs copies this function between the
   * `zoko-core` markers of every bridge file (Unity jslib, Godot shell, GameMaker,
   * Defold). Do not edit the copies by hand.
   *
   * Wire protocol shared by those engines:
   *   call(method, args, onEvent) -> Promise<result>     (async methods)
   *   callSync(method, args)      -> { ok, result } | { ok:false, error }
   *   setGlobalHandler(fn(name, data))   names: settingsChange, offlineChange, auth, joinRoom, achievementUnlocked,
   *   roomMessage, roomState, roomMembers, roomHost, roomError (SDK 1.3.0 game.room), roomLeft (SDK 1.7.0), screenshotRequest, mirrorError (SDK 1.4.0)
   * SDK 1.4.0 calls: game.reportChat {report}; game.setScreenshotProvider {enabled} + the global event screenshotRequest, which the
   * engine answers with game.answerScreenshot {dataUrl} (a data:image/... URL, or null when it cannot capture; the SDK waits 3 s);
   * data.mirrorLocalStorage {keys?, prefix?, pattern?} (+ data.stopMirrorLocalStorage), failures arrive as the global event mirrorError.
   * Errors are always plain { code, message, containerId? } objects.
   * Banner rectangles arrive as normalised { nx, ny, nw, nh } (0..1, top-left origin,
   * relative to the game canvas) and are mapped to a fixed-position container <div>.
   * Calls that show portal UI over the game (ad.requestAd, user.showAuthPrompt, user.showAccountLinkPrompt, store.buyItem) give
   * keyboard focus back to the game frame and its canvas when they settle, so key presses reach the engine again.
   */
  $zokoCreateCore: function (root, getCanvas) {
    var globalHandler = function () {};
    var hooked = false;
    var noop = function () {};

    function sdk() {
      var z = root.ZokoGames;
      return z && z.SDK ? z.SDK : null;
    }
    function need() {
      var s = sdk();
      if (!s) throw { code: 'sdkNotLoaded', message: 'The ZokoGames SDK script is not loaded' };
      return s;
    }
    function plainError(e) {
      if (e && typeof e.code === 'string') {
        var o = { code: e.code, message: String(e.message || e.code) };
        if (typeof e.containerId === 'string') o.containerId = e.containerId;
        return o;
      }
      return { code: 'other', message: e && e.message ? String(e.message) : String(e) };
    }
    /** Portal overlays (ads, prompts, checkout) take keyboard focus; hand it back to this frame and the game canvas. */
    function regainFocus() {
      try {
        if (typeof root.focus === 'function') root.focus();
        var c = getCanvas && getCanvas();
        if (c && typeof c.focus === 'function') {
          if (!c.hasAttribute || !c.hasAttribute('tabindex')) c.setAttribute('tabindex', '-1');
          c.focus({ preventScroll: true });
        }
      } catch (e) { /* focus is best effort */ }
    }
    function refocus(p) {
      return p.then(function (v) { regainFocus(); return v; }, function (e) { regainFocus(); throw e; });
    }
    function bad(message) {
      return { code: 'invalidArgument', message: message };
    }
    function snapshot(s) {
      var out = {
        environment: s.environment,
        isQaTool: !!s.isQaTool,
        settings: { disableChat: false, kidsMode: false, muteAudio: false },
        systemInfo: null,
        isUserAccountAvailable: false,
        isInstantMultiplayer: false,
        isOffline: false,
        inviteParams: null,
        storeAvailable: false,
        sdkVersion: s.version || (root.ZokoGames && root.ZokoGames.SDK_VERSION) || ''
      };
      if (s.environment === 'disabled') return out;
      out.settings = s.game.settings;
      out.systemInfo = s.user.systemInfo;
      out.isUserAccountAvailable = s.user.isUserAccountAvailable;
      out.isInstantMultiplayer = s.game.isInstantMultiplayer;
      out.isOffline = !!s.game.isOffline;
      out.inviteParams = s.game.inviteParams;
      out.storeAvailable = !!(s.store && s.store.isAvailable);
      return out;
    }
    /** getPurchaseToken is the primary name; older SDK builds only have the deprecated getXsollaUserToken. */
    function purchaseToken(s) {
      if (s.store && typeof s.store.getPurchaseToken === 'function') return s.store.getPurchaseToken();
      return s.user.getXsollaUserToken();
    }
    function hook(s) {
      if (hooked || s.environment === 'disabled') return;
      hooked = true;
      s.game.addSettingsChangeListener(function (v) { globalHandler('settingsChange', v); });
      // SDK 1.2.0+; older builds have no offline members and the flag stays false.
      if (typeof s.game.addOfflineListener === 'function') s.game.addOfflineListener(function (v) { globalHandler('offlineChange', !!v); });
      s.game.addJoinRoomListener(function (v) { globalHandler('joinRoom', v); });
      s.user.addAuthListener(function (v) { globalHandler('auth', v); });
      // Achievements module (SDK builds that have it); the event carries the achievement object.
      if (s.achievements && typeof s.achievements.onUnlock === 'function') s.achievements.onUnlock(function (v) { globalHandler('achievementUnlocked', v); });
      // Room data channel (SDK 1.3.0+). Events: roomMessage {data,direct,from,seq,ts}, roomState {state,change:{by?,keys,rev}},
      // roomMembers [member], roomHost {hostId,isHost}, roomError {code,message}.
      var r = s.game.room;
      if (r && typeof r.onMessage === 'function') {
        r.onMessage(function (m) { globalHandler('roomMessage', m); });
        r.onState(function (st, change) { globalHandler('roomState', { state: st, change: change }); });
        r.onMembers(function (m) { globalHandler('roomMembers', m); });
        r.onHostChange(function (hostId, isHost) { globalHandler('roomHost', { hostId: hostId, isHost: !!isHost }); });
        r.onError(function (e) { globalHandler('roomError', plainError(e)); });
        // SDK 1.7.0+: the portal ended our membership without the game asking. roomLeft {reason, roomId?}.
        if (typeof r.addLeftListener === 'function') r.addLeftListener(function (e) { globalHandler('roomLeft', { reason: e && e.reason, roomId: e && e.roomId }); });
      }
    }

    /* ---- banner containers ---- */
    function place(id, rect) {
      if (typeof id !== 'string' || id === '') throw { code: 'missingId', message: 'A container id is required' };
      if (!rect || typeof rect.nx !== 'number') throw bad('A banner rectangle is required');
      var doc = root.document;
      var el = doc.getElementById(id);
      if (!el) {
        el = doc.createElement('div');
        el.id = id;
        el.setAttribute('data-zoko-banner', '1');
        el.style.cssText = 'position:fixed;margin:0;padding:0;background:transparent;pointer-events:none;z-index:2147483000;';
        doc.body.appendChild(el);
      }
      var c = getCanvas && getCanvas();
      var b = c ? c.getBoundingClientRect() : { left: 0, top: 0, width: root.innerWidth, height: root.innerHeight };
      el.style.left = b.left + rect.nx * b.width + 'px';
      el.style.top = b.top + rect.ny * b.height + 'px';
      el.style.width = rect.nw * b.width + 'px';
      el.style.height = rect.nh * b.height + 'px';
      return el;
    }
    function removeContainer(id) {
      var el = root.document.getElementById(id);
      if (el && el.getAttribute('data-zoko-banner')) el.parentNode.removeChild(el);
    }
    function hasElement(id) {
      return typeof id === 'string' && id !== '' && !!root.document.getElementById(id);
    }
    /** A container this call created is removed again when the banner request fails, so no empty overlay stays behind. */
    function dropOnFailure(id, fresh) {
      return function (e) {
        if (fresh) removeContainer(id);
        throw e;
      };
    }
    function removeAllContainers() {
      var list = root.document.querySelectorAll('[data-zoko-banner]');
      for (var i = 0; i < list.length; i++) list[i].parentNode.removeChild(list[i]);
    }

    /* ---- SDK 1.4.0: screenshot provider and localStorage mirror ---- */
    var shotStop = null;
    var shotPending = null;
    var mirrorStop = null;
    function settleShot(v) {
      var p = shotPending;
      shotPending = null;
      if (p) p(v);
    }
    function setScreenshotProvider(s, enabled) {
      if (shotStop) { shotStop(); shotStop = null; settleShot(null); }
      if (!enabled) return;
      shotStop = s.game.onScreenshotRequest(function () {
        settleShot(null);
        return new Promise(function (resolve) {
          shotPending = resolve;
          globalHandler('screenshotRequest', null);
        });
      });
    }
    function stopMirror() {
      if (mirrorStop) { mirrorStop(); mirrorStop = null; }
    }

    function simple(mod, fn) {
      return function (a, ev, s) { return s[mod][fn](); };
    }

    function storeOf(s) {
      if (!s.store) throw { code: 'other', message: 'This SDK build has no store module' };
      return s.store;
    }
    function achOf(s) {
      if (!s.achievements) throw { code: 'other', message: 'This SDK build has no achievements module' };
      return s.achievements;
    }
    function roomOf(s) {
      if (!s.game.room) throw { code: 'other', message: 'This SDK build has no game.room module (needs SDK 1.3.0)' };
      return s.game.room;
    }
    function dataKey(a) {
      if (typeof a.key !== 'string') throw bad('A text key is required');
      return a.key;
    }
    /** True when an engine left the argument out (Lua nil, GML undefined, JSON null). */
    function absent(v) {
      return v === undefined || v === null;
    }
    function isArray(v) {
      return Object.prototype.toString.call(v) === '[object Array]';
    }
    function emptyTable(v) {
      if (!v || typeof v !== 'object') return false;
      for (var k in v) if (Object.prototype.hasOwnProperty.call(v, k)) return false;
      return true;
    }
    /**
     * Store page options. A value the engine sends is passed on unchanged, so the SDK rejects a limit of 0 or below, above 50,
     * a fraction or a negative offset with invalidArgument (until 1.6.2 such a value was dropped and the default page came back).
     */
    function pageOf(a) {
      var p = {};
      if (!absent(a.limit)) p.limit = a.limit;
      if (!absent(a.offset)) p.offset = a.offset;
      return p;
    }

    /* ---- async methods ---- */
    var T = {
      'init': function (a, ev) {
        var s = need();
        return s.init().then(function () { hook(s); return snapshot(s); });
      },
      'ad.requestAd': function (a, ev, s) {
        return refocus(s.ad.requestAd(a.type, {
          adStarted: function () { ev('adStarted', null); },
          adFinished: function () { regainFocus(); ev('adFinished', null); },
          adError: function (e) { regainFocus(); ev('adError', plainError(e)); }
        })).then(noop);
      },
      'ad.hasAdblock': simple('ad', 'hasAdblock'),
      'banner.requestBanner': function (a, ev, s) {
        var fresh = !hasElement(a.id);
        place(a.id, a.rect);
        return s.banner.requestBanner({ id: a.id, width: a.width, height: a.height }).then(noop, dropOnFailure(a.id, fresh));
      },
      'banner.requestResponsiveBanner': function (a, ev, s) {
        var fresh = !hasElement(a.id);
        place(a.id, a.rect);
        return s.banner.requestResponsiveBanner(a.id).then(noop, dropOnFailure(a.id, fresh));
      },
      'banner.clearBanner': function (a, ev, s) {
        return s.banner.clearBanner(a.id).then(function () { removeContainer(a.id); });
      },
      'banner.clearAllBanners': function (a, ev, s) {
        return s.banner.clearAllBanners().then(removeAllContainers);
      },
      'game.gameplayStart': simple('game', 'gameplayStart'),
      'game.gameplayStop': simple('game', 'gameplayStop'),
      'game.loadingStart': simple('game', 'loadingStart'),
      'game.loadingStop': simple('game', 'loadingStop'),
      'game.loadingProgress': function (a, ev, s) { return s.game.loadingProgress(a.percentage); },
      'game.happytime': simple('game', 'happytime'),
      'game.leftRoom': simple('game', 'leftRoom'),
      'game.reportGameCompletedPercentage': function (a, ev, s) { return s.game.reportGameCompletedPercentage(a.percentage); },
      'game.setGameContext': function (a, ev, s) { return s.game.setGameContext(a.context); },
      'game.clearGameContext': simple('game', 'clearGameContext'),
      'game.showInviteButton': function (a, ev, s) { return s.game.showInviteButton(a.params); },
      'game.hideInviteButton': simple('game', 'hideInviteButton'),
      'game.reportChat': function (a, ev, s) { return s.game.reportChat(a.report).then(noop); },
      'game.updateRoom': function (a, ev, s) { return s.game.updateRoom(a.update); },
      'game.inviteLink': function (a, ev, s) { return s.game.inviteLink(a.params); },
      'user.getUser': simple('user', 'getUser'),
      'user.listFriends': function (a, ev, s) { return s.user.listFriends({ page: a.page, size: a.size }); },
      'user.getUserToken': function (a, ev, s) {
        // '' is the engines' "default key" value; anything else goes to the SDK, which rejects an unknown type.
        return s.user.getUserToken(absent(a.signingKeyType) || a.signingKeyType === '' ? undefined : { signingKeyType: a.signingKeyType });
      },
      'store.getPurchaseToken': function (a, ev, s) { return purchaseToken(s); },
      'user.getXsollaUserToken': function (a, ev, s) { return purchaseToken(s); },
      'user.showAuthPrompt': function (a, ev, s) { return refocus(s.user.showAuthPrompt()); },
      'user.showAccountLinkPrompt': function (a, ev, s) { return refocus(s.user.showAccountLinkPrompt()); },
      'user.submitScore': function (a, ev, s) {
        return s.user.submitScore({ encryptedScore: a.encryptedScore, score: a.score });
      },
      // SDK 1.7.0: fire-and-forget; resolves with nothing.
      'analytics.track': function (a, ev, s) { s.analytics.track(a.name, absent(a.params) ? undefined : a.params); return Promise.resolve(); },
      'analytics.trackOrder': function (a, ev, s) { return s.analytics.trackOrder(a.provider, a.order); },
      'store.getCatalogItems': function (a, ev, s) { return storeOf(s).getCatalogItems(pageOf(a)); },
      'store.getPlayerInventory': function (a, ev, s) { return storeOf(s).getPlayerInventory(pageOf(a)); },
      'store.buyItem': function (a, ev, s) { return refocus(storeOf(s).buyItem(a.itemId)); },
      'store.consumeItem': function (a, ev, s) { return storeOf(s).consumeItem(a.sku, a.quantity).then(noop); },
      'room.send': function (a, ev, s) {
        // No `to` (or the engines' '' default) sends to every other member; a `to` that is not a string fails like the SDK.
        if (!absent(a.to) && typeof a.to !== 'string') throw bad('to must be a member id');
        return roomOf(s).send(a.data, typeof a.to === 'string' && a.to !== '' ? { to: a.to } : undefined).then(noop);
      },
      'room.setState': function (a, ev, s) {
        // `remove` is a list of keys to delete (engines whose tables cannot hold null, such as Lua, send it next to `patch`).
        var patch = a.patch;
        var k, i;
        // An empty Lua table arrives as {} or [] whichever it stands for, so an empty one counts as "nothing" in both places.
        if (!absent(a.remove) && !emptyTable(a.remove)) {
          // A string here used to be walked character by character, deleting one-letter keys.
          if (!isArray(a.remove)) throw bad('remove must be an array of keys');
          for (i = 0; i < a.remove.length; i++) if (typeof a.remove[i] !== 'string' && typeof a.remove[i] !== 'number') throw bad('remove must be an array of keys');
        }
        if (isArray(a.remove) && a.remove.length) {
          if (!absent(a.patch) && !emptyTable(a.patch) && (typeof a.patch !== 'object' || isArray(a.patch))) throw bad('patch must be an object');
          patch = {};
          if (a.patch && typeof a.patch === 'object') for (k in a.patch) if (Object.prototype.hasOwnProperty.call(a.patch, k)) patch[k] = a.patch[k];
          for (i = 0; i < a.remove.length; i++) patch[a.remove[i]] = null;
        }
        return roomOf(s).setState(patch).then(noop);
      },
      'achievements.list': function (a, ev, s) { return achOf(s).list(); },
      'achievements.unlock': function (a, ev, s) { return achOf(s).unlock(a.key); }
    };

    /* ---- synchronous methods ---- */
    var S = {
      'game.getSettings': function (a, s) { return s.game.settings; },
      'game.isOffline': function (a, s) { return !!s.game.isOffline; },
      'game.getInviteParams': function (a, s) { return s.game.inviteParams; },
      'game.getInviteParam': function (a, s) { return s.game.getInviteParam(a.key); },
      // The SDK turns any key into text, so a missing key (malformed argument JSON) would silently address the key "undefined".
      'data.getItem': function (a, s) { return s.data.getItem(dataKey(a)); },
      'data.setItem': function (a, s) {
        if (a.value === undefined || a.value === null) throw bad('A value is required');
        s.data.setItem(dataKey(a), a.value);
        return null;
      },
      'data.removeItem': function (a, s) { s.data.removeItem(dataKey(a)); return null; },
      'data.clear': function (a, s) { s.data.clear(); return null; },
      'game.setScreenshotProvider': function (a, s) { setScreenshotProvider(s, a.enabled !== false); return null; },
      'game.answerScreenshot': function (a, s) {
        settleShot(typeof a.dataUrl === 'string' && /^data:image\//i.test(a.dataUrl) ? a.dataUrl : null);
        return null;
      },
      'data.mirrorLocalStorage': function (a, s) {
        stopMirror();
        var o = { onError: function (e) { globalHandler('mirrorError', plainError(e)); } };
        if (a.keys && a.keys.length) o.keys = a.keys;
        else if (typeof a.pattern === 'string' && a.pattern !== '') {
          try { o.keys = new RegExp(a.pattern); } catch (e) { throw bad('pattern is not a valid regular expression'); }
        }
        if (typeof a.prefix === 'string' && a.prefix !== '') o.prefix = a.prefix;
        mirrorStop = s.data.mirrorLocalStorage(o);
        return null;
      },
      'data.stopMirrorLocalStorage': function (a, s) { stopMirror(); return null; },
      'room.members': function (a, s) { return roomOf(s).members(); },
      'room.getState': function (a, s) { return roomOf(s).getState(); },
      'room.isHost': function (a, s) { return !!roomOf(s).isHost(); },
      'room.selfId': function (a, s) { return roomOf(s).selfId; },
      'store.isAvailable': function (a, s) { return !!(s.store && s.store.isAvailable); },
      // Like the SDK: only a boolean (0/1, "true" or a missing flag used to be turned into one).
      'store.setSandbox': function (a, s) {
        if (typeof a.sandbox !== 'boolean') throw bad('sandbox must be a boolean');
        storeOf(s).setSandbox(a.sandbox);
        return null;
      }
    };

    return {
      setGlobalHandler: function (fn) { globalHandler = typeof fn === 'function' ? fn : noop; },
      call: function (method, args, onEvent) {
        try {
          var s = need();
          var f = T[method];
          if (f) return Promise.resolve(f(args || {}, onEvent || noop, s)).then(function (v) { return v === undefined ? null : v; });
          var g = S[method];
          if (g) return Promise.resolve(g(args || {}, s));
          return Promise.reject({ code: 'unknownMethod', message: 'Unknown bridge method ' + method });
        } catch (e) {
          return Promise.reject(plainError(e));
        }
      },
      callSync: function (method, args) {
        try {
          var g = S[method];
          if (!g) throw { code: 'unknownMethod', message: 'Unknown sync bridge method ' + method };
          var v = g(args || {}, need());
          return { ok: true, result: v === undefined ? null : v };
        } catch (e) {
          return { ok: false, error: plainError(e) };
        }
      },
      errorOf: plainError
    };
  },
  // </zoko-core>

  $ZokoUnityState__deps: ['$zokoCreateCore'],
  $ZokoUnityState: {
    core: null,
    send: function (method, payload) {
      var text = JSON.stringify(payload);
      if (typeof SendMessage === 'function') SendMessage('ZokoSDKBridge', method, text);
      else if (typeof Module !== 'undefined' && Module.SendMessage) Module.SendMessage('ZokoSDKBridge', method, text);
    },
    get: function () {
      if (!ZokoUnityState.core) {
        ZokoUnityState.core = zokoCreateCore(window, function () {
          return document.querySelector('#unity-canvas') || document.querySelector('canvas');
        });
        ZokoUnityState.core.setGlobalHandler(function (name, data) {
          ZokoUnityState.send('OnEvent', { id: 0, name: name, data: data });
        });
      }
      return ZokoUnityState.core;
    }
  },

  ZokoSDK_Call__deps: ['$zokoCreateCore', '$ZokoUnityState'],
  ZokoSDK_Call: function (id, methodPtr, argsPtr) {
    var method = UTF8ToString(methodPtr);
    var args = null;
    var text = UTF8ToString(argsPtr);
    try { args = text ? JSON.parse(text) : null; } catch (e) { args = null; }
    var core = ZokoUnityState.get();
    core.call(method, args, function (name, data) {
      ZokoUnityState.send('OnEvent', { id: id, name: name, data: data });
    }).then(function (result) {
      ZokoUnityState.send('OnResponse', { id: id, ok: true, result: result });
    }, function (error) {
      ZokoUnityState.send('OnResponse', { id: id, ok: false, error: core.errorOf(error) });
    });
  },

  ZokoSDK_CallSync__deps: ['$zokoCreateCore', '$ZokoUnityState'],
  ZokoSDK_CallSync: function (methodPtr, argsPtr) {
    var method = UTF8ToString(methodPtr);
    var args = null;
    var text = UTF8ToString(argsPtr);
    try { args = text ? JSON.parse(text) : null; } catch (e) { args = null; }
    var json = JSON.stringify(ZokoUnityState.get().callSync(method, args));
    var size = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(size);
    stringToUTF8(json, buffer, size);
    return buffer;
  },

  // Clipboard helper (not part of the shared core): copies text from a click handler. Tries the synchronous
  // copy command first (works inside the portal iframe without extra permissions), then the async API.
  // Returns 1 when the copy was done or started, 0 otherwise.
  ZokoSDK_CopyText: function (textPtr) {
    var text = UTF8ToString(textPtr);
    var ok = 0;
    try {
      var area = document.createElement('textarea');
      area.value = text;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      ok = document.execCommand('copy') ? 1 : 0;
      document.body.removeChild(area);
      var canvas = document.querySelector('#unity-canvas') || document.querySelector('canvas');
      if (canvas && typeof canvas.focus === 'function') canvas.focus();
    } catch (e) {
      ok = 0;
    }
    if (!ok && typeof navigator !== 'undefined' && navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
      navigator.clipboard.writeText(text).catch(function () {});
      ok = 1;
    }
    return ok;
  }
});
