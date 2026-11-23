using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ZokoGames
{
    /// <summary>
    /// Hidden MonoBehaviour that receives <c>SendMessage</c> calls from ZokoSDK.jslib. The GameObject
    /// name "ZokoSDKBridge" and the method names OnResponse / OnEvent are part of the jslib contract.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ZokoSDKBridge : MonoBehaviour
    {
        sealed class Pending
        {
            public Action<string, object> OnEvent;
            public Action<ZokoResponse> Done;
        }

        static ZokoSDKBridge _instance;
        readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
        int _nextId = 1;

        /// <summary>Id 0 events: settingsChange, offlineChange, auth, joinRoom, achievementUnlocked, roomMessage, roomState, roomMembers, roomHost, roomLeft, roomError.</summary>
        internal static event Action<string, object> GlobalEvent;

        /// <summary>Text drawn as a screen overlay while the simulator plays an ad (editor only).</summary>
        internal string OverlayText;

        internal static ZokoSDKBridge Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ZokoSDKBridge");
                    go.hideFlags = HideFlags.HideInHierarchy;
                    // DontDestroyOnLoad is only valid in Play mode (and players); edit-mode callers get a scene object.
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                    _instance = go.AddComponent<ZokoSDKBridge>();
                }
                return _instance;
            }
        }

        /// <summary>Called from ZokoSDK.ResetStatics when Domain Reload is off: drop the destroyed instance and the global listeners.</summary>
        internal static void ResetStatics()
        {
            _instance = null;
            GlobalEvent = null;
        }

        internal int Register(Action<string, object> onEvent, Action<ZokoResponse> done)
        {
            int id = _nextId++;
            _pending[id] = new Pending { OnEvent = onEvent, Done = done };
            return id;
        }

        /// <summary>SendMessage target. Payload: {"id":n,"ok":true,"result":...} or {"id":n,"ok":false,"error":{...}}.</summary>
        public void OnResponse(string json)
        {
            try
            {
                var msg = ZokoJson.Parse(json);
                int id = (int)ZokoJson.Num(msg, "id");
                Pending p;
                if (!_pending.TryGetValue(id, out p)) return;
                _pending.Remove(id);
                var res = ZokoJson.Bool(msg, "ok")
                    ? ZokoResponse.Success(ZokoJson.Get(msg, "result"))
                    : new ZokoResponse { Ok = false, Error = ZokoError.From(ZokoJson.Get(msg, "error")) };
                if (p.Done != null) p.Done(res);
            }
            catch (Exception e)
            {
                Debug.LogError("[ZokoGames] bad response: " + e.Message + "\n" + json);
            }
        }

        /// <summary>SendMessage target. Payload: {"id":n,"name":"adStarted","data":...}; id 0 is global.</summary>
        public void OnEvent(string json)
        {
            try
            {
                var msg = ZokoJson.Parse(json);
                int id = (int)ZokoJson.Num(msg, "id");
                string name = ZokoJson.Str(msg, "name");
                object data = ZokoJson.Get(msg, "data");
                if (id == 0)
                {
                    var h = GlobalEvent;
                    if (h != null) h(name, data);
                    return;
                }
                Pending p;
                if (_pending.TryGetValue(id, out p) && p.OnEvent != null) p.OnEvent(name, data);
            }
            catch (Exception e)
            {
                Debug.LogError("[ZokoGames] bad event: " + e.Message + "\n" + json);
            }
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(OverlayText)) return;
            var area = new Rect(0, 0, Screen.width, Screen.height);
            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22 };
            GUI.Label(area, OverlayText, style);
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>Real transport: calls into window.ZokoGames.SDK through ZokoSDK.jslib.</summary>
    internal sealed class ZokoWebGLTransport : IZokoTransport
    {
        [DllImport("__Internal")] static extern void ZokoSDK_Call(int id, string method, string argsJson);
        [DllImport("__Internal")] static extern string ZokoSDK_CallSync(string method, string argsJson);
        [DllImport("__Internal")] static extern int ZokoSDK_CopyText(string text);

        /// <summary>Copy text to the browser clipboard (call from a click or tap handler). True when the copy was done or started.</summary>
        internal static bool CopyText(string text) { return ZokoSDK_CopyText(text ?? "") == 1; }

        public void Call(string method, Dictionary<string, object> args, Action<string, object> onEvent, Action<ZokoResponse> done)
        {
            int id = ZokoSDKBridge.Instance.Register(onEvent, done);
            ZokoSDK_Call(id, method, args == null ? "{}" : ZokoJson.Serialize(args));
        }

        public ZokoResponse CallSync(string method, Dictionary<string, object> args)
        {
            try
            {
                var msg = ZokoJson.Parse(ZokoSDK_CallSync(method, args == null ? "{}" : ZokoJson.Serialize(args)));
                if (ZokoJson.Bool(msg, "ok")) return ZokoResponse.Success(ZokoJson.Get(msg, "result"));
                return new ZokoResponse { Ok = false, Error = ZokoError.From(ZokoJson.Get(msg, "error")) };
            }
            catch (Exception e)
            {
                return ZokoResponse.Fail("other", e.Message);
            }
        }
    }
#endif
}
