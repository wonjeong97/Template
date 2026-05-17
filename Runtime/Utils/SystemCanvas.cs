using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.UI; // GraphicRaycaster 사용을 위해 추가
using VContainer;
using ZLogger;

namespace Wonjeong.Utils
{
    public class SystemCanvas : MonoBehaviour
    {
        // 씬 중복 로드 시 캔버스가 여러 개 생기는 것을 막기 위한 내부 플래그
        private static bool _isInstantiated;
        
        // 중복 생성되어 파괴되는 객체가 정적 플래그를 건드리는 것을 막기 위한 인스턴스 확인 변수
        private bool _isOriginal;

        private ILogger<SystemCanvas> _logger;

        /// <summary>
        /// VContainer 의존성 주입.
        /// ZLogger 할당.
        /// </summary>
        [Inject]
        public void Construct(ILogger<SystemCanvas> logger)
        {
            _logger = logger;
        }

        private void Awake()
        {
            if (!_isInstantiated)
            {
                _isInstantiated = true;
                _isOriginal = true; // 내가 최초의 원본임을 기억함
                DontDestroyOnLoad(gameObject);
                InitializeCanvas();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 원본 객체가 파괴될 때만 정적 플래그를 해제하여 다음 번 생성이 가능하게 함.
        /// </summary>
        private void OnDestroy()
        {
            if (_isOriginal)
            {
                _isInstantiated = false;
            }
        }

        /// <summary>
        /// 캔버스의 렌더 모드와 Sorting Order를 설정하여 최상단 렌더링을 보장함.
        /// </summary>
        private void InitializeCanvas()
        {
            if (!TryGetComponent(out Canvas canvas))
            {
                canvas = gameObject.AddComponent<Canvas>();
                
                if (_logger != null)
                {
                    _logger.ZLogWarning($"[SystemCanvas] Canvas component missing. Added default at runtime.");
                }
            }
    
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; 
            canvas.sortingOrder = 30000; 
            
            if (!TryGetComponent(out GraphicRaycaster raycaster))
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }
        }
    }
}