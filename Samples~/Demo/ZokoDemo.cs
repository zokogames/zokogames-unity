using System.Collections.Generic;
using UnityEngine;
using ZokoGames;

/// <summary>
/// Add to any GameObject. Press the keys in Play mode (Editor simulator) or in a WebGL build:
/// A midgame ad, R rewarded ad, U user, F friends, T token, S save, G gameplay start/stop, H happytime, M room message and state, B store (buy first item).
/// Press G (gameplay start) before the first A: since SDK 1.6.4 a web build refuses a midgame ad with adCooldown until the game has called gameplay start (or 30 s of visible time passed).
/// </summary>
public class ZokoDemo : MonoBehaviour
{
    bool _playing;

    void Start()
    {
        Application.runInBackground = true; // WebGL: keep running (timers, audio) while an ad overlay or another tab has focus
        ZokoSDK.Init(error =>
        {
            if (error != null) { Debug.LogWarning("ZokoGames init failed: " + error); return; }
            Debug.Log("ZokoGames ready in " + ZokoSDK.Environment + (ZokoSDK.IsSimulated ? " (simulated)" : ""));
            ZokoSDK.Game.LoadingProgress(100); // optional progress bar value while loading
            // The portal's mute setting is applied to AudioListener.pause by the SDK (ZokoSDK.Ad.FollowPortalMute).
            ZokoSDK.User.AuthChanged += u => Debug.Log("Signed in as " + u.Username);
            ZokoSDK.Game.Room.MessageReceived += m => Debug.Log("room message from " + m.From + ": " + m.Json);
            ZokoSDK.Game.Room.StateChanged += (state, change) => Debug.Log("room state: " + ZokoSDK.Game.Room.GetStateJson());

            int plays = ZokoSDK.Data.GetInt("plays", 0) + 1;
            ZokoSDK.Data.SetInt("plays", plays);
            Debug.Log("Cloud save says this is play #" + plays);
        });
    }

    async void Update()
    {
        if (!ZokoSDK.IsInitialized) return;

        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.R))
        {
            var type = Input.GetKeyDown(KeyCode.R) ? ZokoAdType.Rewarded : ZokoAdType.Midgame;
            ZokoSDK.Ad.RequestAd(type,
                adStarted: () => { Time.timeScale = 0; AudioListener.pause = true; },
                adFinished: () => { Time.timeScale = 1; AudioListener.pause = false; Debug.Log("ad done, reward!"); },
                adError: e => { Time.timeScale = 1; AudioListener.pause = false; Debug.Log("ad error " + e.Code); });
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            _playing = !_playing;
            if (_playing) ZokoSDK.Game.GameplayStart(); else ZokoSDK.Game.GameplayStop();
        }
        if (Input.GetKeyDown(KeyCode.H)) ZokoSDK.Game.Happytime();
        if (Input.GetKeyDown(KeyCode.M))
        {
            // Room data channel: the simulator loops these back to this instance.
            ZokoSDK.Game.Room.Send(new Dictionary<string, object> { { "type", "ping" }, { "t", Time.time } });
            if (ZokoSDK.Game.Room.IsHost) ZokoSDK.Game.Room.SetState("lastPing", Time.time);
        }

        if (Input.GetKeyDown(KeyCode.B) && ZokoSDK.Store.IsAvailable)
        {
            try
            {
                var items = await ZokoSDK.Store.GetCatalogItemsAsync();
                if (items.Count == 0) return;
                string bought = await ZokoSDK.Store.BuyItemAsync(items[0].Sku);
                Debug.Log("bought " + bought + "; verify ownership on your server");
                foreach (var owned in await ZokoSDK.Store.GetPlayerInventoryAsync())
                    Debug.Log(owned.Sku + " x" + (owned.Quantity.HasValue ? owned.Quantity.Value.ToString() : "?"));
            }
            catch (ZokoException e) { Debug.LogWarning(e.Error); }
        }

        if (Input.GetKeyDown(KeyCode.U))
        {
            // Task style: throws ZokoException on failure.
            try
            {
                var user = await ZokoSDK.User.GetUserAsync();
                Debug.Log(user == null ? "logged out" : "user " + user.Username);
            }
            catch (ZokoException e) { Debug.LogWarning(e.Error); }
        }

        if (Input.GetKeyDown(KeyCode.F))
            ZokoSDK.User.ListFriends(1, 10, (page, err) =>
                Debug.Log(err != null ? err.ToString() : page.Friends.Count + " of " + page.Total + " friends"));

        if (Input.GetKeyDown(KeyCode.T))
            ZokoSDK.User.GetUserToken((token, err) => Debug.Log(err != null ? err.ToString() : "token " + token));

        if (Input.GetKeyDown(KeyCode.S))
        {
            ZokoSDK.Data.SetFloat("volume", 0.5f);
            ZokoSDK.Data.SetString("name", "Ada");
            Debug.Log("volume=" + ZokoSDK.Data.GetFloat("volume") + " has name=" + ZokoSDK.Data.HasKey("name"));
            ZokoSDK.Game.SetGameContext(new Dictionary<string, object> { { "level", 3 }, { "hard", true } });
            ZokoSDK.Analytics.TrackOrder("xsolla", new Dictionary<string, object> { { "sku", "gems_100" }, { "price", 0.99 } });
        }
    }
}
