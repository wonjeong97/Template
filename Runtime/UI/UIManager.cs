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
        
        private readonly Dictionary<string, Font> _loadedFonts = new Dictionary<string, Font>();
        private readonly List<AsyncOperationHandle> _fontHandles = new List<AsyncOperationHandle>();
        
        private readonly Dictionary<string, HashSet<Text>> _pendingLabels = new Dictionary<string, HashSet<Text>>();
        private readonly Dictionary<string, UnityEngine.Events.UnityAction> _buttonActions = new Dictionary<string, UnityEngine.Events.UnityAction>();

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

        /// <summary> Addressable 시스템을 통해 단일 폰트 에셋을 비동기 로드함. </summary>
        private void LoadSingleFont(string key, string address)
        {
            if (string.IsNullOrEmpty(address)) return;

            Addressables.LoadAssetAsync<Font>(address).Completed += (handle) => ProcessLoadedFont(handle, key, address);
        }
        
        /// <summary> 비동기 로드된 폰트 결과를 처리하고 캐싱함. </summary>
        private void ProcessLoadedFont(AsyncOperationHandle<Font> handle, string key, string address)
        {
            _fontHandles.Add(handle);

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[UIManager] Failed to load font: {address}");
                return;
            }

            Font loadedFont = handle.Result;
            CacheFont(key, loadedFont);
            ApplyPendingFonts(key, loadedFont);
        }
        
        /// <summary> 로드된 폰트를 딕셔너리에 보관함. </summary>
        private void CacheFont(string key, Font loadedFont)
        {
            if (!_loadedFonts.ContainsKey(key))
            {
                _loadedFonts.Add(key, loadedFont);
            }
        }
        
        /// <summary> 로드 이전에 폰트를 요청하고 대기 중이던 텍스트 컴포넌트들에 폰트를 일괄 적용함. </summary>
        private void ApplyPendingFonts(string key, Font loadedFont)
        {
            if (!_pendingLabels.TryGetValue(key, out HashSet<Text> waitingSet)) return;

            foreach (Text txt in waitingSet)
            {
                if (txt) 
                {
                    txt.font = loadedFont;
                }
            }
            _pendingLabels.Remove(key);
        }

        #region Set Methods (Configuration)

        public void SetImage(GameObject target, ImageSetting setting)
        {
            if (target == null || setting == null) return;
            
            target.name = setting.name;
    
            if (!target.TryGetComponent(out RectTransform rt))
            {
                rt = target.AddComponent<RectTransform>();
                Debug.LogWarning("[UIManager] RectTransform component missing. Added default at runtime.");
            }
            ApplyTransform(rt, setting);

            if (!target.TryGetComponent(out Image img))
            {
                img = target.AddComponent<Image>();
                Debug.LogWarning("[UIManager] Image component missing. Added default at runtime.");
            }
    
            img.color = setting.color;
            img.type = (Image.Type)setting.type;

            Sprite sprite = LoadSprite(setting.sourceImage);
            if (sprite)
            {
                img.sprite = sprite;
            }
        }

        public void SetText(GameObject target, TextSetting setting)
        {
            if (target == null || setting == null) return;

            target.name = setting.name;
    
            if (!target.TryGetComponent(out RectTransform rt))
            {
                rt = target.AddComponent<RectTransform>();
                Debug.LogWarning("[UIManager] RectTransform component missing. Added default at runtime.");
            }
            ApplyTransform(rt, setting);

            if (!target.TryGetComponent(out Text txt))
            {
                txt = target.AddComponent<Text>();
                Debug.LogWarning("[UIManager] Text component missing. Added default at runtime.");
            }
    
            ApplyTextSettings(txt, setting);
        }

        /// <summary> 버튼 UI 요소의 위치, 배경, 텍스트 및 이벤트를 설정함. </summary>
        public void SetButton(GameObject target, ButtonSetting setting)
        {
            if (!target || setting == null) 
            {
                Debug.LogWarning("[UIManager] Target or setting is null. Cannot set button.");
                return;
            }

            target.name = setting.name;
    
            if (!target.TryGetComponent(out RectTransform rt))
            {
                rt = target.AddComponent<RectTransform>();
                Debug.LogWarning("[UIManager] RectTransform component missing. Added default at runtime.");
            }
            ApplyTransform(rt, setting);

            ApplyButtonBackground(target, setting.buttonBackgroundImage);
            ApplyButtonText(target, setting.buttonText);
            ConfigureButtonListener(target, setting.name);
        }
        
        /// <summary> 버튼의 배경 이미지와 색상을 적용함. </summary>
        private void ApplyButtonBackground(GameObject target, ImageSetting bgSetting)
        {
            if (bgSetting == null) return;

            if (!target.TryGetComponent(out Image bgImg))
            {
                bgImg = target.AddComponent<Image>();
                Debug.LogWarning("[UIManager] Image component missing. Added default at runtime.");
            }

            bgImg.color = bgSetting.color;
            Sprite sprite = LoadSprite(bgSetting.sourceImage);

            if (sprite)
            {
                bgImg.sprite = sprite;
            }
        }
        
        /// <summary> 버튼 하위의 텍스트 컴포넌트 설정을 적용함. </summary>
        private void ApplyButtonText(GameObject target, TextSetting textSetting)
        {
            if (textSetting == null) return;

            Text btnText = target.GetComponentInChildren<Text>();
            if (!btnText)
            {
                btnText = target.AddComponent<Text>();
                Debug.LogWarning("[UIManager] Child Text component missing. Added default to root object at runtime.");
            }

            ApplyTextSettings(btnText, textSetting);
        }
        
        /// <summary> 버튼의 클릭 이벤트 리스너를 초기화하고 재설정함. </summary>
        private void ConfigureButtonListener(GameObject target, string buttonName)
        {
            if (!target.TryGetComponent(out Button btn))
            {
                btn = target.AddComponent<Button>();
                Debug.LogWarning("[UIManager] Button component missing. Added default at runtime.");
            }

            btn.onClick.RemoveAllListeners();

            if (!_buttonActions.TryGetValue(buttonName, out UnityEngine.Events.UnityAction action))
            {
                action = () => OnButtonClicked(buttonName);
                _buttonActions.Add(buttonName, action);
            }

            btn.onClick.AddListener(action);
        }
        
        /// <summary> 버튼 클릭 시 호출되는 공통 콜백 메서드. </summary>
        private void OnButtonClicked(string buttonName)
        {
            Debug.Log($"[UIManager] Button Clicked: {buttonName}");
        }

        public void SetVideo(GameObject target, VideoSetting setting)
        {
            if (target == null || setting == null) return;

            target.name = setting.name;

            if (!target.TryGetComponent(out RawImage rawImage))
            {
                rawImage = target.AddComponent<RawImage>();
                Debug.LogWarning("[UIManager] RawImage component missing. Added default at runtime.");
            }
    
            if (!target.TryGetComponent(out VideoPlayer vp))
            {
                vp = target.AddComponent<VideoPlayer>();
                Debug.LogWarning("[UIManager] VideoPlayer component missing. Added default at runtime.");
            }
    
            if (!target.TryGetComponent(out AudioSource audioSource))
            {
                audioSource = target.AddComponent<AudioSource>();
                Debug.LogWarning("[UIManager] AudioSource component missing. Added default at runtime.");
            }

            ApplyTransform(rawImage.rectTransform, setting);

            Vector2Int size = new Vector2Int((int)setting.size.x, (int)setting.size.y);
            VideoManager.Instance.WireRawImageAndRenderTexture(vp, rawImage, size);

            string url = VideoManager.Instance.ResolvePlayableUrl(setting.fileName);
            StartCoroutine(VideoManager.Instance.PrepareAndPlayRoutine(vp, url, audioSource, setting.volume));
        }

        #endregion

        #region Helper Methods

        /// <summary> 텍스트 컴포넌트의 텍스트, 크기, 정렬 및 폰트를 설정함. </summary>
        private void ApplyTextSettings(Text txt, TextSetting setting)
        {
            if (!txt || setting == null) return;

            txt.text = setting.text;
            txt.fontSize = setting.fontSize;
            txt.color = setting.fontColor;
            txt.alignment = setting.alignment;

            AssignOrQueueFont(txt, setting.fontName);
        }
        
        /// <summary> 로드된 폰트가 있으면 즉시 적용하고, 없다면 대기열에 등록함. </summary>
        private void AssignOrQueueFont(Text txt, string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return;

            if (_loadedFonts.TryGetValue(fontName, out Font fontAsset))
            {
                txt.font = fontAsset;
                return;
            }

            QueuePendingFont(txt, fontName);
        }
        
        /// <summary>
        /// 비동기 로딩 중인 폰트를 대기하는 리스트에 텍스트 컴포넌트를 추가함.
        /// </summary>
        private void QueuePendingFont(Text txt, string fontName)
        {
            if (_fontMaps == null || !IsFontKeyValid(fontName))
            {
                Debug.LogWarning($"[UIManager] Invalid font name or uninitialized font maps: {fontName}");
                return;
            }
    
            if (!_pendingLabels.ContainsKey(fontName))
            {
                _pendingLabels[fontName] = new HashSet<Text>();
            }
        
            _pendingLabels[fontName].Add(txt);    
        }
        
        /// <summary>
        /// 요청된 폰트 키가 유효하고 JSON에 매핑된 주소가 존재하는지 검사함.
        /// </summary>
        /// <returns>유효한 폰트 키이며 에셋 주소가 존재할 경우 true.</returns>
        private bool IsFontKeyValid(string fontName)
        {
            string fontAddress = fontName switch
            {
                "font1" => _fontMaps.font1,
                "font2" => _fontMaps.font2,
                "font3" => _fontMaps.font3,
                "font4" => _fontMaps.font4,
                "font5" => _fontMaps.font5,
                "font6" => _fontMaps.font6,
                "font7" => _fontMaps.font7,
                "font8" => _fontMaps.font8,
                "font9" => _fontMaps.font9,
                _ => null // 일치하는 키가 없으면 null 반환
            };

            return !string.IsNullOrEmpty(fontAddress);
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