using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VContainer;
using Wonjeong.App;
using Wonjeong.Data;
using ZLogger;

namespace Wonjeong.Core
{
    /// <summary>
    /// 일정 시간 동안 입력이 없으면 InactivityTimeoutEvent를 발행하는 범용 비활동 타이머.
    /// "최초 화면"이 별도 씬인지, 같은 씬의 첫 패널인지는 프로젝트마다 다르므로,
    /// 이 컴포넌트는 언제 타임아웃됐는지만 알리고 실제 복귀 로직은 프로젝트가
    /// ISubscriber&lt;InactivityTimeoutEvent&gt;를 구독해 구현함.
    /// Settings.json의 useInactivityTimer가 false거나 resetTime이 0 이하이면 동작하지 않음.
    /// 긴 영상 재생처럼 입력 없이도 사용자가 실제로는 콘텐츠를 보고 있는 구간에서는
    /// Pause()/Resume()으로 카운트를 일시 중지할 수 있음.
    /// </summary>
    public class InactivityTimer : MonoBehaviour
    {
        private IPublisher<InactivityTimeoutEvent> _publisher;

        private bool _isEnabled;
        private bool _isPaused;
        private float _timeoutSeconds;
        private float _lastActivityTime;
        private bool _hasTimedOut;

        private ILogger<InactivityTimer> _logger;
        private AppSettingsProvider _settingsProvider;

        /// <summary>Settings.json 로드가 끝난 뒤 타이머가 활성화된 상태인지.</summary>
        public bool IsEnabled => _isEnabled;

        /// <summary>Pause() 호출로 카운트가 일시 중지된 상태인지.</summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// VContainer 의존성 주입.
        /// 메시지 파이프 퍼블리셔, ZLogger 및 설정 제공자 할당.
        /// </summary>
        [Inject]
        public void Construct(IPublisher<InactivityTimeoutEvent> publisher, ILogger<InactivityTimer> logger,
            AppSettingsProvider settingsProvider)
        {
            _publisher = publisher;
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        /// <summary>
        /// 씬 전환 후에도 비활동 상태를 계속 추적할 수 있도록 파괴를 방지함.
        /// </summary>
        private void Awake()
        {
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnEnable()
        {
            InputSystem.onEvent += OnAnyInputEvent;
        }

        private void OnDisable()
        {
            InputSystem.onEvent -= OnAnyInputEvent;
        }

        private void Start()
        {
            // 주입 없이 컴포넌트만 붙인 경우 원인을 알기 어려운 NullReferenceException이 발생하므로
            // 무엇을 빠뜨렸는지 알려주고 중단함.
            if (_settingsProvider == null)
            {
                if (_logger != null)
                {
                    _logger.ZLogError($"[InactivityTimer] AppSettingsProvider was not injected. Check that RegisterComponentInHierarchy<InactivityTimer>() is registered on the LifetimeScope.");
                }
                else
                {
                    Debug.LogError("[InactivityTimer] Dependencies were not injected. Check that RegisterComponentInHierarchy<InactivityTimer>() is registered on the LifetimeScope.");
                }
                return;
            }

            LoadSettingsAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// Settings.json에서 useInactivityTimer/resetTime을 읽어와 타이머 동작 여부와 제한 시간을 결정함.
        /// </summary>
        private async UniTaskVoid LoadSettingsAsync(CancellationToken cancellationToken)
        {
            try
            {
                Settings settings = await _settingsProvider.GetAsync(cancellationToken);

                if (settings != null && settings.useInactivityTimer && settings.resetTime > 0f)
                {
                    _isEnabled = true;
                    _timeoutSeconds = settings.resetTime;

                    if (_logger != null)
                    {
                        _logger.ZLogInformation($"[InactivityTimer] Enabled. Will return to the first screen after {_timeoutSeconds}s of inactivity.");
                    }
                }
                else
                {
                    _isEnabled = false;

                    if (settings != null && settings.useInactivityTimer && _logger != null)
                    {
                        _logger.ZLogWarning($"[InactivityTimer] useInactivityTimer is true but resetTime is not positive ({settings.resetTime}). Timer disabled.");
                    }
                    else if (_logger != null)
                    {
                        _logger.ZLogInformation($"[InactivityTimer] Disabled (useInactivityTimer is false).");
                    }
                }

                ResetTimer();
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴 시 정상적으로 취소됨
            }
        }

        private void Update()
        {
            if (!_isEnabled || _hasTimedOut || _isPaused)
            {
                return;
            }

            if (Time.unscaledTime - _lastActivityTime < _timeoutSeconds)
            {
                return;
            }

            _hasTimedOut = true;

            if (_logger != null)
            {
                _logger.ZLogInformation($"[InactivityTimer] No activity for {_timeoutSeconds}s. Publishing timeout event.");
            }

            _publisher?.Publish(new InactivityTimeoutEvent());
        }

        /// <summary>
        /// 마우스/터치/키보드 등 모든 입력 장치의 이벤트를 감지하여 활동으로 취급함.
        /// UI 레이캐스트 대상 여부와 무관하게 화면 어디를 눌러도 감지되도록 Input System의
        /// 전역 이벤트를 사용함(특정 UI 위에 전체 화면 캐처를 깔면 다른 UI의 입력을 가로채는
        /// 부작용이 생기므로 피함).
        /// </summary>
        private void OnAnyInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            ResetTimer();
        }

        /// <summary>
        /// 마지막 활동 시각을 현재로 갱신하고 타임아웃 상태를 해제함.
        /// 프로젝트가 입력이 아닌 다른 활동(예: 영상 재생 중)을 활동으로 취급하고 싶을 때
        /// 외부에서 직접 호출할 수 있도록 public으로 공개함.
        /// </summary>
        public void ResetTimer()
        {
            _lastActivityTime = Time.unscaledTime;
            _hasTimedOut = false;
        }

        /// <summary>
        /// 카운트를 일시 중지함. 긴 영상 재생처럼 입력 없이도 사용자가 콘텐츠를 보고 있는
        /// 구간에서 재생 시작 시 호출해, 그 시간 동안은 타임아웃이 발동하지 않도록 함.
        /// </summary>
        public void Pause()
        {
            _isPaused = true;
        }

        /// <summary>
        /// 일시 중지를 해제함. 정지 중 흐른 실제 시간을 그대로 반영하면 재생이 끝나자마자
        /// 곧바로(또는 얼마 안 가) 타임아웃되므로, 재개 시점을 새 활동으로 취급해 ResetTimer로
        /// 카운트를 처음부터 다시 시작함.
        /// </summary>
        public void Resume()
        {
            _isPaused = false;
            ResetTimer();
        }
    }
}
