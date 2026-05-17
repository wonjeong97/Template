using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using ZLogger;

namespace Wonjeong.UI
{
    public class FadeManager : MonoBehaviour
    {
        private Canvas _fadeCanvas;
        private CanvasGroup _canvasGroup;
        private RawImage _fadeImage;
        private bool _isTransitioning;

        private ILogger<FadeManager> _logger;

        /// <summary>
        /// VContainer 의존성 주입.
        /// ZLogger 할당.
        /// </summary>
        [Inject]
        public void Construct(ILogger<FadeManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 씬 전환 시 페이드 UI 상태 유지를 위해 파괴를 방지하고 초기화함.
        /// </summary>
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            CreateFadeUI(); 
        }

        /// <summary>
        /// 동적으로 캔버스와 페이드용 검은 이미지를 생성함.
        /// 하드코딩된 UI 의존성을 줄이고 런타임에 독립적으로 동작하기 위함.
        /// </summary>
        private void CreateFadeUI()
        {
            GameObject canvasObj = new GameObject("FadeCanvas");
            canvasObj.transform.SetParent(transform);
            
            _fadeCanvas = canvasObj.AddComponent<Canvas>();
            _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fadeCanvas.sortingOrder = -1; 

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            GameObject imageObj = new GameObject("FadeImage");
            imageObj.transform.SetParent(canvasObj.transform);
            
            _fadeImage = imageObj.AddComponent<RawImage>();
            _fadeImage.color = Color.black; 
            _fadeImage.raycastTarget = false;

            RectTransform rt = _fadeImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.offsetMin = Vector2.zero; 
            rt.offsetMax = Vector2.zero; 
            rt.localScale = Vector3.one;

            _canvasGroup = imageObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
        }

        /// <summary>
        /// 화면을 점진적으로 어둡게 처리함.
        /// </summary>
        public async UniTask FadeOutAsync(float duration, CancellationToken cancellationToken = default)
        {
            if (_isTransitioning) return;
            
            if (duration <= 0f)
            {
                if (_logger != null) _logger.ZLogWarning($"[FadeManager] FadeOut duration must be positive. Using default 0.5f.");
                duration = 0.5f;
            }
            
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), cancellationToken))
            {
                await FadeAsync(0f, 1f, duration, true, cts.Token);
            }
        }

        /// <summary>
        /// 화면을 점진적으로 밝게 처리함.
        /// </summary>
        public async UniTask FadeInAsync(float duration, CancellationToken cancellationToken = default)
        {
            if (_isTransitioning) return;
            
            if (duration <= 0f)
            {
                if (_logger != null) _logger.ZLogWarning($"[FadeManager] FadeIn duration must be positive. Using default 0.5f.");
                duration = 0.5f;
            }
            
            // using 블록을 통해 비동기 작업 완료 시 연동된 토큰 소스를 안전하게 메모리 해제(Dispose)함.
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), cancellationToken))
            {
                await FadeAsync(1f, 0f, duration, false, cts.Token);
            }
        }

        /// <summary>
        /// 페이드 효과의 핵심 알파값 보간 로직.
        /// </summary>
        private async UniTask FadeAsync(float startAlpha, float endAlpha, float duration, bool isFadeOut, CancellationToken cancellationToken)
        {
            _isTransitioning = true;
            
            _fadeCanvas.sortingOrder = 999;
            _fadeImage.raycastTarget = true;

            float elapsed = 0f;

            try
            {
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
                    
                    // 프레임 대기 (GC 할당 없는 코루틴 대체재)
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                _canvasGroup.alpha = endAlpha;
            }
            catch (OperationCanceledException)
            {
                if (_logger != null) _logger.ZLogInformation($"[FadeManager] Fade transition canceled.");

                // 강제 취소된 경우, 페이드아웃 중이었더라도 UI 블록을 해제하여 소프트락을 방지함
                if (isFadeOut)
                {
                    _fadeCanvas.sortingOrder = -1;
                    _fadeImage.raycastTarget = false;
                }
            }
            finally
            {
                if (!isFadeOut) 
                {
                    _fadeCanvas.sortingOrder = -1;
                    _fadeImage.raycastTarget = false;
                }

                _isTransitioning = false;
            }
        }
    }
}