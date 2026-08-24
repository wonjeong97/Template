using System;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using Wonjeong.App;
using Wonjeong.Data;
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
        private AppSettingsProvider _settingsProvider;

        private TemplateInputActions _inputActions;
        
        private static bool _isInstantiated;

        // 중복 생성되어 파괴되는 객체가 정적 플래그를 건드리는 것을 막기 위한 인스턴스 확인 변수
        private bool _isOriginal;

        /// <summary>
        /// 의존성 주입 및 Input Action 초기화.
        /// </summary>
        [Inject]
        public void Construct(IPublisher<InspectorEvent> publisher, ILogger<GameManagerBase<T>> logger,
            AppSettingsProvider settingsProvider)
        {
            _publisher = publisher;
            _logger = logger;
            _settingsProvider = settingsProvider;

            // 입력 클래스 생성 및 콜백 연결
            _inputActions = new TemplateInputActions();
            
            _inputActions.System.ToggleDebug.performed += _ => ToggleReporterControl();
            _inputActions.System.ToggleInspector.performed += _ => ToggleInspectorUI();
            _inputActions.System.ToggleMouse.performed += _ => ToggleCursorVisibility();
            
            if (isActiveAndEnabled)
            {
                _inputActions.Enable();
            }
        }

        /// <summary>
        /// 씬 전환 시 매니저의 파괴를 방지함.
        /// 중복 생성 시 기존 객체를 보존하고 새로 생성된 객체를 파괴함.
        /// </summary>
        protected virtual void Awake()
        {
            if (!_isInstantiated)
            {
                _isInstantiated = true;
                _isOriginal = true; // 내가 최초의 원본임을 기억함

                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else
            {
                // 이미 인스턴스가 존재하면 새로 로드된 중복 객체를 즉시 파괴
                Destroy(gameObject);
            }
        }

        protected virtual void OnEnable()
        {
            _inputActions?.Enable();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        protected virtual void OnDisable()
        {
            _inputActions?.Disable();
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        
        /// <summary>
        /// 원본 객체가 파괴될 때만 정적 플래그를 해제하여 다음 번 생성이 가능하게 함.
        /// (Domain Reload 비활성화 시 정적 필드가 유지되어 재생 2회차부터 자기 자신을 파괴하는 문제 방지)
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (_isOriginal)
            {
                _isInstantiated = false;
            }

            if (_inputActions != null)
            {
                _inputActions.Dispose();
                _inputActions = null;
            }
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
            // 주입 없이 컴포넌트만 붙인 경우 원인을 알기 어려운 NullReferenceException이 발생하므로
            // 무엇을 빠뜨렸는지 알려주고 중단함.
            if (_settingsProvider == null)
            {
                if (_logger != null)
                {
                    _logger.ZLogError($"[GameManagerBase] AppSettingsProvider was not injected. Check that RegisterComponentInHierarchy is registered on the LifetimeScope.");
                }
                else
                {
                    Debug.LogError("[GameManagerBase] Dependencies were not injected. Check that RegisterComponentInHierarchy is registered on the LifetimeScope.");
                }
                return;
            }

            try
            {
                settings = await _settingsProvider.GetAsync(this.GetCancellationTokenOnDestroy());

                if (settings == null)
                {
                    if (_logger != null) _logger.ZLogError($"[GameManagerBase] Settings file not found.");
                }
                else
                {
                    ApplyFrameRateSettings();
                }
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴로 인한 정상적인 취소
            }
        }

        /// <summary>
        /// Settings.json의 targetFrameRate를 적용함. 0 이하(미설정)면 아무것도 바꾸지 않아
        /// 프로젝트의 QualitySettings 기본값을 그대로 따름.
        /// vSyncCount가 켜져 있으면 Application.targetFrameRate가 무시되고 디스플레이 주사율에
        /// 종속되므로, 값이 설정된 경우에만 vSyncCount를 0으로 끄고 targetFrameRate를 적용함.
        /// 프로젝트별로 다른 정책이 필요하면 override하여 재정의 가능.
        /// </summary>
        protected virtual void ApplyFrameRateSettings()
        {
            if (settings.targetFrameRate <= 0) return;

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = settings.targetFrameRate;

            if (_logger != null) _logger.ZLogInformation($"[GameManagerBase] targetFrameRate applied: {settings.targetFrameRate}");
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

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_logger != null) _logger.ZLogInformation($"[GameManagerBase] Scene loaded: {scene.name} (mode: {mode})");
        }
    }
}