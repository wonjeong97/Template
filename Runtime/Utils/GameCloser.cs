using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using HuliacDev.Data;
using ZLogger;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HuliacDev.Utils
{
    /// <summary>
    /// 지정된 UI 요소(주로 투명 버튼)를 연속 클릭하여 앱을 강제 종료하는 유틸리티 클래스.
    /// 런타임 시 Settings.json을 읽어와 자체적으로 작동 로직(클릭 횟수, 제한 시간)과 UI(위치, 투명도)를 세팅함.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class GameCloser : MonoBehaviour
    {
        [Header("Close Settings (Overwritten by JSON)")]
        [SerializeField, Min(CloseSettingResolver.MinClickCount), Tooltip("앱을 종료하기 위해 필요한 연속 클릭 횟수")]
        private int targetClickCount = 10;

        [SerializeField, Min(CloseSettingResolver.MinClickTimeWindow), Tooltip("연속 클릭으로 인정되는 최대 대기 시간 (초, 최소 1초)")]
        private float clickTimeWindow = 3.0f;

        private int _currentClickCount;
        private float _firstClickTime;

        private ILogger<GameCloser> _logger;
        private AppSettingsProvider _settingsProvider;
        private Button _hiddenButton;

        /// <summary>
        /// VContainer 의존성 주입.
        /// ZLogger 및 설정 제공자 할당.
        /// </summary>
        [Inject]
        public void Construct(ILogger<GameCloser> logger, AppSettingsProvider settingsProvider)
        {
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        /// <summary>
        /// 인스펙터 OnClick (리플렉션) 방식을 대체하여 런타임에 이벤트를 직접 연결함.
        /// </summary>
        private void Awake()
        {
            if (TryGetComponent(out Button button))
            {
                _hiddenButton = button;
                _hiddenButton.onClick.AddListener(OnClicked);
            }
            else
            {
                if (_logger != null)
                {
                    _logger.ZLogWarning($"[GameCloser] Button component is missing on {gameObject.name}");
                }
            }
        }

        /// <summary>
        /// 시작 시 인스펙터 값을 최소값 규칙에 맞게 보정하고, 비동기로 설정 파일을 읽어와
        /// 버튼의 레이아웃과 색상, 클릭 조건을 적용함.
        /// </summary>
        private void Start()
        {
            // [Min]은 인스펙터 편집 시에만 동작하므로, 이미 저장된 값(씬 재정의 등)도 실행 시 최소값으로 맞춤.
            // 설정 로드가 실패하거나 주입이 없어도 이 값으로 동작하므로 가장 먼저 수행함.
            LogInspectorCorrections(CloseSettingResolver.ClampInspectorValues(ref targetClickCount, ref clickTimeWindow));

            // 주입 없이 컴포넌트만 붙인 경우 원인을 알기 어려운 NullReferenceException이 발생하므로
            // 무엇을 빠뜨렸는지 알려주고 중단함.
            if (_settingsProvider == null)
            {
                if (_logger != null)
                {
                    _logger.ZLogError($"[GameCloser] AppSettingsProvider was not injected. Check that RegisterComponentInHierarchy<GameCloser>() is registered on the LifetimeScope.");
                }
                else
                {
                    Debug.LogError("[GameCloser] Dependencies were not injected. Check that RegisterComponentInHierarchy<GameCloser>() is registered on the LifetimeScope.");
                }
                WarnIfLowClickCount();
                return;
            }

            ApplySettingsAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

       /// <summary>
        /// JsonLoader를 통해 프레임 드랍 없이 Settings.json을 읽어와
        /// 버튼의 작동 로직(클릭 횟수, 시간)과 UI(위치, 투명도) 중 JSON에 지정된 값만 덮어씌움.
        /// </summary>
        private async UniTaskVoid ApplySettingsAsync(CancellationToken cancellationToken)
        {
            try
            {
                Settings settings = await _settingsProvider.GetAsync(cancellationToken);

                // 해석 규칙(누락 판정, 필드별 미지정·검증)은 CloseSettingResolver가 담당하고,
                // 여기서는 경고를 남기고 결과를 컴포넌트에 적용하는 일만 함.
                if (!CloseSettingResolver.TryResolve(settings?.closeSetting, out ResolvedCloseSetting resolved))
                {
                    if (_logger != null)
                    {
                        _logger.ZLogWarning($"[GameCloser] closeSetting is missing or numToClose is not positive in settings. Using inspector defaults: Target({targetClickCount}), Window({clickTimeWindow}s)");
                    }
                    WarnIfLowClickCount();
                    return;
                }

                LogSettingIssues(resolved.Issues);
                ApplyResolvedSettings(resolved);
                WarnIfLowClickCount();
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴 시 정상적으로 취소됨
            }
        }

        /// <summary>
        /// 설정 해석 중 발견한 문제(범위 밖 값, 위치 한 성분만 지정)를 필드별 경고로 남김.
        /// 해당 필드는 적용되지 않고 인스펙터 값이 유지되므로, 원인을 로그로 알 수 있게 하기 위함.
        /// 최소값 미만이라 올려 적용한 JSON 제한 시간도 함께 알림.
        /// </summary>
        private void LogSettingIssues(CloseSettingIssues issues)
        {
            if (issues == CloseSettingIssues.None || _logger == null) return;

            if ((issues & CloseSettingIssues.InvalidResetClickTime) != 0)
            {
                _logger.ZLogWarning($"[GameCloser] closeSetting.resetClickTime must be positive. Using inspector value: Window({clickTimeWindow}s)");
            }

            if ((issues & CloseSettingIssues.ResetClickTimeBelowMinimum) != 0)
            {
                _logger.ZLogWarning($"[GameCloser] closeSetting.resetClickTime is below the minimum ({CloseSettingResolver.MinClickTimeWindow}s). Using the minimum instead.");
            }

            if ((issues & CloseSettingIssues.ImageAlphaOutOfRange) != 0)
            {
                _logger.ZLogWarning($"[GameCloser] closeSetting.imageAlpha must be between 0 and 1. Keeping current image alpha.");
            }

            if ((issues & CloseSettingIssues.PartialPosition) != 0)
            {
                _logger.ZLogWarning($"[GameCloser] closeSetting.position must specify both x and y (-1 is reserved as 'unspecified' and cannot be used as a coordinate). Keeping current position.");
            }

            if ((issues & CloseSettingIssues.PositionOutOfRange) != 0)
            {
                _logger.ZLogWarning($"[GameCloser] closeSetting.position must be between 0 and 1 (normalized). Keeping current position.");
            }
        }

        /// <summary>
        /// 실행 시 최소값으로 올린 인스펙터(직렬화) 값을 경고로 남김. [Min]은 편집할 때만 동작해
        /// 저장된 값이 조용히 바뀌면 원인을 알기 어려우므로 알림.
        /// </summary>
        private void LogInspectorCorrections(InspectorValueCorrections corrections)
        {
            if (corrections == InspectorValueCorrections.None || _logger == null) return;

            if ((corrections & InspectorValueCorrections.ClickCountRaised) != 0)
            {
                _logger.ZLogWarning($"[GameCloser] Inspector targetClickCount on {gameObject.name} was below {CloseSettingResolver.MinClickCount}. Raised to {targetClickCount}.");
            }

            if ((corrections & InspectorValueCorrections.ClickTimeWindowRaised) != 0)
            {
                _logger.ZLogWarning($"[GameCloser] Inspector clickTimeWindow on {gameObject.name} was below the minimum ({CloseSettingResolver.MinClickTimeWindow}s). Raised to {clickTimeWindow}s.");
            }
        }

        /// <summary>
        /// 최종 적용된 클릭 횟수가 권장 최소값(3회)보다 작으면 오터치 위험을 경고함. 값은 바꾸지 않음.
        /// JSON과 인스펙터 중 어느 쪽 값이 쓰였든 같은 기준으로 한 번만 알리기 위해, 값이 확정된 뒤 호출함.
        /// </summary>
        private void WarnIfLowClickCount()
        {
            if (_logger == null || !CloseSettingResolver.IsBelowRecommendedClickCount(targetClickCount)) return;

            _logger.ZLogWarning($"[GameCloser] Click count to close ({targetClickCount}) is below the recommended minimum ({CloseSettingResolver.RecommendedMinClickCount}). Visitors may close the app by accidental taps.");
        }

        /// <summary>
        /// 해석된 설정을 클릭 조건과 RectTransform·Image에 적용함.
        /// 값이 없는(미지정이거나 잘못 지정된) 필드는 바꾸지 않고 인스펙터에서 정한 값을 그대로 둠.
        /// 클릭 횟수가 0 이하인 결과(TryResolve 실패 시의 default 등)는 CloseSettingResolver.TryGetClickSettings가
        /// 거부하므로 아무것도 적용하지 않음. 0이 들어가면 숨은 버튼을 한 번만 눌러도 앱이 종료되기 때문임.
        /// </summary>
        internal void ApplyResolvedSettings(ResolvedCloseSetting resolved)
        {
            if (!CloseSettingResolver.TryGetClickSettings(resolved, clickTimeWindow, out int newTargetClickCount, out float newClickTimeWindow))
            {
                if (_logger != null) _logger.ZLogError($"[GameCloser] Resolved closeSetting has non-positive click count ({resolved.TargetClickCount}). Settings were not applied.");
                return;
            }

            targetClickCount = newTargetClickCount;
            clickTimeWindow = newClickTimeWindow;

            bool isPositionApplied = resolved.Position.HasValue && TryApplyPosition(resolved.Position.Value);
            bool isAlphaApplied = resolved.ImageAlpha.HasValue && TryApplyImageAlpha(resolved.ImageAlpha.Value);

            if (_logger != null)
            {
                _logger.ZLogInformation($"[GameCloser] Settings applied from JSON: Target({targetClickCount}), Window({clickTimeWindow}s), PositionFromJson({isPositionApplied}), AlphaFromJson({isAlphaApplied})");
            }
        }

        /// <summary>
        /// 앵커(기준점)와 피벗(중심점)을 0~1 정규화 좌표(예: 0,0 또는 1,1)로 맞춰 모서리를 지정하고,
        /// 기준점에 완전히 밀착하도록 로컬 좌표를 0으로 초기화함. RectTransform이 없으면 경고 후 false를 반환함.
        /// </summary>
        private bool TryApplyPosition(Vector2 position)
        {
            if (!TryGetComponent(out RectTransform rt))
            {
                if (_logger != null) _logger.ZLogWarning($"[GameCloser] RectTransform is missing on {gameObject.name}. closeSetting.position was not applied.");
                return false;
            }

            rt.anchorMin = position;
            rt.anchorMax = position;
            rt.pivot = position;
            rt.anchoredPosition = Vector2.zero;
            return true;
        }

        /// <summary>
        /// 버튼 Image의 투명도만 바꾸고 색상은 유지함. Image가 없으면 경고 후 false를 반환함.
        /// </summary>
        private bool TryApplyImageAlpha(float alpha)
        {
            if (!TryGetComponent(out Image img))
            {
                if (_logger != null) _logger.ZLogWarning($"[GameCloser] Image is missing on {gameObject.name}. closeSetting.imageAlpha was not applied.");
                return false;
            }

            Color c = img.color;
            c.a = alpha;
            img.color = c;
            return true;
        }

        /// <summary>
        /// 메모리 누수 방지를 위해 객체 파괴 시 이벤트 구독을 해제함.
        /// </summary>
        private void OnDestroy()
        {
            if (_hiddenButton)
            {
                _hiddenButton.onClick.RemoveListener(OnClicked);
            }
        }

        /// <summary>
        /// 버튼 클릭 시 호출되는 내부 로직.
        /// 첫 클릭 시간을 기준으로 제한 시간 내에 타겟 횟수 도달 시 앱을 종료함.
        /// </summary>
        private void OnClicked()
        {
            float currentTime = Time.unscaledTime;

            // 완전 첫 클릭일 때 시작 시간을 기록함
            if (_currentClickCount == 0)
            {
                _firstClickTime = currentTime;
            }

            // 첫 클릭으로부터 지정된 총 제한 시간(Window)이 지났다면 
            // 카운트를 초기화하고 '방금 누른 클릭'을 새로운 1회차로 취급함
            if (currentTime - _firstClickTime > clickTimeWindow)
            {
                _currentClickCount = 1;
                _firstClickTime = currentTime;
            }
            else
            {
                // 제한 시간 내에 클릭했다면 카운트 증가
                _currentClickCount++;
            }

            if (_logger != null)
            {
                _logger.ZLogDebug($"[GameCloser] Clicked: {_currentClickCount} / {targetClickCount} (Window: {currentTime - _firstClickTime:F1}s)");
            }

            if (_currentClickCount >= targetClickCount)
            {
                // 성공 시 다음번 안전을 위해 카운트 초기화 후 종료 호출
                _currentClickCount = 0;
                QuitApplication();
            }
        }

        /// <summary>
        /// 플랫폼 환경(에디터 및 빌드)에 맞춰 안전하게 종료 명령을 호출함.
        /// 파생 클래스는 종료 방식(예: 종료 전 확인 화면)을 바꾸기 위해 재정의할 수 있음.
        /// </summary>
        protected virtual void QuitApplication()
        {
            // ApiManagerBase가 종료 로그 메시지에 "누가 종료시켰는지" 반영할 수 있도록,
            // Application.Quit()을 부르기 직전에 남겨둠.
            QuitReason.Set(QuitReason.GameCloser);

            if (_logger != null)
            {
                _logger.ZLogInformation($"[GameCloser] Target click count reached. Quitting application...");
            }

#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
            // 브라우저 환경에서는 Application.Quit()이 동작하지 않으므로 로그만 남김.
            if (_logger != null)
            {
                _logger.ZLogInformation($"[GameCloser] Application.Quit() is not supported on WebGL. Close the browser tab instead.");
            }
#else
            Application.Quit();
#endif
        }
    }
}