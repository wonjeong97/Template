using Cysharp.Threading.Tasks;
using MessagePipe;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer;
using Wonjeong.App;
using Wonjeong.Data;
using Wonjeong.Utils;
using ZLogger;

namespace Wonjeong.Core
{
    /// <summary> 게임 매니저 베이스 클래스. </summary>
    public abstract class GameManagerBase<T> : MonoBehaviour where T : GameManagerBase<T>
    {
        [SerializeField] private Reporter.Reporter reporter;
        [SerializeField] private GameObject inspectorContainer;
        
        protected Settings settings;
        
        private IPublisher<InspectorEvent> _publisher;
        private ILogger<GameManagerBase<T>> _logger;

        private TemplateInputActions _inputActions;
        
        /// <summary>
        /// 의존성 주입 및 Input Action 초기화.
        /// </summary>
        [Inject]
        public void Construct(IPublisher<InspectorEvent> publisher, ILogger<GameManagerBase<T>> logger)
        {
            _publisher = publisher;
            _logger = logger;
            
            // 입력 클래스 생성 및 콜백 연결
            _inputActions = new TemplateInputActions();
            
            _inputActions.System.ToggleDebug.performed += _ => ToggleReporterControl();
            _inputActions.System.ToggleInspector.performed += _ => ToggleInspectorUI();
            _inputActions.System.ToggleMouse.performed += _ => ToggleCursorVisibility();
        }

        protected virtual void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        protected virtual void OnEnable()
        {
            // 오브젝트 활성화 시 입력 감지 시작
            _inputActions?.Enable();
        }

        protected virtual void OnDisable()
        {
            // 오브젝트 비활성화 시 입력 감지 중지 (메모리 및 오작동 방지)
            _inputActions?.Disable();
        }

        /// <summary>
        /// 런타임 시작 시 디버그 UI 및 커서를 초기화하고 비동기로 설정을 로드함.
        /// </summary>
        protected virtual void Start()
        {
            Cursor.visible = false;

            if (reporter)
            {
                if (reporter.show)
                {
                    reporter.show = false;
                }
            }
            else
            {
                if (_logger != null) _logger.ZLogWarning($"[GameManagerBase] reporter is null. Debug UI will be disabled.");
            }

            if (inspectorContainer)
            {
                inspectorContainer.SetActive(false);
            }
            else
            {
                if (_logger != null) _logger.ZLogWarning($"[GameManagerBase] inspectorContainer is null.");
            }
            
            LoadSettingsAsync().Forget();
        }

        /// <summary>
        /// 환경 설정을 비동기로 로드하여 메인 스레드 블로킹을 방지함.
        /// </summary>
        protected virtual async UniTaskVoid LoadSettingsAsync()
        {
            settings = await JsonLoader.LoadAsync<Settings>("Settings.json", this.GetCancellationTokenOnDestroy());
            
            if (settings == null)
            {
                if (_logger != null) _logger.ZLogError($"[GameManagerBase] Settings file not found.");
            }
        }

        /// <summary>
        /// Reporter UI의 활성화 상태를 토글함.
        /// </summary>
        private void ToggleReporterControl()
        {
            if (!reporter)
            {
                if (_logger != null) _logger.ZLogWarning($"[GameManagerBase] reporter is null. Cannot toggle control.");
                return;
            }

            reporter.showGameManagerControl = !reporter.showGameManagerControl;

            if (reporter.show)
            {
                reporter.show = false;
            }
        }
        
        /// <summary>
        /// 런타임 인스펙터 UI의 활성화 상태를 토글하고 이벤트를 발행함.
        /// </summary>
        private void ToggleInspectorUI()
        {
            if (!inspectorContainer)
            {
                if (_logger != null) _logger.ZLogWarning($"[GameManagerBase] inspectorContainer is null. Cannot toggle inspector.");
                return;
            }

            bool isActivating = !inspectorContainer.activeSelf;
            inspectorContainer.SetActive(isActivating);

            if (!isActivating)
            {
                _publisher?.Publish(new InspectorEvent());
            }
        }
        
        private void ToggleCursorVisibility()
        {
            Cursor.visible = !Cursor.visible;
        }
    }
}