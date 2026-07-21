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
        public CloseSetting closeSetting;
        public FontSetting[] fonts;
        public SoundSetting[] sounds;
    }
}