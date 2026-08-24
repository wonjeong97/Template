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
    /// 프로그램 시작 시 서버에 시작 로그를 1회 전송하는 매니저. 날짜/시간은 서버에서
    /// 수신 시점에 자동으로 기록되므로 클라이언트는 상태 메시지만 전송함.
    /// Settings.json의 apiUrl은 idx_content_device, uid 등 콘텐츠별 쿼리 파라미터가
    /// 이미 포함된 형태(message= 까지)로 서버에서 발급되므로, 여기서는 message 값만
    /// 이어붙여 GET 요청을 보냄. 실패 시 3초 간격으로 최대 10회까지 재시도하며,
    /// 그래도 실패하면 로그만 남기고 포기함(전시/키오스크 환경에서 네트워크 장애로
    /// 앱 실행 자체가 막히면 안 되므로).
    /// </summary>
    public class ApiManager : MonoBehaviour
    {
        /// <summary>
        /// 오늘 시작 로그를 이미 성공적으로 전송했는지 판별하기 위해 마지막 전송 날짜를
        /// 기기에 저장해두는 PlayerPrefs 키. 같은 날 재실행되면 "재시작"으로 구분함.
        /// </summary>
        private const string LastStartupLogDateKey = "ApiManager_LastStartupLogDate";

        /// <summary>네트워크 실패 시 최대 재시도 횟수(최초 시도 포함).</summary>
        private const int MaxAttemptCount = 10;

        /// <summary>재시도 사이의 대기 시간(초).</summary>
        private const float RetryDelaySeconds = 3f;

        private ILogger<ApiManager> _logger;
        private AppSettingsProvider _settingsProvider;

        [Inject]
        public void Construct(ILogger<ApiManager> logger, AppSettingsProvider settingsProvider)
        {
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        private void Start()
        {
            SendStartupLogAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid SendStartupLogAsync(CancellationToken cancellationToken)
        {
            try
            {
                Settings settings = await _settingsProvider.GetAsync(cancellationToken);

                if (settings == null || string.IsNullOrEmpty(settings.apiUrl))
                {
                    if (_logger != null) _logger.ZLogInformation($"[ApiManager] apiUrl이 설정되지 않아 시작 로그를 전송하지 않음.");
                    return;
                }

                string today = DateTime.Now.ToString("yyyy-MM-dd");
                bool alreadyLoggedToday = PlayerPrefs.GetString(LastStartupLogDateKey, string.Empty) == today;
                string message = alreadyLoggedToday ? "프로그램 재시작 됨" : "프로그램 시작 됨";

// 에디터/디벨롭 빌드에서 매 플레이·테스트 빌드마다 서버로 로그가 나가면 실제 운영 로그가
// 오염되므로, 이 두 환경에서는 전송을 생략하고 무엇을 보냈을지만 콘솔에 남김.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_logger != null) _logger.ZLogInformation($"[ApiManager] 에디터/디벨롭 빌드이므로 시작 로그를 전송하지 않음: {message}");
#else
                // 네트워크 자체가 연결되어 있지 않으면 시도해도 무조건 실패하므로, 재시도 루프를
                // 돌리며 30초(3초 x 10회)를 허비하지 않도록 먼저 걸러냄.
                if (Application.internetReachability == NetworkReachability.NotReachable)
                {
                    if (_logger != null) _logger.ZLogWarning($"[ApiManager] 네트워크가 연결되어 있지 않아 시작 로그를 전송하지 않음: {message}");
                    return;
                }

                string url = settings.apiUrl + Uri.EscapeDataString(message);

                for (int attempt = 1; attempt <= MaxAttemptCount; attempt++)
                {
                    using UnityWebRequest request = UnityWebRequest.Get(url);
                    await request.SendWebRequest().WithCancellation(cancellationToken);

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        if (_logger != null) _logger.ZLogInformation($"[ApiManager] 시작 로그 전송 성공({attempt}/{MaxAttemptCount}): {message}");

                        // 같은 날 재실행 시 "재시작"으로 구분되도록, 전송이 실제로 성공했을 때만 날짜를 갱신함.
                        PlayerPrefs.SetString(LastStartupLogDateKey, today);
                        PlayerPrefs.Save();
                        break;
                    }

                    bool isLastAttempt = attempt == MaxAttemptCount;

                    if (isLastAttempt)
                    {
                        if (_logger != null) _logger.ZLogError($"[ApiManager] 시작 로그 전송 실패({attempt}/{MaxAttemptCount}, 재시도 중단): {message}, {request.error}");
                    }
                    else
                    {
                        if (_logger != null) _logger.ZLogWarning($"[ApiManager] 시작 로그 전송 실패({attempt}/{MaxAttemptCount}), {RetryDelaySeconds}초 후 재시도: {message}, {request.error}");
                        await UniTask.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken: cancellationToken);
                    }
                }
#endif
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴로 인한 정상적인 취소
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogError($"[ApiManager] 시작 로그 전송 중 예외 발생: {e.Message}");
            }
        }
    }
}
