using System;
using TMPro;
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
        public Vector3 scale = Vector3.one; // 기본값 (1,1,1)
    }

    [Serializable]
    public enum UIImageType
    {
        Simple = 0,
        Sliced,
        Tiled,
        Filled
    }

    // 아두이노 시리얼 통신 설정
    [Serializable]
    public class SerialSetting
    {
        public string portName = "COM3";
        public int baudRate = 9600;
        public bool autoConnect = true;
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
        public float fontSize;
        public Color fontColor = Color.white;
        public TextAlignmentOptions alignment = TextAlignmentOptions.Center;
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

    [Serializable]
    public class FontMaps
    {
        public string font1;
        public string font2;
        public string font3;
        public string font4;
        public string font5;
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
        public float inactivityTime;
        public float fadeTime;
        public SerialSetting serial;
        public CloseSetting closeSetting;
        public FontMaps fontMap;
        public SoundSetting[] sounds;
    }
}