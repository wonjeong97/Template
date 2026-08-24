using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.Networking;
using VContainer;
using Wonjeong.Data;
using ZLogger;

namespace Wonjeong.Network
{
    /// <summary>
    /// 프로그램 시작 시 서버에 시작 로그를 1회 전송하는 매니저의 기반 클래스.
    /// 콘텐츠마다 호출해야 하는 API가 다를 수 있으므로, 이 클래스를 상속해 Start()를
    /// override하고 <see cref="SendGetRequestWithRetryAsync"/>를 재사용하면 시작 로그 외의
    /// 다른 API 호출에도 동일한 재시도/네트워크 확인/에디터·디벨롭 빌드 스킵 정책을 그대로
    /// 적용할 수 있음.
    /// <para>
    /// Settings.json의 apiUrl은 idx_content_device, uid 등 콘텐츠별 쿼리 파라미터가
    /// 이미 포함된 형태(message= 까지)로 서버에서 발급되므로, 시작 로그는 여기에 상태
    /// 메시지 값만 이어붙여 GET 요청을 보냄.
    /// </para>
    /// </summary>
    public class ApiManagerBase : MonoBehaviour
    {
        /// <summary>
        /// 오늘 시작 로그를 이미 성공적으로 전송했는지 판별하기 위해 마지막 전송 날짜를
        /// 기기에 저장해두는 PlayerPrefs 키. 같은 날 재실행되면 "재시작"으로 구분함.
        /// </summary>
        private const string LastStartupLogDateKey = "ApiManagerBase_LastStartupLogDate";

        /// <summary>네트워크 실패 시 최대 재시도 횟수(최초 시도 포함).</summary>
        private const int MaxAttemptCount = 10;

        /// <summary>재시도 사이의 대기 시간(초).</summary>
        private const float RetryDelaySeconds = 3f;

        protected ILogger<ApiManagerBase> Logger { get; private set; }
        protected AppSettingsProvider SettingsProvider { get; private set; }

        [Inject]
        public void Construct(ILogger<ApiManagerBase> logger, AppSettingsProvider settingsProvider)
        {
            Logger = logger;
            SettingsProvider = settingsProvider;
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
            try
            {
                Settings settings = await SettingsProvider.GetAsync(cancellationToken);

                if (settings == null || string.IsNullOrEmpty(settings.apiUrl))
                {
                    if (Logger != null) Logger.ZLogInformation($"[ApiManagerBase] apiUrl이 설정되지 않아 시작 로그를 전송하지 않음.");
                    return;
                }

                string today = DateTime.Now.ToString("yyyy-MM-dd");
                bool alreadyLoggedToday = PlayerPrefs.GetString(LastStartupLogDateKey, string.Empty) == today;
                string message = alreadyLoggedToday ? "프로그램 재시작 됨" : "프로그램 시작 됨";
                string url = settings.apiUrl + Uri.EscapeDataString(message);

                bool success = await SendGetRequestWithRetryAsync(url, $"시작 로그({message})", cancellationToken);

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
                if (Logger != null) Logger.ZLogError($"[ApiManagerBase] 시작 로그 전송 중 예외 발생: {e.Message}");
            }
        }

        /// <summary>
        /// GET 요청을 다음 공통 정책과 함께 전송함: 에디터/디벨롭 빌드에서는 실제 전송 없이
        /// 무엇을 보냈을지만 로그로 남기고, 네트워크 자체가 연결되어 있지 않으면 즉시 포기하며,
        /// 그 외 실패는 <see cref="RetryDelaySeconds"/>초 간격으로 최대 <see cref="MaxAttemptCount"/>회
        /// 재시도함. 콘텐츠별로 추가 API를 호출해야 하는 파생 클래스에서 재사용할 수 있도록
        /// protected로 공개함.
        /// </summary>
        /// <param name="url">요청 URL.</param>
        /// <param name="logLabel">로그에 표시할 요청 식별용 라벨(예: "시작 로그").</param>
        /// <returns>실제로 전송을 시도해 성공하면 true. 에디터/디벨롭 빌드·네트워크 미연결로
        /// 전송을 생략했거나 재시도를 모두 소진해 실패했으면 false.</returns>
        // 에디터/디벨롭 빌드 분기는 await 없이 즉시 반환하므로, 두 플랫폼 분기가 동일한
        // async UniTask<bool> 시그니처를 공유하는 데서 오는 CS1998을 의도적으로 억제함.
#pragma warning disable CS1998
        protected async UniTask<bool> SendGetRequestWithRetryAsync(string url, string logLabel, CancellationToken cancellationToken)
        {
// 에디터/디벨롭 빌드에서 매 플레이·테스트마다 서버로 로그가 나가면 실제 운영 로그가
// 오염되므로, 이 두 환경에서는 전송을 생략하고 무엇을 보냈을지만 콘솔에 남김.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Logger != null) Logger.ZLogInformation($"[ApiManagerBase] 에디터/디벨롭 빌드이므로 전송하지 않음: {logLabel}");
            return false;
#else
            // 네트워크 자체가 연결되어 있지 않으면 시도해도 무조건 실패하므로, 재시도 루프를
            // 돌리며 최대 대기 시간을 허비하지 않도록 먼저 걸러냄.
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                if (Logger != null) Logger.ZLogWarning($"[ApiManagerBase] 네트워크가 연결되어 있지 않아 전송하지 않음: {logLabel}");
                return false;
            }

            for (int attempt = 1; attempt <= MaxAttemptCount; attempt++)
            {
                using UnityWebRequest request = UnityWebRequest.Get(url);
                await request.SendWebRequest().WithCancellation(cancellationToken);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    if (Logger != null) Logger.ZLogInformation($"[ApiManagerBase] 전송 성공({attempt}/{MaxAttemptCount}): {logLabel}");
                    return true;
                }

                bool isLastAttempt = attempt == MaxAttemptCount;

                if (isLastAttempt)
                {
                    if (Logger != null) Logger.ZLogError($"[ApiManagerBase] 전송 실패({attempt}/{MaxAttemptCount}, 재시도 중단): {logLabel}, {request.error}");
                }
                else
                {
                    if (Logger != null) Logger.ZLogWarning($"[ApiManagerBase] 전송 실패({attempt}/{MaxAttemptCount}), {RetryDelaySeconds}초 후 재시도: {logLabel}, {request.error}");
                    await UniTask.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken: cancellationToken);
                }
            }

            return false;
#endif
        }
#pragma warning restore CS1998
    }
}
