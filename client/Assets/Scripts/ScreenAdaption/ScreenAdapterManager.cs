using Game.Common;
using UnityEngine;
using UnityEngine.Events;

namespace Game.ScreenAdaption
{
    public class ScreenAdapterManager : SingleTon<ScreenAdapterManager>
    {
        public bool isInit;

        private Rect m_SafeAreaRect = new(-1, -1, -1, -1);
        public Rect SafeAreaRect
        {
            get
            {
                if (!(m_SafeAreaRect.x < 0)) return m_SafeAreaRect;

                UpdateSafeAreaRect();
                return m_SafeAreaRect;
            }
        }

        private Rect m_AntiSafeAreaRect = new(-1, -1, -1, -1);
        public Rect AntiSafeAreaRect
        {
            get
            {
                if (!(m_AntiSafeAreaRect.x < 0)) return m_AntiSafeAreaRect;

                UpdateSafeAreaRect();
                return m_AntiSafeAreaRect;
            }
        }

        public UnityEvent<int[]> onScreenResolutionChanged = new();

        private float maxScreenSize => Mathf.Max(Screen.width, Screen.height);
        private bool isLandscape => Screen.width >= Screen.height;
        private float safeAreaInsetWidthNormalized // 归一化的安全区侧边宽度，为安全区侧边的实际宽度:屏幕宽度
        {
            get
            {
#if UNITY_EDITOR
                return isLandscape ? Screen.safeArea.x / Screen.width : Screen.safeArea.y / Screen.height;
#elif UNITY_ANDROID
                return AndroidScreenSupport.Instance.safeAreaInsetWidthNormalized;
#elif UNITY_IOS
                return isLandscape ? Screen.safeArea.x / Screen.width : Screen.safeArea.y / Screen.height;
#else
                return 0;
#endif
            }
        }
        private int[] lastScreenResolution = { 0, 0 };

        public void Init()
        {
            lastScreenResolution = new[] { Screen.width, Screen.height };

            isInit = true;
        }

        public void Update()
        {
            if (lastScreenResolution[0] == Screen.width || lastScreenResolution[1] == Screen.height) return;

            // 分辨率发生变化。发布分辨率变化的事件，并告知变化之前的分辨率
            int[] resolution = { lastScreenResolution[0], lastScreenResolution[1] };
            lastScreenResolution = new[] { Screen.width, Screen.height };
            UpdateSafeAreaRect();
            onScreenResolutionChanged.Invoke(resolution);
        }

        private void UpdateSafeAreaRect()
        {
            float safeWidth = safeAreaInsetWidthNormalized;
            float antiWidth = -safeWidth / (1f - 2f * safeWidth);

            if (isLandscape)
            {
                m_SafeAreaRect     = new Rect(safeWidth, 0, 1 - safeWidth, 1);
                m_AntiSafeAreaRect = new Rect(antiWidth, 0, 1 - antiWidth, 1);
            }
            else
            {
                m_SafeAreaRect     = new Rect(0, safeWidth, 1, 1 - safeWidth);
                m_AntiSafeAreaRect = new Rect(0, antiWidth, 1, 1 - antiWidth);
            }
        }
    }
}