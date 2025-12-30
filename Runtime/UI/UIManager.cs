using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using Wonjeong.Data;

namespace Wonjeong.UI
{
    public class UIManager : MonoBehaviour
    {
        private static UIManager _instance;
        public static UIManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<UIManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("UIManager");
                        _instance = go.AddComponent<UIManager>();
                    }
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance == null) 
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        #region Set Methods (Configuration)

        /// <summary> 기존 게임오브젝트에 이미지 설정을 적용 </summary>
        public void SetImage(GameObject target, ImageSetting setting)
        {
            if (target == null || setting == null) return;
            ApplyTransform(target.GetComponent<RectTransform>(), setting);

            Image img = target.GetComponent<Image>();
            if (img != null)
            {
                img.color = setting.color;
                img.type = (Image.Type)setting.type;

                // StreamingAssets에서 이미지 로드
                Sprite sprite = LoadSprite(setting.sourceImage);
                if (sprite != null)
                {
                    img.sprite = sprite;
                }
            }
        }

        /// <summary> 기존 게임오브젝트에 텍스트 설정을 적용 </summary>
        public void SetText(GameObject target, TextSetting setting)
        {
            if (target == null || setting == null) return;

            target.name = setting.name;
            ApplyTransform(target.GetComponent<RectTransform>(), setting);

            TextMeshProUGUI tmp = target.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = setting.text;
                tmp.fontSize = setting.fontSize;
                tmp.color = setting.fontColor;
                tmp.alignment = setting.alignment;
                
                if (setting.useGradient)
                {
                    tmp.enableVertexGradient = true;
                    tmp.colorGradient = new VertexGradient(
                        setting.gradientTopLeft, 
                        setting.gradientTopRight, 
                        setting.gradientBottomLeft, 
                        setting.gradientBottomRight
                    );
                }
            }
        }

        /// <summary> 기존 버튼 오브젝트(이미지+텍스트)에 설정을 적용 </summary>
        public void SetButton(GameObject target, ButtonSetting setting)
        {
            if (target == null || setting == null) return;

            target.name = setting.name;
            ApplyTransform(target.GetComponent<RectTransform>(), setting);

            // 1. 버튼 배경 이미지 적용
            if (setting.buttonBackgroundImage != null)
            {
                Image bgImg = target.GetComponent<Image>(); 
                if (bgImg != null)
                {
                    bgImg.color = setting.buttonBackgroundImage.color;
                    Sprite sprite = LoadSprite(setting.buttonBackgroundImage.sourceImage);
                    if (sprite != null) bgImg.sprite = sprite;
                }
            }

            // 2. 버튼 텍스트 적용
            if (setting.buttonText != null)
            {
                TextMeshProUGUI btnText = target.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = setting.buttonText.text;
                    btnText.fontSize = setting.buttonText.fontSize;
                    btnText.color = setting.buttonText.fontColor;
                    btnText.alignment = setting.buttonText.alignment;
                }
            }

            // 3. 클릭 이벤트 연결
            Button btn = target.GetComponent<Button>();
            if (btn != null)
            {
                // 기존에 연결된 이벤트가 있다면 제거 (중복 실행 방지)
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => 
                {
                    Debug.Log($"[UIManager] Button Clicked: {setting.name}");
                });
            }
        }

        /// <summary> 기존 VideoPlayer 오브젝트에 설정을 적용 </summary>
        public void SetVideo(GameObject target, VideoSetting setting)
        {
            if (target == null || setting == null) return;

            target.name = setting.name;
            ApplyTransform(target.GetComponent<RectTransform>(), setting);

            VideoPlayer vp = target.GetComponent<VideoPlayer>();
            if (vp != null)
            {
                string path = Path.Combine(Application.streamingAssetsPath, setting.fileName).Replace("\\", "/");
                
                vp.source = VideoSource.Url;
                vp.url = path;
                vp.SetDirectAudioVolume(0, setting.volume);
            }
        }

        #endregion

        #region Helper Methods

        private void ApplyTransform(RectTransform rt, UISettingBase setting)
        {
            if (rt == null) return;
            
            rt.anchoredPosition = setting.position;
            rt.sizeDelta = setting.size;
            rt.localEulerAngles = setting.rotation;
            rt.localScale = setting.scale;
        }

        private Sprite LoadSprite(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string path = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");

            if (File.Exists(path))
            {
                try
                {
                    byte[] fileData = File.ReadAllBytes(path);
                    
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    
                    if (texture.LoadImage(fileData))
                    {
                        texture.Apply(false, true); 
                        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    }
                    else
                    {
                        Destroy(texture); // 로드 실패 시 제거
                        Debug.LogError($"[UIManager] Failed to load image data: {path}");
                        return null;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UIManager] Exception loading sprite: {path}, Error: {e.Message}");
                    return null;
                }
            }
            
            Debug.LogWarning($"[UIManager] Image not found: {path}");
            return null;
        }

        #endregion
    }
}