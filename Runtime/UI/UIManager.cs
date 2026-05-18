using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using UnityEngine.Video;
using VContainer;
using Wonjeong.Data;
using Wonjeong.Utils;
using ZLogger;

namespace Wonjeong.UI
{
    public class UIManager : MonoBehaviour
    {
        private FontMaps _fontMaps;
        private bool _fontsLoadedStarted;

        private readonly Dictionary<string, Font> _loadedFonts = new Dictionary<string, Font>();
        private readonly List<AsyncOperationHandle> _fontHandles = new List<AsyncOperationHandle>();

        private readonly Dictionary<string, HashSet<Text>> _pendingLabels = new Dictionary<string, HashSet<Text>>();

        private readonly Dictionary<string, UnityEngine.Events.UnityAction> _buttonActions =
            new Dictionary<string, UnityEngine.Events.UnityAction>();

        private readonly Dictionary<string, Sprite> _cachedSprites = new Dictionary<string, Sprite>();

        private ILogger<UIManager> _logger;
        private VideoManager _videoManager;

        /// <summary>
        /// VContainer 의존성 주입.
        /// 로거 및 비디오 매니저 할당.
        /// </summary>
        [Inject]
        public void Construct(ILogger<UIManager> logger, VideoManager videoManager)
        {
            _logger = logger;
            _videoManager = videoManager;
        }

        /// <summary>
        /// 씬 전환 시 UI 매니저 파괴를 방지하고 설정을 로드함.
        /// </summary>
        private void Awake()
        {
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
            LoadSettings();
        }

        /// <summary>
        /// 런타임 시작 시 폰트 비동기 로드를 백그라운드에서 실행함.
        /// </summary>
        private void Start()
        {
            if (_fontMaps != null && !_fontsLoadedStarted)
            {
                _fontsLoadedStarted = true;
                PreloadFontsAsync(this.GetCancellationTokenOnDestroy()).Forget();
            }
        }

        /// <summary>
        /// 로컬 환경 설정을 로드하여 폰트맵 데이터를 캐싱함.
        /// </summary>
        private void LoadSettings()
        {
            Settings settings = JsonLoader.Load<Settings>("Settings.json");
            if (settings != null)
            {
                _fontMaps = settings.fontMap;
                if (_logger != null) _logger.ZLogInformation($"[UIManager] Settings loaded successfully.");
            }
            else
            {
                if (_logger != null) _logger.ZLogWarning($"[UIManager] Failed to load settings.json");
            }
        }

        /// <summary>
        /// 설정된 모든 폰트를 병렬로 비동기 로드함.
        /// 로딩 속도 최적화 목적.
        /// </summary>
        private async UniTask PreloadFontsAsync(CancellationToken cancellationToken)
        {
            if (_fontMaps == null) return;

            List<UniTask> tasks = new List<UniTask>
            {
                LoadSingleFontAsync("font1", _fontMaps.font1, cancellationToken),
                LoadSingleFontAsync("font2", _fontMaps.font2, cancellationToken),
                LoadSingleFontAsync("font3", _fontMaps.font3, cancellationToken),
                LoadSingleFontAsync("font4", _fontMaps.font4, cancellationToken),
                LoadSingleFontAsync("font5", _fontMaps.font5, cancellationToken),
                LoadSingleFontAsync("font6", _fontMaps.font6, cancellationToken),
                LoadSingleFontAsync("font7", _fontMaps.font7, cancellationToken),
                LoadSingleFontAsync("font8", _fontMaps.font8, cancellationToken),
                LoadSingleFontAsync("font9", _fontMaps.font9, cancellationToken)
            };

            // 모든 폰트가 동시에 로드되도록 병렬 대기
            await UniTask.WhenAll(tasks);
        }

        /// <summary>
        /// Addressable 시스템을 통해 단일 폰트 에셋을 비동기 로드함.
        /// </summary>
        private async UniTask LoadSingleFontAsync(string key, string address, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(address)) return;

            try
            {
                AsyncOperationHandle<Font> handle = Addressables.LoadAssetAsync<Font>(address);
                _fontHandles.Add(handle);

                // 제네릭 반환 타입(void) 에러 방지를 위해 대기와 결과 추출을 분리함.
                await handle.ToUniTask(cancellationToken: cancellationToken);

                Font loadedFont = handle.Result;

                if (loadedFont)
                {
                    CacheFont(key, loadedFont);
                    ApplyPendingFonts(key, loadedFont);
                }
                else
                {
                    if (_logger != null) _logger.ZLogError($"[UIManager] Font loaded but result is null: {address}");
                }
            }
            catch (OperationCanceledException)
            {
                // 파괴 시 정상 취소 처리
            }
            catch (Exception e)
            {
                if (_logger != null)
                    _logger.ZLogError($"[UIManager] Failed to load font: {address}. Error: {e.Message}");
            }
        }

