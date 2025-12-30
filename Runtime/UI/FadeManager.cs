using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Wonjeong.UI
{
    public class FadeManager : MonoBehaviour
    {
        private static FadeManager _instance;
        public static FadeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<FadeManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("FadeManager");
                        _instance = go.AddComponent<FadeManager>();
                    }
                }
                return _instance;
            }
        }

        private Canvas _fadeCanvas;
        private CanvasGroup _canvasGroup;
        private RawImage _fadeImage;
        private bool _isTransitioning = false;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                CreateFadeUI(); // 스스로 UI 생성
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        /// <summary> 동적으로 캔버스와 까만 이미지를 생성합니다. </summary>
        private void CreateFadeUI()
        {
            // 1. 캔버스 생성 및 설정
            GameObject canvasObj = new GameObject("FadeCanvas");
            canvasObj.transform.SetParent(transform);
            _fadeCanvas = canvasObj.AddComponent<Canvas>();
            _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fadeCanvas.sortingOrder = -1; // 초기엔 제일 뒤로

            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // 2. 페이드용 이미지 생성
            GameObject imageObj = new GameObject("FadeImage");
            imageObj.transform.SetParent(canvasObj.transform);
            
            _fadeImage = imageObj.AddComponent<RawImage>();
            _fadeImage.color = Color.black; // 까만 이미지 설정
            _fadeImage.raycastTarget = false;

            // 화면 가득 채우기 (RectTransform 설정)
            RectTransform rt = _fadeImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            // 3. 투명도 조절을 위한 CanvasGroup 추가
            _canvasGroup = imageObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
        }

        public void FadeOut(float duration, System.Action onComplete = null)
        {
            if (_isTransitioning) return;
            StartCoroutine(FadeRoutine(0f, 1f, duration, true, onComplete));
        }

        public void FadeIn(float duration, System.Action onComplete = null)
        {
            if (_isTransitioning) return;
            StartCoroutine(FadeRoutine(1f, 0f, duration, false, onComplete));
        }

        private IEnumerator FadeRoutine(float startAlpha, float endAlpha, float duration, bool isFadeOut, System.Action onComplete)
        {
            _isTransitioning = true;
            
            // 시작 시 최상단으로 이동 및 입력 차단
            _fadeCanvas.sortingOrder = 999;
            _fadeImage.raycastTarget = true;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
                yield return null;
            }

            _canvasGroup.alpha = endAlpha;

            // 종료 시 상태 처리
            if (!isFadeOut) // FadeIn이 끝나서 투명해졌을 때
            {
                _fadeCanvas.sortingOrder = -1;
                _fadeImage.raycastTarget = false;
            }

            _isTransitioning = false;
            onComplete?.Invoke();
        }
    }
}