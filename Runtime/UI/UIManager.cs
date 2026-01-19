using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Wonjeong.Data;
using Wonjeong.Utils;

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

        private FontMaps _fontMaps;
        private bool _fontsLoadedStarted;
        
        // 변경: TMP_FontAsset -> Font
        private readonly Dictionary<string, Font> _loadedFonts = new Dictionary<string, Font>();
        private readonly List<AsyncOperationHandle> _fontHandles = new List<AsyncOperationHandle>();
        
        // 변경: TextMeshProUGUI -> Text
        private readonly Dictionary<string, List<Text>> _pendingLabels = new Dictionary<string, List<Text>>();

        private void Awake()
        {
            if (_instance == null) 
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                
                LoadSettings();
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (_fontMaps != null && !_fontsLoadedStarted)
            {
                PreloadFonts();
                _fontsLoadedStarted = true;
            }
        }
        
        private void LoadSettings()
        {
            Settings settings = JsonLoader.Load<Settings>("Settings.json");
            if (settings != null)
            {
                _fontMaps = settings.fontMap;
                Debug.Log("[UIManager] Settings loaded successfully.");
            }
            else
            {
                Debug.LogWarning("[UIManager] Failed to load settings.json");
            }
        }

        // 폰트맵에 있는 주소로 Addressables 로드 시도
        private void PreloadFonts()
        {
            if (_fontMaps == null) return;

            // font1 ~ font5 순회하며 로드
            LoadSingleFont("font1", _fontMaps.font1);
            LoadSingleFont("font2", _fontMaps.font2);
            LoadSingleFont("font3", _fontMaps.font3);
            LoadSingleFont("font4", _fontMaps.font4);
            LoadSingleFont("font5", _fontMaps.font5);
            LoadSingleFont("font6", _fontMaps.font6);
            LoadSingleFont("font7", _fontMaps.font7);
            LoadSingleFont("font8", _fontMaps.font8);
            LoadSingleFont("font9", _fontMaps.font9);
        }

        private void LoadSingleFont(string key, string address)
        {
            if (string.IsNullOrEmpty(address)) return;

            // 변경: Font 타입으로 로드
            Addressables.LoadAssetAsync<Font>(address).Completed += (handle) =>
            {   
                _fontHandles.Add(handle);
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    Font loadedFont = handle.Result;

                    // 1. 캐시에 저장
                    if (!_loadedFonts.ContainsKey(key))
                    {
                        _loadedFonts.Add(key, loadedFont);
                    }

                    // 2. 이 폰트를 기다리던 대기 명단이 있는지 확인하고 적용
                    if (_pendingLabels.TryGetValue(key, out List<Text> waitingList))
                    {
                        foreach (Text txt in waitingList)
                        {
                            // 객체가 파괴되지 않았다면 폰트 적용
                            if (txt != null) 
                            {
                                txt.font = loadedFont;
                            }
                        }
                        // 처리가 끝났으니 대기 명단에서 삭제
                        _pendingLabels.Remove(key);
                    }
                }
                else
                {
                    Debug.LogError($"[UIManager] Failed to load font: {address}");
                }
            };
        }

        #region Set Methods (Configuration)

        public void SetImage(GameObject target, ImageSetting setting)
        {
            if (target == null || setting == null) return;
            
            target.name = setting.name;
            ApplyTransform(target.GetComponent<RectTransform>(), setting);

            Image img = target.GetComponent<Image>();
            if (img != null)
            {
                img.color = setting.color;
                img.type = (Image.Type)setting.type;

                Sprite sprite = LoadSprite(setting.sourceImage);
                if (sprite != null)
                {
                    img.sprite = sprite;
                }
            }
        }

        public void SetText(GameObject target, TextSetting setting)
        {
            if (target == null || setting == null) return;

            target.name = setting.name;
            ApplyTransform(target.GetComponent<RectTransform>(), setting);

            // 변경: Text 컴포넌트 사용
            Text txt = target.GetComponent<Text>();
            if (txt != null)
            {
                ApplyTextSettings(txt, setting);
            }
        }

        public void SetButton(GameObject target, ButtonSetting setting)
        {
            if (target == null || setting == null) return;

            target.name = setting.name;
            ApplyTransform(target.GetComponent<RectTransform>(), setting);

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

            if (setting.buttonText != null)
            {
                // 변경: 자식에서 Text 컴포넌트 찾기
                Text btnText = target.GetComponentInChildren<Text>();
                if (btnText != null)
                {
                    ApplyTextSettings(btnText, setting.buttonText);
                }
            }

            Button btn = target.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => 
                {
                    Debug.Log($"[UIManager] Button Clicked: {setting.name}");
                });
            }
        }

        public void SetVideo(GameObject target, VideoSetting setting)
        {
            if (target == null || setting == null) return;

            target.name = setting.name;
    
            RawImage rawImage = target.GetComponent<RawImage>();
            VideoPlayer vp = target.GetComponent<VideoPlayer>();
            AudioSource audioSource = target.GetComponent<AudioSource>(); // 오디오 소스 추가 확인

            if (vp != null && rawImage != null)
            {
                // 1. 변환 적용
                ApplyTransform(rawImage.rectTransform, setting);

                // 2. VideoManager를 통한 RenderTexture 연결
                Vector2Int size = new Vector2Int((int)setting.size.x, (int)setting.size.y);
                VideoManager.Instance.WireRawImageAndRenderTexture(vp, rawImage, size);

                // 3. 경로 해석 및 코루틴 재생 시작
                string url = VideoManager.Instance.ResolvePlayableUrl(setting.fileName);
                StartCoroutine(VideoManager.Instance.PrepareAndPlayRoutine(vp, url, audioSource, setting.volume));
            }
        }

        #endregion

        #region Helper Methods

       private void ApplyTextSettings(Text txt, TextSetting setting)
        {
            txt.text = setting.text;
            txt.fontSize = setting.fontSize;
            txt.color = setting.fontColor;
            txt.alignment = setting.alignment;

            // 캐시된 폰트 딕셔너리에서 가져오기
            if (!string.IsNullOrEmpty(setting.fontName))
            {
                // CASE 1: 이미 로딩이 끝난 폰트 (바로 적용)
                // 변경: Font 타입 사용
                if (_loadedFonts.TryGetValue(setting.fontName, out Font fontAsset))
                {
                    txt.font = fontAsset;
                }
                // CASE 2: 아직 로딩 중인 폰트 (대기 명단 등록)
                else
                {
                    // 유효하지 않은 폰트 키 체크
                    if (_fontMaps == null || !IsFontKeyValid(setting.fontName))
                    {
                        Debug.LogWarning($"[UIManager] Invalid font name: {setting.fontName}");
                        return;
                    }
                    
                    if (!_pendingLabels.ContainsKey(setting.fontName))
                    {
                        _pendingLabels[setting.fontName] = new List<Text>();
                    }
                        
                    // 중복 등록 방지
                    if (!_pendingLabels[setting.fontName].Contains(txt))
                    {
                        _pendingLabels[setting.fontName].Add(txt);    
                    }
                }
            }
        }
        
        private bool IsFontKeyValid(string fontName)
        {
            return fontName == "font1" && !string.IsNullOrEmpty(_fontMaps.font1) ||
                   fontName == "font2" && !string.IsNullOrEmpty(_fontMaps.font2) ||
                   fontName == "font3" && !string.IsNullOrEmpty(_fontMaps.font3) ||
                   fontName == "font4" && !string.IsNullOrEmpty(_fontMaps.font4) ||
                   fontName == "font5" && !string.IsNullOrEmpty(_fontMaps.font5) ||
                   fontName == "font6" && !string.IsNullOrEmpty(_fontMaps.font6) ||
                   fontName == "font7" && !string.IsNullOrEmpty(_fontMaps.font7) ||
                   fontName == "font8" && !string.IsNullOrEmpty(_fontMaps.font8) ||
                   fontName == "font9" && !string.IsNullOrEmpty(_fontMaps.font9);
            }

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
                        Destroy(texture); 
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

        private void OnDestroy()
        {
            // Addressables로 로드한 폰트 해제
            foreach (AsyncOperationHandle handle in _fontHandles)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            _fontHandles.Clear();
            _loadedFonts.Clear();
            _pendingLabels.Clear(); // 대기열도 정리
        }
    }
}