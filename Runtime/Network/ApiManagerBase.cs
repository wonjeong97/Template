using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer;
using Wonjeong.Data;
using ZLogger;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Wonjeong.Network
{
    /// <summary>
    /// 프로그램 시작 시 서버에 시작 로그를 1회 전송하는 매니저의 기반 클래스.
    /// 콘텐츠마다 호출해야 하는 API가 다를 수 있으므로, 프로젝트별 클래스가 이 클래스를
    /// 상속해 Start()를 override하고 <see cref="ApiRetryUtil.SendGetRequestWithRetryAsync"/>를
    /// 재사용하면 시작 로그 외의 다른 API 호출에도 동일한 재시도/네트워크 확인/에디터·디벨롭
    /// 빌드 스킵 정책을 그대로 적용할 수 있음(GameManagerBase&lt;T&gt;와 동일하게, abstract이므로
    /// 씬에는 이 클래스를 상속한 프로젝트 전용 클래스를 배치할 것).
    /// <para>
    /// Settings.json의 apiUrl은 idx_content_device, uid 등 콘텐츠별 쿼리 파라미터가
    /// 이미 포함된 형태(message= 까지)로 서버에서 발급되므로, 시작 로그는 여기에 상태
    /// 메시지 값만 이어붙여 GET 요청을 보냄.
    /// </para>
    /// </summary>
    public abstract class ApiManagerBase : MonoBehaviour
    {
        /// <summary>
        /// 오늘 시작 로그를 이미 성공적으로 전송했는지 판별하기 위해 마지막 전송 날짜를
        /// 기기에 저장해두는 PlayerPrefs 키. 같은 날 재실행되면 "재시작"으로 구분함.
        /// </summary>
        private const string LastStartupLogDateKey = "ApiManagerBase_LastStartupLogDate";

        /// <summary>종료 시 서버에 보낼 상태 메시지.</summary>
        private const string ExitLogMessage = "Program exited";

        // 종료 요청을 한 번 보류하고 로그를 보낸 뒤 다시 종료를 진행하기 위한 상태.
        private bool _isQuitConfirmed;
        private bool _isSendingExitLog;

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

        [Inject]
        public void Construct(ILogger<ApiManagerBase> logger, AppSettingsProvider settingsProvider)
        {
            Logger = logger;
            SettingsProvider = settingsProvider;
        }

        /// <summary>
        /// 다른 선택 매니저(FadeManager/SoundManager/UIManager/VideoManager)와 동일하게,
        /// 씬 전환으로 재생성되어 시작 로그가 중복 전송되지 않도록 파괴를 방지함.
        /// </summary>
        protected virtual void Awake()
        {
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        /// <summary>
        /// 종료 로그 전송을 위해 종료 요청을 가로챌 수 있도록 이벤트를 구독함.
        /// 파생 클래스에서 override할 경우 반드시 base.OnEnable()을 호출할 것.
        /// 빠뜨리면 구독이 누락되어 종료 로그가 전송되지 않음.
        /// </summary>
        protected virtual void OnEnable()
        {
            Application.wantsToQuit += OnWantsToQuit;
        }

        /// <summary>
        /// 구독을 해제함. 파생 클래스에서 override할 경우 반드시 base.OnDisable()을 호출할 것.
        /// </summary>
        protected virtual void OnDisable()
        {
            Application.wantsToQuit -= OnWantsToQuit;
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

                string url = settings.apiUrl + Uri.EscapeDataString(ExitLogMessage);

                await ApiRetryUtil.SendGetRequestWithRetryAsync(
                    url,
                    $"exit log ({ExitLogMessage})",
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
            SendStartupLogAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

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
                string message = alreadyLoggedToday ? "Program restarted" : "Program started";
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
    }
}
