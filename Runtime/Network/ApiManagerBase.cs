using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer;
using Wonjeong.Data;
using ZLogger;

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

        protected ILogger<ApiManagerBase> Logger { get; private set; }
        protected AppSettingsProvider SettingsProvider { get; private set; }

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
