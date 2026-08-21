using System;
using UnityEngine;

namespace Wonjeong.Data
{
    [Serializable]
    public class UISettingBase
    {
        public string name;
        public Vector2 position;
        public Vector2 size;
        public Vector3 rotation;
        public Vector3 scale = Vector3.one;
    }

    [Serializable]
    public enum UIImageType
    {
        Simple = 0,
        Sliced,
        Tiled,
        Filled
    }

    [Serializable]
    public class CloseSetting
    {
        public Vector2 position;
        public int numToClose;
        public float resetClickTime;
        public float imageAlpha;
    }

    // ---------------------- UISettingBase 상속-------------------------------
    
    [Serializable]
    public class ImageSetting : UISettingBase
    {
        public string sourceImage;
        public Color color = Color.white;
        public UIImageType type = UIImageType.Simple;
    }

    [Serializable]
    public class TextSetting : UISettingBase
    {
        public string text;
        public string fontName;
        public int fontSize;
        public Color fontColor = Color.white;
        public TextAnchor alignment = TextAnchor.MiddleCenter;
        public bool isBold;
    }

    [Serializable]
    public class VideoSetting : UISettingBase
    {
        public string fileName;
        public float volume;
    }

    [Serializable]
    public class ButtonSetting : UISettingBase
    {
        public ImageSetting buttonBackgroundImage;
        public TextSetting buttonText;
        public string buttonSound;
    }
    
    // --------------------------------------------------------------------

    /// <summary>
    /// 폰트 키와 Addressables 주소의 매핑.
    /// key는 TextSetting.fontName에서 참조하는 이름이며 자유롭게 명명할 수 있음.
    /// </summary>
    [Serializable]
    public class FontSetting
    {
        public string key;
        public string address;
    }

    [Serializable]
    public class SoundSetting
    {
        public string key;
        public string clipPath;
        public float volume = 1.0f;
    }

    [Serializable]
    public class Settings
    {
        public float warningTime;
        public float resetTime;
        public float fadeTime;

        /// <summary>
        /// 목표 프레임 레이트(FPS). 0 이하(미설정)면 아무것도 변경하지 않고 QualitySettings의
        /// 기본값(vSyncCount 등)을 그대로 따름. 전시/키오스크처럼 장시간 구동되는 환경에서는
        /// 디스플레이 주사율을 넘는 프레임을 그려 낭비되는 GPU 자원·발열·전력 소모를 줄이기 위해
        /// 30~60 사이 값으로 명시적으로 캡을 거는 것을 권장함.
        /// </summary>
        public int targetFrameRate;

        public CloseSetting closeSetting;
        public FontSetting[] fonts;
        public SoundSetting[] sounds;
    }
}