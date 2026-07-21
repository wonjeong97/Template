using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Wonjeong.Data;
using ZLogger;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Wonjeong.Utils
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
                    _logger.ZLogError($"[GameCloser] AppSettingsProvider가 주입되지 않았습니다. LifetimeScope에 RegisterComponentInHierarchy<GameCloser>()를 등록했는지 확인하세요.");
                }
                else
                {
                    Debug.LogError("[GameCloser] 의존성이 주입되지 않았습니다. LifetimeScope에 RegisterComponentInHierarchy<GameCloser>()를 등록했는지 확인하세요.");
                }
                return;
            }

            ApplySettingsAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

       /// <summary>
        /// JsonLoader를 통해 프레임 드랍 없이 Settings.json을 읽어와
        /// 버튼의 작동 로직(클릭 횟수, 시간)과 UI(위치, 투명도)를 동적으로 덮어씌움.
        /// </summary>
        private async UniTaskVoid ApplySettingsAsync(CancellationToken cancellationToken)
        {
            try
            {
                Settings settings = await _settingsProvider.GetAsync(cancellationToken);

                if (settings != null && settings.closeSetting != null) 
                {
                    // 1. 작동 로직 동기화
                    targetClickCount = settings.closeSetting.numToClose;
                    clickTimeWindow = settings.closeSetting.resetClickTime;

                    // 2. UI 위치 동기화 (0~1 비율의 정규화 좌표 적용)
                    if (TryGetComponent(out RectTransform rt))
                    {
                        Vector2 normalizedPos = settings.closeSetting.position;
                        
                        // 앵커(기준점)와 피벗(중심점)을 세팅값(예: 0,0 또는 1,1)으로 맞춰서 모서리를 지정함
                        rt.anchorMin = normalizedPos;
                        rt.anchorMax = normalizedPos;
                        rt.pivot = normalizedPos;
                        
                        // 기준점에 완전히 밀착하도록 로컬 좌표를 0으로 초기화
                        rt.anchoredPosition = Vector2.zero;
                    }

                    // 3. UI 투명도 동기화
                    if (TryGetComponent(out Image img))
                    {
                        Color c = img.color;
                        c.a = settings.closeSetting.imageAlpha;
                        img.color = c;
                    }

                    if (_logger != null)
                    {
                        _logger.ZLogInformation($"[GameCloser] Settings applied from JSON: Pos({settings.closeSetting.position}), Alpha({settings.closeSetting.imageAlpha}), Target({targetClickCount}), Window({clickTimeWindow}s)");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴 시 정상적으로 취소됨
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