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
        [SerializeField, Tooltip("앱을 종료하기 위해 필요한 연속 클릭 횟수")]
        private int targetClickCount = 10;

        [SerializeField, Tooltip("연속 클릭으로 인정되는 최대 대기 시간 (초)")]
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
        /// 시작 시 비동기로 설정 파일을 읽어와 버튼의 레이아웃과 색상, 클릭 조건을 적용함.
        /// </summary>
        private void Start()
        {
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

                // 병합 규칙(누락 판정, 필드별 미지정 처리)은 CloseSettingResolver가 담당하고,
                // 여기서는 그 결과를 컴포넌트에 적용하는 일만 함.
                if (!CloseSettingResolver.TryResolve(settings?.closeSetting, targetClickCount, clickTimeWindow,
                        out ResolvedCloseSetting resolved))
                {
                    if (_logger != null)
                    {
                        _logger.ZLogWarning($"[GameCloser] closeSetting is missing or numToClose is not positive in settings. Using inspector defaults: Target({targetClickCount}), Window({clickTimeWindow}s)");
                    }
                    return;
                }

                ApplyResolvedSettings(resolved);
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴 시 정상적으로 취소됨
            }
        }

        /// <summary>
        /// 병합된 설정을 클릭 조건과 RectTransform·Image에 적용함.
        /// 위치·투명도는 JSON에서 지정된 경우에만 바꾸고, 미지정이면 인스펙터에서 정한 값을 그대로 둠.
        /// </summary>
        private void ApplyResolvedSettings(ResolvedCloseSetting resolved)
        {
            targetClickCount = resolved.TargetClickCount;
            clickTimeWindow = resolved.ClickTimeWindow;

            // 앵커(기준점)와 피벗(중심점)을 세팅값(0~1 정규화 좌표, 예: 0,0 또는 1,1)으로 맞춰서 모서리를 지정하고,
            // 기준점에 완전히 밀착하도록 로컬 좌표를 0으로 초기화함.
            if (resolved.HasPosition && TryGetComponent(out RectTransform rt))
            {
                rt.anchorMin = resolved.Position;
                rt.anchorMax = resolved.Position;
                rt.pivot = resolved.Position;
                rt.anchoredPosition = Vector2.zero;
            }

            if (resolved.HasImageAlpha && TryGetComponent(out Image img))
            {
                Color c = img.color;
                c.a = resolved.ImageAlpha;
                img.color = c;
            }

            if (_logger != null)
            {
                _logger.ZLogInformation($"[GameCloser] Settings applied from JSON: Pos({(resolved.HasPosition ? resolved.Position.ToString() : "inspector")}), Alpha({(resolved.HasImageAlpha ? resolved.ImageAlpha.ToString() : "inspector")}), Target({targetClickCount}), Window({clickTimeWindow}s)");
            }
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
        /// </summary>
        private void QuitApplication()
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