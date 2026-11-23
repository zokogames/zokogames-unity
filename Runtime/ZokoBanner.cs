using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZokoGames
{
    public enum ZokoBannerSize
    {
        Responsive,
        S728x90,
        S300x250,
        S320x50,
        S468x60,
        S320x100,
    }

    /// <summary>
    /// Put this on a UI RectTransform. It reports its on-screen rectangle to the portal, which draws the
    /// banner over the game canvas at that position. Optional <see cref="placeholder"/> is shown while a
    /// banner is "displayed" so you can see the layout in the Editor simulator.
    /// </summary>
    [AddComponentMenu("ZokoGames/Zoko Banner")]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ZokoBanner : MonoBehaviour
    {
        [Tooltip("DOM container id; must be unique per banner.")]
        public string containerId = "zoko-banner";
        public ZokoBannerSize size = ZokoBannerSize.Responsive;
        public bool requestOnEnable = true;
        [Tooltip("Camera of the parent canvas; leave empty for Screen Space - Overlay.")]
        public Camera uiCamera;
        [Tooltip("Optional graphic that is only visible while a banner is shown (useful in the Editor).")]
        public Graphic placeholder;

        public event Action<ZokoError> Completed;
        public bool IsShown { get; private set; }
        /// <summary>Last rectangle (screen pixels, origin bottom-left) that was reported to the portal.</summary>
        public Rect ScreenRect { get; private set; }

        RectTransform _rt;
        readonly Vector3[] _corners = new Vector3[4];

        void Awake()
        {
            _rt = (RectTransform)transform;
            if (placeholder != null) placeholder.enabled = false;
        }

        void OnEnable()
        {
            if (requestOnEnable) Request();
        }

        void OnDisable()
        {
            if (IsShown) Clear();
        }

        /// <summary>Current rectangle of this transform in screen pixels (origin bottom-left).</summary>
        public Rect GetScreenRect()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            _rt.GetWorldCorners(_corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, _corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCamera, _corners[2]);
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        public void Request()
        {
            ZokoSDK.Init(initError =>
            {
                if (initError != null) { Finish(initError); return; }
                if (!isActiveAndEnabled) return;
                ScreenRect = GetScreenRect();
                int w, h;
                if (TryGetStaticSize(size, out w, out h))
                    ZokoSDK.Banner.RequestBanner(containerId, w, h, ScreenRect, Finish);
                else
                    ZokoSDK.Banner.RequestResponsiveBanner(containerId, ScreenRect, Finish);
            });
        }

        public void Clear()
        {
            IsShown = false;
            if (placeholder != null) placeholder.enabled = false;
            ZokoSDK.Banner.ClearBanner(containerId);
        }

        void Finish(ZokoError error)
        {
            IsShown = error == null;
            if (placeholder != null) placeholder.enabled = IsShown && ZokoSDK.IsSimulated;
            if (error != null) Debug.LogWarning("[ZokoGames] Banner '" + containerId + "': " + error);
            var h = Completed;
            if (h != null) h(error);
        }

        static bool TryGetStaticSize(ZokoBannerSize s, out int w, out int h)
        {
            switch (s)
            {
                case ZokoBannerSize.S728x90: w = 728; h = 90; return true;
                case ZokoBannerSize.S300x250: w = 300; h = 250; return true;
                case ZokoBannerSize.S320x50: w = 320; h = 50; return true;
                case ZokoBannerSize.S468x60: w = 468; h = 60; return true;
                case ZokoBannerSize.S320x100: w = 320; h = 100; return true;
            }
            w = h = 0;
            return false;
        }
    }
}
