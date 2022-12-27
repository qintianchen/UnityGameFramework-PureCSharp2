using Game.Common;
using UnityEngine;

namespace Game.ScreenAdaption
{
    /// <summary>
    /// 不同版本的Android，不同机型的Android获取Notch的方式不一样
    /// 直到Android 28以后，才增加了DisplayCutout的API以供开发者获取Notch Height
    /// https://developer.android.com/reference/android/view/DisplayCutout
    /// 读取 safeAreaInsetWidthNormalized 获取安全区的大小
    /// （这里的安全区是指手机最长的方向上的最大安全区大小，而不是上下左右分别返回，这是因为历史原因，安全区出现的时候，Android还没有正式出API支持获取安全区的大小，都是各大手机产商群魔乱舞）
    /// 通过 Reset() 来使得 AndroidScreenHelper 能够重新计算安全区的大小
    /// </summary>
    internal class AndroidScreenHelper : SingleTon<AndroidScreenHelper>
    {
        private const int NOTCH_IN_SCREEN_VIVO_MARK = 0x00000020; //是否有凹槽
        private const int SAMSUNG_COCKTAIL_PANEL    = 7;

        #region 属性

        private int screenWidth => Screen.width;
        private int screenHeight => Screen.height;
        private int maxScreenSize => Mathf.Max(screenWidth, screenHeight);
        private float screenRatio => screenWidth * 1.0f / screenHeight;

        private int m_AndroidSDKVersion = -1; // 当前的 SDK 版本

        private int AndroidSDKVersion
        {
            get
            {
                if (m_AndroidSDKVersion >= 0) return m_AndroidSDKVersion;

                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                m_AndroidSDKVersion = version.GetStatic<int>("SDK_INT");
                return m_AndroidSDKVersion;
            }
        }

        private AndroidPhoneType m_CurAndroidPhoneType = AndroidPhoneType.MAX; // 当前的设备品牌

        private AndroidPhoneType CurAndroidPhoneType
        {
            get
            {
                if (m_CurAndroidPhoneType != AndroidPhoneType.MAX) return m_CurAndroidPhoneType;

                m_CurAndroidPhoneType = AndroidPhoneType.NONE;
                string phoneUpperModel = SystemInfo.deviceModel.ToUpper();

                for (int i = (int)AndroidPhoneType.NONE + 1; i < (int)AndroidPhoneType.MAX; i++)
                {
                    AndroidPhoneType current = (AndroidPhoneType)i;
                    if (!phoneUpperModel.Contains(current.ToString())) continue;

                    m_CurAndroidPhoneType = current;
                    break;
                }

                return m_CurAndroidPhoneType;
            }
        }

        private float? m_SafeAreaInsetWidthNormalized;

        public float safeAreaInsetWidthNormalized // 安全区边侧的宽度
        {
            get
            {
                m_SafeAreaInsetWidthNormalized ??= GetSafeAreaInset();
                return (float)m_SafeAreaInsetWidthNormalized;
            }
        }

        #endregion

        #region 方法

        public void Reset()
        {
            m_SafeAreaInsetWidthNormalized = null;
        }

        private float GetSafeAreaInset()
        {
            if (AndroidSDKVersion >= 28)
            {
                return GetSafeAreaInset_AndroidP();
            }

            var phoneType = CurAndroidPhoneType;
            return phoneType switch
            {
                AndroidPhoneType.XIAOMI  => GetSafeAreaInset_XIAOMI(),
                AndroidPhoneType.HUAWEI  => GetSafeAreaInset_Huawei(),
                AndroidPhoneType.VIVO    => GetSafeAreaInset_Vivo(),
                AndroidPhoneType.OPPO    => GetSafeAreaInset_Oppo(),
                AndroidPhoneType.SAMSUNG => GetSafeAreaInset_Samsung(),
                _                        => GetSafeAreaInset_Notch(),
            };
        }

