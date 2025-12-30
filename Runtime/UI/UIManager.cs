using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
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
        
        // 로드된 폰트를 저장할 캐시
        private readonly Dictionary<string, TMP_FontAsset> _loadedFonts = new Dictionary<string, TMP_FontAsset>();
        private readonly List<AsyncOperationHandle> _fontHandles = new List<AsyncOperationHandle>();
        
        // 폰트 로딩을 기다리는 텍스트 컴포넌트 대기 명단
        // Key: "font1", Value: 해당 폰트를 기다리는 TMP 객체 리스트
        private readonly Dictionary<string, List<TextMeshProUGUI>> _pendingLabels = new Dictionary<string, List<TextMeshProUGUI>>();

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

        private void Start()
        {
            // 1. Settings 로드
            Settings settings = JsonLoader.Load<Settings>("Settings.json");
            if (settings != null)
            {
                _fontMaps = settings.fontMap;
                
                // 2. 폰트 비동기 프리로드 시작
                if (!_fontsLoadedStarted)
                {
                    PreloadFonts();
                    _fontsLoadedStarted = true;
                }
            }
            else
            {
                Debug.LogWarning("[UIManager] Failed to load settings.json");
            }
        }

        public void Init(Settings settings)
        {
            if (settings != null)
            {
                _fontMaps = settings.fontMap;
                if (!_fontsLoadedStarted)
                {
                    PreloadFonts();
                    _fontsLoadedStarted = true;
                }
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
        }

        private void LoadSingleFont(string key, string address)
        {
            if (string.IsNullOrEmpty(address)) return;

            // Addressables를 통해 폰트 비동기 로드
            Addressables.LoadAssetAsync<TMP_FontAsset>(address).Completed += (handle) =>
            {   
                _fontHandles.Add(handle);
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    TMP_FontAsset loadedFont = handle.Result;

                    // 1. 캐시에 저장
                    if (!_loadedFonts.ContainsKey(key))
                    {
                        _loadedFonts.Add(key, loadedFont);
                    }

                    // 2. 이 폰트를 기다리던 대기 명단이 있는지 확인하고 적용
                    if (_pendingLabels.TryGetValue(key, out List<TextMeshProUGUI> waitingList))
                    {
                        foreach (TextMeshProUGUI tmp in waitingList)
                        {
                            // 객체가 파괴되지 않았다면 폰트 적용
                            if (tmp != null) 
                            {
                                tmp.font = loadedFont;
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

            TextMeshProUGUI tmp = target.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                ApplyTextSettings(tmp, setting);
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
                TextMeshProUGUI btnText = target.GetComponentInChildren<TextMeshProUGUI>();
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

        private void ApplyTextSettings(TextMeshProUGUI tmp, TextSetting setting)
        {
            tmp.text = setting.text;
            tmp.fontSize = setting.fontSize;
            tmp.color = setting.fontColor;
            tmp.alignment = setting.alignment;

            // 캐시된 폰트 딕셔너리에서 가져오기
            if (!string.IsNullOrEmpty(setting.fontName))
            {
                // CASE 1: 이미 로딩이 끝난 폰트 (바로 적용)
                if (_loadedFonts.TryGetValue(setting.fontName, out TMP_FontAsset fontAsset))
                {
                    tmp.font = fontAsset;
                }
                // CASE 2: 아직 로딩 중인 폰트 (대기 명단 등록)
                else
                {
                    if (!_pendingLabels.ContainsKey(setting.fontName))
                    {
                        _pendingLabels[setting.fontName] = new List<TextMeshProUGUI>();
                    }

                    _pendingLabels[setting.fontName].Add(tmp);
                }
            }
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