using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
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
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
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

            // CanvasScaler를 의도적으로 붙이지 않음. ScreenSpaceOverlay 캔버스는 항상 화면
            // 크기와 일치하고, 페이드 이미지는 풀스트레치 앵커(0~1)로 캔버스 전체를 따라가므로
            // 어떤 해상도·비율에서도 화면 전체를 덮음. 특정 기준 해상도(1920x1080 등)를
            // 설정하면 "해당 해상도 전용"이라는 오해만 부르고 실익이 없음.
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
            // 진행 중이면 요청을 무시함. 호출자는 await가 끝나면 페이드가 완료된 줄 알기 때문에,
            // 무음으로 넘기면 "화면이 검은 채로 멈췄다" 같은 증상의 원인을 추적할 수 없음.
            if (_isTransitioning)
            {
                if (_logger != null)
                {
                    _logger.ZLogWarning($"[FadeManager] 이미 페이드가 진행 중이어서 FadeOut 요청을 무시했습니다.");
                }
                return;
            }

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
            // FadeOut과 동일한 이유로 무시 사유를 남김.
            // 특히 FadeIn이 무시되면 화면이 검은 상태로 남아 증상이 심각함.
            if (_isTransitioning)
            {
                if (_logger != null)
                {
                    _logger.ZLogWarning($"[FadeManager] 이미 페이드가 진행 중이어서 FadeIn 요청을 무시했습니다. 화면이 어두운 상태로 남을 수 있습니다.");
                }
                return;
            }

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

            try
            {
                _canvasGroup.alpha = startAlpha;

                // SetUpdate(true): Time.timeScale과 무관한 독립 업데이트로 실행함.
                // deltaTime 기반 수동 루프는 timeScale=0일 때 페이드가 영원히 끝나지 않아
                // _isTransitioning이 true로 굳고 raycastTarget이 켜진 채 남아
                // 화면 전체 입력이 차단되는 소프트락이 발생했음. 연출은 게임 시간과 무관해야 함.
                //
                // KillAndCancelAwait: 취소 시 트윈을 즉시 제거하고 OperationCanceledException을
                // 던져, 기존 수동 루프와 동일한 취소 처리 흐름(catch 블록)을 유지함.
                await _canvasGroup.DOFade(endAlpha, duration)
                    .SetEase(Ease.Linear)
                    .SetUpdate(true)
                    .ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
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