        private float GetSafeAreaInset_AndroidP()
        {
            try
            {
                using var unityPlayer   = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity      = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var window        = activity.Call<AndroidJavaObject>("getWindow");
                using var decorView     = window.Call<AndroidJavaObject>("getDecorView");
                using var windowInsets  = decorView.Call<AndroidJavaObject>("getRootWindowInsets");
                var       displayCutout = windowInsets.Call<AndroidJavaObject>("getDisplayCutout");
                if (displayCutout != null)
                {
                    float notch = 0;
                    if (screenWidth >= screenHeight)
                    {
                        notch = Mathf.Max(displayCutout.Call<int>("getSafeInsetLeft"), displayCutout.Call<int>("getSafeInsetRight"));
                    }
                    else
                    {
                        notch = Mathf.Max(displayCutout.Call<int>("getSafeInsetTop"), displayCutout.Call<int>("getSafeInsetBottom"));
                    }

                    return notch * 1.0f / maxScreenSize;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Android P hasNotch occurred error: " + e);
            }

            return 0;
        }

        private float GetSafeAreaInset_XIAOMI()
        {
            try
            {
                using var jo       = new AndroidJavaClass("android/os/SystemProperties");
                var       hasNotch = jo.CallStatic<string>("get", "ro.miui.notch");
                if (hasNotch != "1")
                {
                    return 0;
                }

                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var context     = activity.Call<AndroidJavaObject>("getApplicationContext");
                using var resources   = context.Call<AndroidJavaObject>("getResources");

                // MIUI 10 支持动态获取Notch高度
                float notchHeight = 89;
                int   resourceId  = resources.Call<int>("getIdentifier", "notch_height", "dimen", "android");
                if (resourceId > 0)
                {
                    notchHeight = resources.Call<int>("getDimensionPixelSize", resourceId);
                }
                else
                {
                    resourceId = resources.Call<int>("getIdentifier", "status_bar_height", "dimen", "android");
                    if (resourceId > 0)
                    {
                        notchHeight = resources.Call<int>("getDimensionPixelSize", resourceId);
                        if (notchHeight > 100)
                        {
                            notchHeight -= 20;
                        }
                    }
                }

                return notchHeight * 1.0f / maxScreenSize;
            }
            catch (System.Exception e)
            {
                Debug.LogError("MI getting notch occurred error: " + e);
            }

            return 0;
        }

        private float GetSafeAreaInset_Huawei()
        {
            try
            {
                using var jo               = new AndroidJavaClass("com.huawei.android.util.HwNotchSizeUtil");
                var       hasNotchInScreen = jo.CallStatic<bool>("hasNotchInScreen");
                var       notchSize        = jo.CallStatic<int[]>("getNotchSize");

                return notchSize[1] * 1.0f / maxScreenSize;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Huawei getting notch occurred error: " + e);
            }

            return 0;
        }

        private float GetSafeAreaInset_Vivo()
        {
            try
            {
                using var jo               = new AndroidJavaClass("android.util.FtFeature");
                var       hasNotchInScreen = jo.CallStatic<bool>("isFeatureSupport", NOTCH_IN_SCREEN_VIVO_MARK);
                if (hasNotchInScreen)
                {
                    return 80f / maxScreenSize;
                }

                return 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Vivo getting notch occurred error: " + e);
            }

            return 0;
        }

        private float GetSafeAreaInset_Oppo()
        {
            try
            {
                using AndroidJavaClass  unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject context     = activity.Call<AndroidJavaObject>("getApplicationContext");
                using AndroidJavaObject manager     = activity.Call<AndroidJavaObject>("getPackageManager");
                using AndroidJavaObject resources   = context.Call<AndroidJavaObject>("getResources");

                bool hasNotchInScreen = manager.Call<bool>("hasSystemFeature", "com.oppo.feature.screen.heteromorphism");
                int  resourceId       = resources.Call<int>("getIdentifier", "status_bar_height", "dimen", "android");
                int  notchHeight      = 80;
                if (resourceId > 0)
                {
                    notchHeight = resources.Call<int>("getDimensionPixelSize", resourceId);
                }

                if (!hasNotchInScreen)
                {
                    if (screenRatio > 2.1f)
                    {
                        hasNotchInScreen = true;
                    }
                }

                if (hasNotchInScreen)
                {
                    return notchHeight * 1.0f / maxScreenSize;
                }

                return 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Oppo hasNotch occurred error: " + e);
            }

            return 0;
        }

        private float GetSafeAreaInset_Samsung()
        {
            try
            {
                using var jo               = new AndroidJavaClass("com.samsung.android.sdk.look.SlookImpl");
                var       hasNotchInScreen = jo.CallStatic<bool>("isFeatureEnabled", SAMSUNG_COCKTAIL_PANEL);
                if (hasNotchInScreen)
                {
                    return 0.03612f; // 88.0f / 2436
                }

                return 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Samsung hasNotch occurred error: " + e);
            }

            return 0;
        }

        /// <summary>
        /// https://source.android.com/devices/tech/display/display-cutouts
        /// status_bar_height_portrait: In most devices, this defaults to 24dp. When there is a cutout, set this value to the height of the cutout.
        /// Can optionally be taller than the cutout if desired.
        /// </summary>
        private float GetSafeAreaInset_Notch()
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var context     = activity.Call<AndroidJavaObject>("getApplicationContext");
                using var resources   = context.Call<AndroidJavaObject>("getResources");
                using var dm          = resources.Call<AndroidJavaObject>("getDisplayMetrics");
                var       scale       = dm.Get<float>("density");
                var       resourceId  = resources.Call<int>("getIdentifier", "status_bar_height", "dimen", "android");
                if (resourceId > 0)
                {
                    var notchHeight = resources.Call<int>("getDimensionPixelSize", resourceId);

                    if (scale > 1 && notchHeight / scale > 25)
                    {
                        return notchHeight * 1.0f / maxScreenSize;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Check statusBar height occurred error: " + e);
            }

            return 0;
        }

        #endregion

        private enum AndroidPhoneType
        {
            NONE,
            HUAWEI,
            XIAOMI,
            OPPO,
            VIVO,
            SAMSUNG,
            MAX,
        }
    }
}