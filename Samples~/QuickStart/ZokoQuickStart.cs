using UnityEngine;
using ZokoGames;

/// <summary>
/// Minimal scene script: attach to any GameObject. Works in the Editor (simulator) and in WebGL builds.
/// Flow: init, loadingStop, gameplayStart, rewarded ad, cloud save. Press R for a rewarded ad.
/// </summary>
public class ZokoQuickStart : MonoBehaviour
{
    async void Start()
    {
        Application.runInBackground = true;   // WebGL: keep running while an ad overlay or another tab has focus

        try { await ZokoSDK.InitAsync(); }
        catch (ZokoException e) { Debug.LogWarning("ZokoGames init failed: " + e.Error.Code); return; }

        Debug.Log("ZokoGames " + ZokoSDK.Environment + (ZokoSDK.IsSimulated ? " (simulated)" : ""));
        // The portal's mute setting is applied to AudioListener.pause by the SDK (ZokoSDK.Ad.FollowPortalMute).
        ZokoSDK.Game.LoadingStop();
        ZokoSDK.Game.GameplayStart();
    }

    void Update()
    {
        if (!ZokoSDK.IsInitialized || !Input.GetKeyDown(KeyCode.R)) return;

        // Audio is muted and restored by the SDK; pause your own simulation here.
        ZokoSDK.Ad.RequestAd(ZokoAdType.Rewarded,
            adStarted: () => Time.timeScale = 0,
            adFinished: () =>
            {
                Time.timeScale = 1;
                int coins = ZokoSDK.Data.GetInt("coins", 0) + 100;   // reward, then save
                ZokoSDK.Data.SetInt("coins", coins);
                Debug.Log("Reward granted, coins saved: " + coins);
            },
            adError: e => { Time.timeScale = 1; Debug.Log("Ad failed: " + e.Code); });
    }
}
