using System;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.Events;
using VContainer;
using Wonjeong.Data;
using Wonjeong.Utils;
using ZLogger;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Wonjeong.Core
{
    /// <summary>
    /// StreamingAssets의 ShutdownSettings.json을 읽어, 예정된 시각이 되면 Windows shutdown
    /// 명령으로 PC 종료를 예약하고 앱을 정상 종료시키는 예약 종료 매니저.
    /// <para>
    /// 종료 로그는 이 클래스가 직접 보내지 않고 ApiManagerBase의 종료 로그 경로가 담당함.
    /// 예약 종료든 사용자의 수동 종료든 앱이 종료되는 경로는 하나로 모으는 편이, 예약 종료
    /// 때만 로그가 중복 기록되는 문제를 피할 수 있기 때문.
    /// </para>
    /// <para>
    /// 앱이 멈춰 이 매니저가 동작하지 못하는 경우를 대비한 백업은 Tools~/ShutdownScheduleEditor로
    /// 작업 스케줄러에 등록함(예정 시각 +N분).
    /// </para>
    /// </summary>
    public class ShutdownScheduler : MonoBehaviour
    {
        private const string ShutdownSettingsFileName = "ShutdownSettings.json";

        /// <summary>편집 도구가 파일을 만들지 못했을 때를 대비한 종료 인수 기본값.</summary>
        private const string DefaultShutdownArguments = "-s -f -t 45";

        [SerializeField, Tooltip("예정 시각 도달 여부를 확인하는 주기(초).")]
        private float checkIntervalSeconds = 15f;

        [SerializeField, Tooltip("종료 직전에 실행할 동작. 페이드아웃이나 저장 등 프로젝트별 마무리를 연결함(런타임 생성 시에는 OnBeforeShutdown에 코드로 연결).")]
        private UnityEvent onBeforeShutdown = new UnityEvent();

        private ShutdownSetting _schedule;
        private bool _isShuttingDown;

        // 하루에 한 번만 종료가 발동하도록, 이미 처리한 날짜를 기록함.
        private DateTime _handledDate = DateTime.MinValue;

        private ILogger<ShutdownScheduler> _logger;

        /// <summary>
        /// 인스펙터에서 연결할 수 없는 런타임 코드에서도 구독할 수 있도록 노출한 프로퍼티.
        /// </summary>
        public UnityEvent OnBeforeShutdown => onBeforeShutdown;

        /// <summary>
        /// VContainer 의존성 주입.
        /// ZLogger 할당.
        /// </summary>
        [Inject]
        public void Construct(ILogger<ShutdownScheduler> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 씬 전환 후에도 예약 종료 감시가 끊기지 않도록 파괴를 방지함.
        /// </summary>
        private void Awake()
        {
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        /// <summary>
        /// 스케줄 파일을 읽어 감시 루프를 시작함.
        /// </summary>
        private void Start()
        {
            // 이 매니저는 로거 외에 주입받는 의존성이 없어 주입이 없어도 동작 자체는 가능하지만,
            // 그 경우 진단 로그가 전부 사라져 원인 파악이 어려우므로 폴백으로 알려둠.
            if (_logger == null)
            {
                Debug.LogWarning("[ShutdownScheduler] ILogger was not injected; shutdown scheduling will run without logs. Check that RegisterComponentInHierarchy<ShutdownScheduler>() is registered on the LifetimeScope.");
            }

            RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// 스케줄을 로드한 뒤, 예정 시각에 도달했는지 주기적으로 확인하는 감시 루프를 실행함.
        /// </summary>
        private async UniTaskVoid RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                _schedule = await JsonLoader.LoadAsync<ShutdownSetting>(ShutdownSettingsFileName, cancellationToken);

                if (!HasAnySchedule())
                {
                    if (_logger != null)
                    {
                        _logger.ZLogInformation($"[ShutdownScheduler] No shutdown schedule found in {ShutdownSettingsFileName}; scheduler is idle.");
                    }
                    return;
                }

                SkipTodayIfAlreadyPast(DateTime.Now);

                if (_logger != null)
                {
                    _logger.ZLogInformation($"[ShutdownScheduler] Started. Checking every {GetCheckInterval()}s.");
                }

                await WatchLoopAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴 시 정상적으로 취소됨
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogError($"[ShutdownScheduler] Watch loop stopped by an exception: {e.Message}");
            }
        }

        /// <summary>
        /// 실제로 사용할 확인 주기를 반환함. 인스펙터에서 0이나 음수로 설정해도 루프가
        /// 프레임마다 도는 폭주가 되지 않도록 하한을 둠.
        /// </summary>
        private float GetCheckInterval()
        {
            return Mathf.Max(1f, checkIntervalSeconds);
        }

        /// <summary>
        /// 예정 시각 도달 여부를 주기적으로 확인함.
        /// </summary>
        private async UniTask WatchLoopAsync(CancellationToken cancellationToken)
        {
            float interval = GetCheckInterval();

            while (!cancellationToken.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(interval), DelayType.UnscaledDeltaTime, cancellationToken: cancellationToken);

                if (_isShuttingDown)
                {
                    continue;
                }

                DateTime now = DateTime.Now;

                if (_handledDate.Date == now.Date)
                {
                    continue;
                }

                if (!TryGetScheduleFor(now, out DateTime scheduledMoment, out string source))
                {
                    continue;
                }

                if (now < scheduledMoment)
                {
                    continue;
                }

                _handledDate = now.Date;
                _isShuttingDown = true;

                if (_logger != null)
                {
                    _logger.ZLogInformation($"[ShutdownScheduler] Scheduled shutdown reached ({source}, {scheduledMoment:yyyy-MM-dd HH:mm}). Starting shutdown sequence.");
                }

                ShutdownSequence();
                return;
            }
        }

        /// <summary>
        /// 앱을 예정 시각 이후에 실행한 경우(운영자가 밤에 재시작한 상황 등) 곧바로 종료되지
        /// 않도록, 오늘은 이미 처리한 것으로 표시함.
        /// </summary>
        private void SkipTodayIfAlreadyPast(DateTime now)
        {
            if (!TryGetScheduleFor(now, out DateTime scheduledMoment, out string source))
            {
                return;
            }

            if (now < scheduledMoment)
            {
                if (_logger != null)
                {
                    _logger.ZLogInformation($"[ShutdownScheduler] Today's shutdown is scheduled at {scheduledMoment:HH:mm} ({source}).");
                }
                return;
            }

            _handledDate = now.Date;

            if (_logger != null)
            {
                _logger.ZLogInformation($"[ShutdownScheduler] Started after today's scheduled time ({scheduledMoment:HH:mm}, {source}); skipping today to avoid shutting down right after launch.");
            }
        }

        /// <summary>
        /// 마무리 동작을 실행하고 OS 종료를 예약한 뒤, 앱을 정상 종료시킴.
        /// <para>
        /// 종료 로그는 여기서 직접 보내지 않고 ApiManagerBase의 종료 로그 경로에 맡김.
        /// 양쪽에서 보내면 예약 종료 때만 로그가 두 번 기록되기 때문. shutdown 명령이 지연
        /// 시간(-t)을 두고 OS 종료를 예약하므로, 그 사이에 Application.Quit()으로 정상 종료
        /// 절차를 태우면 ApiManagerBase가 로그를 끝까지 보낼 시간이 확보됨.
        /// </para>
        /// </summary>
        private void ShutdownSequence()
        {
            onBeforeShutdown.Invoke();

            string arguments = string.IsNullOrWhiteSpace(_schedule.shutdownArguments)
                ? DefaultShutdownArguments
                : _schedule.shutdownArguments;

            ExecuteShutdownCommand(arguments);

            QuitApplication();
        }

        /// <summary>
        /// 플랫폼 환경(에디터 및 빌드)에 맞춰 앱을 종료함.
        /// 에디터에서는 Application.Quit()이 무시되어 플레이 모드가 멈추지 않고,
        /// 그 결과 ApiManagerBase의 wantsToQuit 종료 로그 흐름까지 검증할 수 없으므로 분기함.
        /// </summary>
        private void QuitApplication()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Windows shutdown 명령을 실행함. 에디터와 Windows 이외의 플랫폼에서는 실제로
        /// 종료하지 않고 무엇을 실행했을지만 남김.
        /// </summary>
        private void ExecuteShutdownCommand(string arguments)
        {
#if UNITY_EDITOR || !UNITY_STANDALONE_WIN
            if (_logger != null)
            {
                _logger.ZLogInformation($"[ShutdownScheduler] Editor or non-Windows platform; skipping actual shutdown (would run: shutdown {arguments}).");
            }
#else
            try
            {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                System.Diagnostics.Process.Start(startInfo);

                if (_logger != null) _logger.ZLogInformation($"[ShutdownScheduler] Shutdown command executed: shutdown {arguments}");
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogError($"[ShutdownScheduler] Failed to execute shutdown command (shutdown {arguments}): {e.Message}");
            }
#endif
        }

        /// <summary>
        /// 스케줄 파일에 종료 예정이 하나라도 설정되어 있는지 확인함.
        /// </summary>
        private bool HasAnySchedule()
        {
            if (_schedule == null)
            {
                return false;
            }

            for (int i = 0; i < 7; i++)
            {
                ShutdownDaySchedule day = GetDaySchedule((DayOfWeek)i);
                if (day != null && day.enabled)
                {
                    return true;
                }
            }

            if (_schedule.dateOverrides != null)
            {
                foreach (ShutdownDateOverride dateOverride in _schedule.dateOverrides)
                {
                    if (dateOverride != null && dateOverride.enabled)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 주어진 시점의 유효한 종료 예정 시각을 구함. 특정 날짜 설정이 있으면 그 요일의
        /// 기본 스케줄보다 우선 적용하며, 종료하지 않는 날이면 false를 반환함.
        /// </summary>
        private bool TryGetScheduleFor(DateTime now, out DateTime scheduledMoment, out string source)
        {
            scheduledMoment = default;
            source = string.Empty;

            if (_schedule == null)
            {
                return false;
            }

            ShutdownDateOverride dateOverride = FindDateOverride(now);

            if (dateOverride != null)
            {
                source = ZString.Concat("date ", dateOverride.date);

                if (!dateOverride.enabled)
                {
                    return false;
                }

                return TryComposeMoment(now, dateOverride.time, source, out scheduledMoment);
            }

            ShutdownDaySchedule daySchedule = GetDaySchedule(now.DayOfWeek);

            if (daySchedule == null || !daySchedule.enabled)
            {
                return false;
            }

            source = ZString.Concat("weekday ", now.DayOfWeek.ToString());

            return TryComposeMoment(now, daySchedule.time, source, out scheduledMoment);
        }

        /// <summary>
        /// "HH:mm" 문자열을 해당 날짜의 실제 시각으로 변환함.
        /// </summary>
        private bool TryComposeMoment(DateTime now, string time, string source, out DateTime scheduledMoment)
        {
            scheduledMoment = default;

            if (!DateTime.TryParseExact(time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime parsed))
            {
                if (_logger != null)
                {
                    _logger.ZLogError($"[ShutdownScheduler] Invalid time format for {source}: '{time}'. Expected HH:mm.");
                }
                return false;
            }

            scheduledMoment = now.Date.Add(parsed.TimeOfDay);
            return true;
        }

        /// <summary>
        /// 오늘 날짜에 해당하는 특정 날짜 설정을 찾음. 없으면 null을 반환함.
        /// </summary>
        private ShutdownDateOverride FindDateOverride(DateTime now)
        {
            if (_schedule.dateOverrides == null)
            {
                return null;
            }

            string today = now.ToString("yyyy-MM-dd");

            foreach (ShutdownDateOverride dateOverride in _schedule.dateOverrides)
            {
                if (dateOverride != null && dateOverride.date == today)
                {
                    return dateOverride;
                }
            }

            return null;
        }

        /// <summary>
        /// 요일에 해당하는 기본 스케줄을 반환함.
        /// </summary>
        private ShutdownDaySchedule GetDaySchedule(DayOfWeek dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case DayOfWeek.Monday: return _schedule.monday;
                case DayOfWeek.Tuesday: return _schedule.tuesday;
                case DayOfWeek.Wednesday: return _schedule.wednesday;
                case DayOfWeek.Thursday: return _schedule.thursday;
                case DayOfWeek.Friday: return _schedule.friday;
                case DayOfWeek.Saturday: return _schedule.saturday;
                case DayOfWeek.Sunday: return _schedule.sunday;
                default: return null;
            }
        }
    }
}
