using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer;
using ZLogger;

namespace Wonjeong.Utils
{
    public class SystemCanvas : MonoBehaviour
    {
        // 씬 중복 로드 시 캔버스가 여러 개 생기는 것을 막기 위한 내부 플래그
        private static bool _isInstantiated;

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
                DontDestroyOnLoad(gameObject);
                InitializeCanvas();
            }
            else
            {
                Destroy(gameObject);
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
        }
    }
}