using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer;
using HuliacDev.App;
using HuliacDev.Data;
using HuliacDev.Utils;
using ZLogger;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HuliacDev.Network
{
    /// <summary>
    /// 프로그램 시작·종료·idle 화면 진입 로그를 서버에 전송하는 매니저의 기반 클래스.
    /// 콘텐츠마다 호출해야 하는 API가 다를 수 있으므로, 프로젝트별 클래스가 이 클래스를
    /// 상속해 Start()를 override하고 <see cref="ApiRetryUtil.SendGetRequestWithRetryAsync"/>를
    /// 재사용하면 여기서 다루는 로그 외의 다른 API 호출에도 동일한 재시도/네트워크 확인/
    /// 에디터·디벨롭 빌드 스킵 정책을 그대로 적용할 수 있음(GameManagerBase&lt;T&gt;와 동일하게,
    /// abstract이므로 씬에는 이 클래스를 상속한 프로젝트 전용 클래스를 배치할 것).
    /// <para>
    /// Settings.json의 apiUrl은 idx_content_device, uid 등 콘텐츠별 쿼리 파라미터가
    /// 이미 포함된 형태(message= 까지)로 서버에서 발급되므로, 각 로그는 여기에 상태
    /// 메시지 값만 이어붙여 GET 요청을 보냄. 메시지 규칙: 시작 "start"/"start (restart)",
    /// 종료 "end (by User/GameCloser/Shutdown Scheduler)", idle 진입 "move_idle"/
    /// "move_idle_timeout", 외부 API 호출 "Call API {url}", 외부 API 응답 "Return OK"/
    /// "Return fail"(실패 사유를 알 수 있으면 "Return fail: {reason}"). 유니티가 멈춰
    /// 작업 스케줄러가 대신 끈 경우("end_kill (by Task
    /// Scheduler)")는 이 클래스가 아니라 Tools~/ShutdownScheduleEditor의 가드 스크립트가 별도로 보냄.
    /// </para>
    /// </summary>
    public abstract class ApiManagerBase : MonoBehaviour
    {
        /// <summary>
        /// 오늘 시작 로그를 이미 성공적으로 전송했는지 판별하기 위해 마지막 전송 날짜를
        /// 기기에 저장해두는 PlayerPrefs 키. 같은 날 재실행되면 "재시작"으로 구분함.
        /// </summary>
        private const string LastStartupLogDateKey = "ApiManagerBase_LastStartupLogDate";

        /// <summary>
        /// 정상 종료 시 서버에 보낼 상태 메시지. 실제 전송 시에는 QuitReason.Current를 덧붙여
        /// "end (by User/GameCloser/Shutdown Scheduler)" 형태로 나감.
        /// </summary>
        private const string ExitLogMessage = "end";

        private const string MoveIdleMessage = "move_idle";
        private const string MoveIdleTimeoutMessage = "move_idle_timeout";
        private const string ExternalApiCallPrefix = "Call API ";
        private const string ExternalApiReturnOkMessage = "Return OK";
        private const string ExternalApiReturnFailMessage = "Return fail";

        /// <summary>
        /// 예외 메시지를 실패 사유로 자동 전송하기 전에 URL 쿼리 스트링(?뒤)을 제거하기 위한 패턴.
        /// 실패한 요청의 URL이 예외 메시지에 그대로 포함되는 경우가 있는데, 쿼리 스트링에는 API 키·
        /// 토큰 등이 담기는 경우가 많아 그대로 사내 로그 서버로 전송하면 유출될 수 있음(SanitizeFailReason 참고).
        /// </summary>
        private static readonly Regex FailReasonQueryStringPattern = new Regex(@"\?[^\s""')]*", RegexOptions.Compiled);

        private bool _isOriginal;

        // 종료 요청을 한 번 보류하고 로그를 보낸 뒤 다시 종료를 진행하기 위한 상태.
        private bool _isQuitConfirmed;
        private bool _isSendingExitLog;

        private ISubscriber<InactivityTimeoutEvent> _inactivityTimeoutSubscriber;
        private IDisposable _inactivityTimeoutSubscription;

        private ISubscriber<MoveIdleEvent> _moveIdleSubscriber;
        private IDisposable _moveIdleSubscription;

        private ISubscriber<ExternalApiCallEvent> _externalApiCallSubscriber;
        private IDisposable _externalApiCallSubscription;

        private ISubscriber<ExternalApiReturnEvent> _externalApiReturnSubscriber;
        private IDisposable _externalApiReturnSubscription;

        protected ILogger<ApiManagerBase> Logger { get; private set; }
        protected AppSettingsProvider SettingsProvider { get; private set; }

        /// <summary>
        /// 종료 로그의 최대 시도 횟수. 시작 로그와 달리 사용자가 종료를 기다리는 상황이고,
        /// OS가 앱 종료를 기다려주는 시간(WaitToKillAppTimeout)도 제한적이므로 짧게 잡음.
        /// </summary>
        protected virtual int ExitLogMaxAttemptCount => 3;

        /// <summary>종료 로그 재시도 사이의 대기 시간(초).</summary>
        protected virtual float ExitLogRetryDelaySeconds => 1f;

        /// <summary>
        /// 종료 로그 전송 전체에 허용하는 최대 시간(초). 이 시간을 넘기면 전송을 포기하고
        /// 종료를 진행함. 로그 때문에 종료가 무한정 막히는 것을 막기 위함.
        /// </summary>
        protected virtual float ExitLogTimeoutSeconds => 5f;

        /// <summary>
        /// VContainer 의존성 주입. 로거, 설정 제공자 및 이벤트 구독자를 할당함.
        /// </summary>
        [Inject]
        public void Construct(ILogger<ApiManagerBase> logger, AppSettingsProvider settingsProvider,
            ISubscriber<InactivityTimeoutEvent> inactivityTimeoutSubscriber, ISubscriber<MoveIdleEvent> moveIdleSubscriber,
            IObjectResolver resolver)
        {
            Logger = logger;
            SettingsProvider = settingsProvider;
            _inactivityTimeoutSubscriber = inactivityTimeoutSubscriber;
            _moveIdleSubscriber = moveIdleSubscriber;

            // 외부 API 이벤트 브로커는 ConfigureMessagePipe를 override한 프로젝트에서 빠질 수 있는
            // 선택적 의존성이므로 ResolveOrDefault로 조회한다. 매개변수 기본값(= null)은 이 VContainer
            // 버전이 주입 시 참조하지 않아, 미등록 시 null이 들어오는 대신 해석 예외가 난다.
            _externalApiCallSubscriber = resolver.ResolveOrDefault<ISubscriber<ExternalApiCallEvent>>();
            _externalApiReturnSubscriber = resolver.ResolveOrDefault<ISubscriber<ExternalApiReturnEvent>>();
        }

        /// <summary>
        /// 다른 선택 매니저(FadeManager/SoundManager/UIManager/VideoManager)와 동일하게,
        /// 씬 전환으로 재생성되어 시작 로그가 중복 전송되지 않도록 파괴를 방지함.
        /// 중복 생성 시 기존 인스턴스를 유지하고 새로 생성된 객체를 파괴함.
        /// <para>
        /// 파생 클래스에서 override할 경우 반드시 base.Awake()를 호출할 것.
        /// 빠뜨리면 중복 생성 방어 로직이 누락되어 시작 로그가 중복 전송될 수 있음.
        /// </para>
        /// </summary>
        protected virtual void Awake()
        {
            if (SingletonGuard<ApiManagerBase>.CheckDuplicate(this, out _isOriginal))
            {
                return;
            }
        }

        /// <summary>
        /// 종료 로그 전송을 위해 종료 요청을 가로챌 수 있도록 이벤트를 구독함.
        /// InactivityTimer의 타임아웃 이벤트, 그리고 프로젝트 코드가 발행하는 MoveIdleEvent,
        /// 외부 API 호출/반환 이벤트도 함께 구독해 관련 로그를 자동으로 보냄.
        /// 파생 클래스에서 override할 경우 반드시 base.OnEnable()을 호출할 것.
        /// 빠뜨리면 구독이 누락되어 종료/idle/외부 API 로그가 전송되지 않음.
        /// </summary>
        protected virtual void OnEnable()
        {
            if (!_isOriginal) return;

            Application.wantsToQuit += OnWantsToQuit;
            _inactivityTimeoutSubscription = _inactivityTimeoutSubscriber?.Subscribe(_ => OnInactivityTimeout());
            _moveIdleSubscription = _moveIdleSubscriber?.Subscribe(_ => OnMoveIdle());
            _externalApiCallSubscription = _externalApiCallSubscriber?.Subscribe(e => OnExternalApiCall(e.RequestUrl));
            _externalApiReturnSubscription = _externalApiReturnSubscriber?.Subscribe(e => OnExternalApiReturn(e.IsSuccess, e.FailReason));
        }

        /// <summary>
        /// 구독을 해제함. 파생 클래스에서 override할 경우 반드시 base.OnDisable()을 호출할 것.
        /// </summary>
        protected virtual void OnDisable()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            _inactivityTimeoutSubscription?.Dispose();
            _inactivityTimeoutSubscription = null;
            _moveIdleSubscription?.Dispose();
            _moveIdleSubscription = null;
            _externalApiCallSubscription?.Dispose();
            _externalApiCallSubscription = null;
            _externalApiReturnSubscription?.Dispose();
            _externalApiReturnSubscription = null;
        }

        /// <summary>
        /// 종료 요청을 한 번만 보류시키고 종료 로그를 보낸 뒤 다시 종료를 진행함.
        /// OnApplicationQuit에서 전송을 시작하면 응답을 받기 전에 프로세스가 사라져 로그가
        /// 유실되므로, 종료 자체를 잠깐 미룰 수 있는 wantsToQuit을 사용함.
        /// </summary>
        private bool OnWantsToQuit()
        {
            if (_isQuitConfirmed)
            {
                return true;
            }

            // 전송 중에 종료 요청이 또 들어와도 중복으로 시작하지 않음.
            if (!_isSendingExitLog)
            {
                _isSendingExitLog = true;
                SendExitLogThenQuitAsync().Forget();
            }

            return false;
        }

        /// <summary>
        /// 종료 로그 전송이 끝나면(성공·실패·시간 초과 무관) 종료를 다시 진행함.
        /// </summary>
        private async UniTaskVoid SendExitLogThenQuitAsync()
        {
            // 전송이 동기적으로 즉시 끝나는 경우(에디터·네트워크 미연결 등) 종료 재개가
            // OnWantsToQuit이 false를 반환하기도 전에 실행될 수 있으므로, 한 프레임 양보해
            // 종료 보류가 확정된 뒤에 진행함.
            await UniTask.Yield();

            try
            {
                await SendExitLogAsync();
            }
            catch (Exception e)
            {
                if (Logger != null) Logger.ZLogError($"[ApiManagerBase] Exception while sending exit log: {e.Message}");
            }
            finally
            {
                // 로그 전송 결과와 무관하게 종료는 반드시 진행되어야 함.
                _isQuitConfirmed = true;
                QuitApplication();
            }
        }

        /// <summary>
        /// 보류시켰던 종료를 플랫폼 환경(에디터 및 빌드)에 맞춰 재개함.
        /// 에디터의 플레이 모드 종료는 Application.Quit()으로 재개되지 않으므로 분기함.
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
        /// Settings.json의 apiUrl로 종료 상태 메시지를 전송함. apiUrl이 비어 있으면 생략하며,
        /// 네트워크 미연결 시 즉시 포기하는 정책은 시작 로그와 동일함.
        /// </summary>
        protected virtual async UniTask SendExitLogAsync()
        {
            if (SettingsProvider == null)
            {
                return;
            }

            // 종료가 로그 때문에 무한정 막히지 않도록 전체 전송에 시간 상한을 둠.
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(ExitLogTimeoutSeconds));

            try
            {
                Settings settings = await SettingsProvider.GetAsync(cts.Token);

                if (settings == null || string.IsNullOrEmpty(settings.apiUrl))
                {
                    if (Logger != null) Logger.ZLogInformation($"[ApiManagerBase] apiUrl is not set; skipping exit log.");
                    return;
                }

                // 누가 종료시켰는지(사용자의 Alt+F4/창 닫기, GameCloser, ShutdownScheduler)
                // 메시지에 남겨서 서버 로그만 보고도 원인을 구분할 수 있게 함. 작업 스케줄러
                // 백업(유니티가 멈춰 강제로 꺼진 경우)은 이 경로를 타지 않고 가드 스크립트가
                // "end_kill (by Task Scheduler)"를 직접 보냄(TaskSchedulerIntegration.cs 참고).
                string message = $"{ExitLogMessage} (by {QuitReason.Current})";
                string url = settings.apiUrl + Uri.EscapeDataString(message);

                await ApiRetryUtil.SendGetRequestWithRetryAsync(
                    url,
                    $"exit log ({message})",
                    Logger,
                    cts.Token,
                    ExitLogMaxAttemptCount,
                    ExitLogRetryDelaySeconds);
            }
            catch (OperationCanceledException)
            {
                if (Logger != null)
                {
                    Logger.ZLogWarning($"[ApiManagerBase] Exit log timed out after {ExitLogTimeoutSeconds}s; quitting anyway.");
                }
            }
        }

        /// <summary>
        /// 파생 클래스에서 다른 API 호출을 추가하려면 override 후 base.Start()를 호출할 것.
        /// </summary>
        protected virtual void Start()
        {
            if (!_isOriginal) return;

            SendStartupLogAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// 앱 시작 로그를 서버로 전송함.
        /// </summary>
        protected virtual async UniTaskVoid SendStartupLogAsync(CancellationToken cancellationToken)
        {
            // 주입 없이 컴포넌트만 붙인 경우 원인을 알기 어려운 NullReferenceException이 발생하므로
            // 무엇을 빠뜨렸는지 알려주고 중단함.
            if (SettingsProvider == null)
            {
                if (Logger != null)
                {
                    Logger.ZLogError($"[ApiManagerBase] AppSettingsProvider was not injected. Check that RegisterComponentInHierarchy<ApiManagerBase>() is registered on the LifetimeScope.");
                }
                else
                {
                    Debug.LogError("[ApiManagerBase] Dependencies were not injected. Check that RegisterComponentInHierarchy<ApiManagerBase>() is registered on the LifetimeScope.");
                }
                return;
            }

            try
            {
                Settings settings = await SettingsProvider.GetAsync(cancellationToken);

                if (settings == null || string.IsNullOrEmpty(settings.apiUrl))
                {
                    if (Logger != null) Logger.ZLogInformation($"[ApiManagerBase] apiUrl is not set; skipping startup log.");
                    return;
                }

                string today = DateTime.Now.ToString("yyyy-MM-dd");
                bool alreadyLoggedToday = PlayerPrefs.GetString(LastStartupLogDateKey, string.Empty) == today;
                string message = alreadyLoggedToday ? "start (restart)" : "start";
                string url = settings.apiUrl + Uri.EscapeDataString(message);

                bool success = await ApiRetryUtil.SendGetRequestWithRetryAsync(url, $"startup log ({message})", Logger, cancellationToken);

                if (success)
                {
                    // 같은 날 재실행 시 "재시작"으로 구분되도록, 전송이 실제로 성공했을 때만 날짜를 갱신함.
                    PlayerPrefs.SetString(LastStartupLogDateKey, today);
                    PlayerPrefs.Save();
                }
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴로 인한 정상적인 취소
            }
            catch (Exception e)
            {
                if (Logger != null) Logger.ZLogError($"[ApiManagerBase] Exception while sending startup log: {e.Message}");
            }
        }

        /// <summary>
        /// InactivityTimer 타임아웃 이벤트(<see cref="InactivityTimeoutEvent"/>) 수신 시 호출되는 가상 핸들러.
        /// 기본 동작은 <see cref="SendMoveIdleTimeoutLogAsync"/>를 호출하여 move_idle_timeout 로그를 전송함.
        /// 아웃트로(마지막 씬)처럼 타임아웃이 발생해도 중도 이탈이 아닌 정상 관람 완료(move_idle)로 집계되어야
        /// 하는 등 씬별/조건별 분기가 필요한 경우 파생 클래스에서 override할 것.
        /// </summary>
        protected virtual void OnInactivityTimeout()
        {
            SendMoveIdleTimeoutLogAsync().Forget();
        }

        /// <summary>
        /// 대기 화면 복귀 이벤트(<see cref="MoveIdleEvent"/>) 수신 시 호출되는 가상 핸들러.
        /// 기본 동작은 <see cref="SendMoveIdleLogAsync"/>를 호출하여 move_idle 로그를 전송함.
        /// 조건에 따라 로그 전송 방식을 커스텀해야 하는 경우 파생 클래스에서 override할 것.
        /// </summary>
        protected virtual void OnMoveIdle()
        {
            SendMoveIdleLogAsync().Forget();
        }

        /// <summary>
        /// 외부 API 호출 이벤트(<see cref="ExternalApiCallEvent"/>) 수신 시 호출되는 가상 핸들러.
        /// 기본 동작은 <see cref="SendExternalApiCallLogAsync"/>를 호출하여 "Call API {requestUrl}" 로그를 전송함.
        /// 조건에 따라 로그 전송 방식을 커스텀해야 하는 경우 파생 클래스에서 override할 것.
        /// </summary>
        protected virtual void OnExternalApiCall(string requestUrl)
        {
            SendExternalApiCallLogAsync(requestUrl).Forget();
        }

        /// <summary>
        /// 외부 API 반환 이벤트(<see cref="ExternalApiReturnEvent"/>) 수신 시 호출되는 가상 핸들러.
        /// 기본 동작은 <see cref="SendExternalApiReturnLogAsync"/>를 호출하여 "Return OK" 또는
        /// "Return fail"(<paramref name="failReason"/>이 있으면 "Return fail: {failReason}") 로그를 전송함.
        /// 조건에 따라 로그 전송 방식을 커스텀해야 하는 경우 파생 클래스에서 override할 것.
        /// </summary>
        protected virtual void OnExternalApiReturn(bool isSuccess, string failReason = null)
        {
            SendExternalApiReturnLogAsync(isSuccess, failReason).Forget();
        }

        /// <summary>
        /// "최초 화면"이 별도 씬인지 같은 씬의 첫 패널인지는 프로젝트마다 다르므로, 언제
        /// 그 화면으로 돌아갔는지는 이 클래스가 자동으로 판단하지 않음. 대신 OnEnable에서
        /// MoveIdleEvent를 구독해 이 메서드를 자동으로 호출하므로, 프로젝트 코드는 실제로
        /// idle 화면에 진입하는 지점에서 IPublisher&lt;MoveIdleEvent&gt;.Publish만 호출하면
        /// 됨(ApiManagerBase를 직접 참조하지 않아도 됨). 이 메서드를 직접 호출해도 무방함.
        /// InactivityTimer의 타임아웃으로 돌아간 경우는 <see cref="SendMoveIdleTimeoutLogAsync"/>를
        /// 대신 호출할 것(이 클래스가 InactivityTimeoutEvent를 직접 구독해 자동으로 호출함).
        /// </summary>
        public UniTask SendMoveIdleLogAsync(CancellationToken cancellationToken = default)
        {
            return SendSimpleLogAsync(MoveIdleMessage, cancellationToken);
        }

        /// <summary>
        /// InactivityTimer가 타임아웃되어 idle 화면으로 돌아간 경우 전용. OnEnable에서
        /// InactivityTimeoutEvent를 구독해 이 메서드를 자동으로 호출하므로 보통 직접 호출할
        /// 일은 없음. 일반적인 idle 복귀(예: 콘텐츠 종료 버튼)는 <see cref="SendMoveIdleLogAsync"/>를
        /// 쓸 것(직접 호출하거나 MoveIdleEvent를 발행하면 자동으로 호출됨).
        /// </summary>
        public UniTask SendMoveIdleTimeoutLogAsync(CancellationToken cancellationToken = default)
        {
            return SendSimpleLogAsync(MoveIdleTimeoutMessage, cancellationToken);
        }

        /// <summary>
        /// 외부 API(wavespeed, gpt 등) 호출 시작 로그("Call API {requestUrl}")를 서버에 전송함.
        /// 직접 호출하거나 <see cref="ExternalApiCallEvent"/>를 발행하면 자동으로 호출됨.
        /// </summary>
        public UniTask SendExternalApiCallLogAsync(string requestUrl, CancellationToken cancellationToken = default)
        {
            return SendSimpleLogAsync(ZString.Concat(ExternalApiCallPrefix, requestUrl), cancellationToken);
        }

        /// <summary>
        /// 외부 API(wavespeed, gpt 등) 호출 결과 로그를 서버에 전송함. 성공 시 "Return OK",
        /// 실패 시 "Return fail"(<paramref name="failReason"/>이 있으면 "Return fail: {failReason}")을 전송함.
        /// 직접 호출하거나 <see cref="ExternalApiReturnEvent"/>를 발행하면 자동으로 호출됨.
        /// </summary>
        public UniTask SendExternalApiReturnLogAsync(bool isSuccess, string failReason = null, CancellationToken cancellationToken = default)
        {
            return SendSimpleLogAsync(BuildReturnMessage(isSuccess, failReason), cancellationToken);
        }

        /// <summary>
        /// "Return OK" 또는 "Return fail"(사유가 있으면 "Return fail: {failReason}") 메시지를 조립함.
        /// </summary>
        private static string BuildReturnMessage(bool isSuccess, string failReason)
        {
            if (isSuccess) return ExternalApiReturnOkMessage;
            if (string.IsNullOrEmpty(failReason)) return ExternalApiReturnFailMessage;
            return ZString.Concat(ExternalApiReturnFailMessage, ": ", failReason);
        }

        /// <summary>
        /// 예외 메시지를 실패 사유로 자동 전송하기 전에 URL 쿼리 스트링을 제거함. 실패한 요청의 URL이
        /// 예외 메시지에 그대로 담기는 HTTP 클라이언트가 있는데, 쿼리 스트링에는 API 키·토큰 등이
        /// 담기는 경우가 많아 그대로 사내 로그 서버로 전송하면 유출될 수 있음.
        /// <para>
        /// 호출자가 <see cref="SendExternalApiReturnLogAsync"/>/<see cref="SendExternalApiReturnFailLogAsync"/>에
        /// 직접 넘기는 failReason에는 적용하지 않음. 그 값은 호출자가 내용을 직접 통제하므로 여기서
        /// 임의로 잘라내면 오히려 의도한 정보가 가려질 수 있음. 이 메서드는 예외에서 자동으로
        /// 캡처되는 <see cref="ExecuteWithExternalApiLoggingAsync{T}"/> 경로에만 적용함.
        /// </para>
        /// </summary>
        private static string SanitizeFailReason(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            return FailReasonQueryStringPattern.Replace(message, "?[REDACTED]");
        }

        /// <summary>
        /// 외부 API 호출 성공 로그("Return OK")를 서버에 전송함.
        /// </summary>
        public UniTask SendExternalApiReturnSuccessLogAsync(CancellationToken cancellationToken = default)
        {
            return SendExternalApiReturnLogAsync(true, null, cancellationToken);
        }

        /// <summary>
        /// 외부 API 호출 실패 로그("Return fail", <paramref name="failReason"/>이 있으면
        /// "Return fail: {failReason}")를 서버에 전송함.
        /// </summary>
        public UniTask SendExternalApiReturnFailLogAsync(string failReason = null, CancellationToken cancellationToken = default)
        {
            return SendExternalApiReturnLogAsync(false, failReason, cancellationToken);
        }

        /// <summary>
        /// 외부 API 호출 전후로 서버에 Call/Return 로그를 자동 전송하며 비동기 작업을 수행하는 헬퍼 메서드.
        /// 시작 시 "Call API {requestUrl}"을 전송하고, 성공 시 "Return OK", 예외 발생 시
        /// "Return fail: {예외 메시지}"를 전송함(발생한 예외는 로그 전송 후 다시 throw됨).
        /// 예외 메시지에 URL 쿼리 스트링이 포함돼 있으면 <see cref="SanitizeFailReason"/>이 제거한 뒤 전송함.
        /// </summary>
        public async UniTask<T> ExecuteWithExternalApiLoggingAsync<T>(string requestUrl, Func<UniTask<T>> apiAction, CancellationToken cancellationToken = default)
        {
            if (apiAction == null)
            {
                throw new ArgumentNullException(nameof(apiAction));
            }

            await SendExternalApiCallLogAsync(requestUrl, cancellationToken);
            try
            {
                T result = await apiAction();
                await SendExternalApiReturnLogAsync(true, null, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                // apiAction 실행 도중 전달된 토큰이 취소되었더라도 Return fail 로그가 유실되지 않도록 CancellationToken.None으로 전송함.
                await SendExternalApiReturnLogAsync(false, SanitizeFailReason(ex.Message), CancellationToken.None);
                throw;
            }
        }

        /// <summary>
        /// 반환값이 없는 외부 API 호출 전후로 서버에 Call/Return 로그를 자동 전송하며 비동기 작업을 수행하는 헬퍼 메서드.
        /// 시작 시 "Call API {requestUrl}"을 전송하고, 성공 시 "Return OK", 예외 발생 시
        /// "Return fail: {예외 메시지}"를 전송함(발생한 예외는 로그 전송 후 다시 throw됨).
        /// 예외 메시지에 URL 쿼리 스트링이 포함돼 있으면 <see cref="SanitizeFailReason"/>이 제거한 뒤 전송함.
        /// </summary>
        public async UniTask ExecuteWithExternalApiLoggingAsync(string requestUrl, Func<UniTask> apiAction, CancellationToken cancellationToken = default)
        {
            if (apiAction == null)
            {
                throw new ArgumentNullException(nameof(apiAction));
            }

            await SendExternalApiCallLogAsync(requestUrl, cancellationToken);
            try
            {
                await apiAction();
                await SendExternalApiReturnLogAsync(true, null, cancellationToken);
            }
            catch (Exception ex)
            {
                // apiAction 실행 도중 전달된 토큰이 취소되었더라도 Return fail 로그가 유실되지 않도록 CancellationToken.None으로 전송함.
                await SendExternalApiReturnLogAsync(false, SanitizeFailReason(ex.Message), CancellationToken.None);
                throw;
            }
        }

        /// <summary>
        /// 시작/종료 로그처럼 재시도·시간 상한 정책이 특별히 필요하지 않은 단발성 상태
        /// 메시지를 ApiRetryUtil의 기본 정책(최대 10회, 3초 간격)으로 전송하는 공통 경로.
        /// </summary>
        private async UniTask SendSimpleLogAsync(string message, CancellationToken cancellationToken)
        {
            if (SettingsProvider == null)
            {
                return;
            }

            try
            {
                Settings settings = await SettingsProvider.GetAsync(cancellationToken);

                if (settings == null || string.IsNullOrEmpty(settings.apiUrl))
                {
                    if (Logger != null) Logger.ZLogInformation($"[ApiManagerBase] apiUrl is not set; skipping log: {message}");
                    return;
                }

                string url = settings.apiUrl + Uri.EscapeDataString(message);
                await ApiRetryUtil.SendGetRequestWithRetryAsync(url, message, Logger, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴 등으로 인한 정상적인 취소
            }
            catch (Exception e)
            {
                if (Logger != null) Logger.ZLogError($"[ApiManagerBase] Exception while sending log ({message}): {e.Message}");
            }
        }

        /// <summary>
        /// 원본 인스턴스일 때만 싱글톤 점유를 해제함.
        /// </summary>
        protected virtual void OnDestroy()
        {
            SingletonGuard<ApiManagerBase>.Release(_isOriginal);
        }
    }
}
