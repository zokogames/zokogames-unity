using UnityEngine;

namespace ZokoGames
{
    /// <summary>
    /// Settings for the simulated SDK that runs in the Unity Editor and in non-WebGL players.
    /// Create one via the menu ZokoGames / Create Simulator Settings (saved to Assets/Resources).
    /// Without an asset the defaults below are used.
    /// </summary>
    [CreateAssetMenu(menuName = "ZokoGames/SDK Settings", fileName = ResourceName)]
    public sealed class ZokoSDKSettings : ScriptableObject
    {
        public const string ResourceName = "ZokoSDKSettings";

        public enum AdBehaviour { Fill, NoFill, Error }

        [Header("Environment")]
        [Tooltip("Behave like an embed where the SDK is disabled: every call fails with sdkDisabled.")]
        public bool simulateDisabled;
        public bool accountAvailable = true;
        public bool instantMultiplayer;
        public string locale = "en-US";
        public string countryCode = "US";

        [Header("Ads")]
        public AdBehaviour adBehaviour = AdBehaviour.Fill;
        [Min(0f)] public float adDurationSeconds = 1.5f;
        public bool adblock;

        [Header("User")]
        public bool loggedIn = true;
        public string username = "SimPlayer";
        public string userId = "sim-user-1";
        [Min(0)] public int friendCount = 12;

        [Header("Game settings (portal controlled)")]
        public bool muteAudio;
        public bool disableChat;
        [Tooltip("Simulate kids mode (Game.Settings.KidsMode, SDK 1.5.0); like the portal it also turns chat off.")]
        public bool kidsMode;
        [Tooltip("Start the simulated player offline (Game.IsOffline).")]
        public bool offline;

        [Header("Store")]
        [Tooltip("Whether Store.IsAvailable is true. Purchases are simulated against a small built-in catalogue.")]
        public bool storeAvailable = true;

        [Header("Misc")]
        public bool verboseLog = true;

        public static ZokoSDKSettings Load()
        {
            var s = Resources.Load<ZokoSDKSettings>(ResourceName);
            return s != null ? s : CreateInstance<ZokoSDKSettings>();
        }
    }
}
