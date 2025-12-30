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

            target.name = setting.name; // 디버깅 편의를 위해 이름 변경
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
                
                // 폰트 변경이 필요하다면 여기에 로직 추가 (Resources.Load 등)
                // if (!string.IsNullOrEmpty(setting.fontName)) ...

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
                // 버튼 자체에 Image 컴포넌트가 있다고 가정
                Image bgImg = target.GetComponent<Image>(); 
                if (bgImg != null)
                {
                    bgImg.color = setting.buttonBackgroundImage.color;
                    Sprite sprite = LoadSprite(setting.buttonBackgroundImage.sourceImage);
                    if (sprite != null) bgImg.sprite = sprite;
                }
            }

            // 2. 버튼 텍스트 적용 (자식 오브젝트 검색)
            if (setting.buttonText != null)
            {
                TextMeshProUGUI btnText = target.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    // 텍스트 내용은 별도 설정 객체를 재사용하거나 직접 주입
                    btnText.text = setting.buttonText.text;
                    btnText.fontSize = setting.buttonText.fontSize;
                    btnText.color = setting.buttonText.fontColor;
                    btnText.alignment = setting.buttonText.alignment;
                }
            }

            // 3. 클릭 이벤트 연결 (필요 시)
            Button btn = target.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners(); // 기존 리스너 제거 (중복 방지)
                btn.onClick.AddListener(() => 
                {
                    Debug.Log($"[UIManager] Button Clicked: {setting.name}");
                    // 사운드 재생 등 공통 로직 추가 가능
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
                
                // 즉시 재생할지 여부는 기획에 따라 결정
                // vp.Play(); 
            }
        }

        #endregion

        #region Helper Methods

        // 공통 Transform 적용 (RectTransform 기준)
        private void ApplyTransform(RectTransform rt, UISettingBase setting)
        {
            if (rt == null) return;
            
            rt.anchoredPosition = setting.position;
            rt.sizeDelta = setting.size;
            rt.localEulerAngles = setting.rotation;
            rt.localScale = setting.scale;
        }

        // StreamingAssets에서 이미지 로드 후 Sprite 변환
        private Sprite LoadSprite(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (!fileName.Contains(".")) fileName += ".png";

            string path = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");

            if (File.Exists(path))
            {
                byte[] fileData = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2);
                if (texture.LoadImage(fileData))
                {
                    return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            }
            
            // 파일이 없을 땐 경고만 (빈 이미지 사용 시)
            Debug.LogWarning($"[UIManager] Image not found: {path}");
            return null;
        }

        #endregion
    }
}