        /// <summary>
        /// 로드된 폰트를 딕셔너리에 보관함.
        /// </summary>
        private void CacheFont(string key, Font loadedFont)
        {
            if (!_loadedFonts.ContainsKey(key))
            {
                _loadedFonts.Add(key, loadedFont);
            }
        }

        /// <summary>
        /// 로드 이전에 폰트를 요청하고 대기 중이던 텍스트 컴포넌트들에 폰트를 일괄 적용함.
        /// </summary>
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

        /// <summary>
        /// 이미지 UI 요소 속성 설정 및 비동기 텍스처 로드 적용.
        /// </summary>
        public void SetImage(GameObject target, ImageSetting setting)
        {
            if (!target || setting == null) return;

            target.name = setting.name;

            if (!target.TryGetComponent(out RectTransform rt))
            {
                rt = target.AddComponent<RectTransform>();
            }

            ApplyTransform(rt, setting);

            if (!target.TryGetComponent(out Image img))
            {
                img = target.AddComponent<Image>();
            }

            img.color = setting.color;
            img.type = (Image.Type)setting.type;

            ApplySpriteAsync(img, setting.sourceImage, this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// 텍스트 UI 요소의 속성, 변환 및 폰트를 설정함.
        /// </summary>
        public void SetText(GameObject target, TextSetting setting)
        {
            if (!target || setting == null) return;

            target.name = setting.name;

            if (!target.TryGetComponent(out RectTransform rt))
            {
                rt = target.AddComponent<RectTransform>();
            }

            ApplyTransform(rt, setting);

            if (!target.TryGetComponent(out Text txt))
            {
                txt = target.AddComponent<Text>();
            }

            ApplyTextSettings(txt, setting);
        }

        /// <summary>
        /// 버튼 UI 요소의 위치, 배경, 텍스트 및 이벤트를 설정함.
        /// </summary>
        public void SetButton(GameObject target, ButtonSetting setting)
        {
            if (!target || setting == null)
            {
                if (_logger != null) _logger.ZLogWarning($"[UIManager] Target or setting is null. Cannot set button.");
                return;
            }

            target.name = setting.name;

            if (!target.TryGetComponent(out RectTransform rt))
            {
                rt = target.AddComponent<RectTransform>();
            }

            ApplyTransform(rt, setting);

            ApplyButtonBackground(target, setting.buttonBackgroundImage);
            ApplyButtonText(target, setting.buttonText);
            ConfigureButtonListener(target, setting.name);
        }

        /// <summary>
        /// 버튼의 배경 이미지와 색상을 적용함.
        /// </summary>
        private void ApplyButtonBackground(GameObject target, ImageSetting bgSetting)
        {
            if (bgSetting == null) return;

            if (!target.TryGetComponent(out Image bgImg))
            {
                bgImg = target.AddComponent<Image>();
            }

            bgImg.color = bgSetting.color;
            ApplySpriteAsync(bgImg, bgSetting.sourceImage, this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// 버튼 하위의 텍스트 컴포넌트 설정을 적용함.
        /// </summary>
        private void ApplyButtonText(GameObject target, TextSetting textSetting)
        {
            if (textSetting == null) return;

            Text btnText = target.GetComponentInChildren<Text>();
            if (!btnText)
            {
                btnText = target.AddComponent<Text>();
            }

            ApplyTextSettings(btnText, textSetting);
        }

        /// <summary>
        /// 버튼의 클릭 이벤트 리스너를 초기화하고 재설정함.
        /// </summary>
        private void ConfigureButtonListener(GameObject target, string buttonName)
        {
            if (!target.TryGetComponent(out Button btn))
            {
                btn = target.AddComponent<Button>();
            }

            btn.onClick.RemoveAllListeners();

            if (!_buttonActions.TryGetValue(buttonName, out UnityEngine.Events.UnityAction action))
            {
                action = () => OnButtonClicked(buttonName);
                _buttonActions.Add(buttonName, action);
            }

            btn.onClick.AddListener(action);
        }

        /// <summary>
        /// 버튼 클릭 시 호출되는 공통 콜백 로직.
        /// </summary>
        private void OnButtonClicked(string buttonName)
        {
            if (_logger != null) _logger.ZLogInformation($"[UIManager] Button Clicked: {buttonName}");
        }

        /// <summary>
        /// 비디오 UI 요소의 렌더 텍스처를 연결하고 재생을 준비함.
        /// </summary>
        public void SetVideo(GameObject target, VideoSetting setting)
        {
            if (!target || setting == null) return;
            if (!_videoManager) return;

            target.name = setting.name;

            if (!target.TryGetComponent(out RawImage rawImage)) rawImage = target.AddComponent<RawImage>();
            if (!target.TryGetComponent(out VideoPlayer vp)) vp = target.AddComponent<VideoPlayer>();
            if (!target.TryGetComponent(out AudioSource audioSource)) audioSource = target.AddComponent<AudioSource>();

            ApplyTransform(rawImage.rectTransform, setting);

            Vector2Int size = new Vector2Int((int)setting.size.x, (int)setting.size.y);
            _videoManager.WireRawImageAndRenderTexture(vp, rawImage, size);

            string url = _videoManager.ResolvePlayableUrl(setting.fileName);

            _videoManager
                .PrepareAndPlayAsync(vp, url, audioSource, setting.volume, this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 비동기 이미지 로드 완료 후 컴포넌트에 할당함.
        /// </summary>
        private async UniTaskVoid ApplySpriteAsync(Image img, string sourceImage, CancellationToken cancellationToken)
        {
            Sprite sprite = await LoadSpriteAsync(sourceImage, cancellationToken);
            if (sprite && img)
            {
                img.sprite = sprite;
            }
        }

        /// <summary>
        /// 텍스트 컴포넌트의 텍스트, 크기, 정렬 및 폰트를 설정함.
        /// </summary>
        private void ApplyTextSettings(Text txt, TextSetting setting)
        {
            if (!txt || setting == null) return;

            txt.text = setting.text;
            txt.fontSize = setting.fontSize;
            txt.color = setting.fontColor;
            txt.alignment = setting.alignment;

            AssignOrQueueFont(txt, setting.fontName);
        }

        /// <summary>
        /// 로드된 폰트가 있으면 즉시 적용하고, 없다면 대기열에 등록함.
        /// </summary>
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
                if (_logger != null)
                    _logger.ZLogWarning($"[UIManager] Invalid font name or uninitialized font maps: {fontName}");
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
                _ => null
            };

            return !string.IsNullOrEmpty(fontAddress);
        }

        /// <summary>
        /// UI 요소의 기본 RectTransform 속성을 적용함.
        /// </summary>
        private void ApplyTransform(RectTransform rt, UISettingBase setting)
        {
            if (!rt) return;

            rt.anchoredPosition = setting.position;
            rt.sizeDelta = setting.size;
            rt.localEulerAngles = setting.rotation;
            rt.localScale = setting.scale;
        }

        /// <summary>
        /// 로컬 스토리지에서 이미지 파일을 비동기로 읽어와 Sprite로 변환함.
        /// 메모리 최적화를 위해 이미 로드된 이미지는 캐싱하여 재사용함.
        /// </summary>
        private async UniTask<Sprite> LoadSpriteAsync(string fileName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            string path = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");

            if (_cachedSprites.TryGetValue(path, out Sprite cachedSprite))
            {
                return cachedSprite;
            }

            if (File.Exists(path))
            {
                try
                {
                    byte[] fileData = await File.ReadAllBytesAsync(path, cancellationToken);

                    await UniTask.SwitchToMainThread(cancellationToken);

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                    if (texture.LoadImage(fileData))
                    {
                        texture.Apply(false, true);
                        Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f));

                        _cachedSprites[path] = newSprite;
                        return newSprite;
                    }
                    else
                    {
                        Destroy(texture);
                        if (_logger != null) _logger.ZLogError($"[UIManager] Failed to decode image data: {path}");
                        return null;
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception e)
                {
                    if (_logger != null)
                        _logger.ZLogError($"[UIManager] Exception loading sprite: {path}, Error: {e.Message}");
                    return null;
                }
            }

            if (_logger != null) _logger.ZLogWarning($"[UIManager] Image not found: {path}");
            return null;
        }

        #endregion

        /// <summary>
        /// 유니티 생명주기 종료 시 Addressables 및 동적 텍스처 메모리를 해제함.
        /// </summary>
        private void OnDestroy()
        {
            foreach (AsyncOperationHandle handle in _fontHandles)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            _fontHandles.Clear();
            _loadedFonts.Clear();
            _pendingLabels.Clear();

            // 동적으로 생성한 텍스처와 스프라이트를 VRAM에서 완전 삭제하여 누수 방지
            foreach (Sprite sprite in _cachedSprites.Values)
            {
                if (sprite)
                {
                    if (sprite.texture) Destroy(sprite.texture);
                    Destroy(sprite);
                }
            }

            _cachedSprites.Clear();
        }
    }
}