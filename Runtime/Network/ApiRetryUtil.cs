using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.Networking;
using ZLogger;

namespace Wonjeong.Network
{
    /// <summary>
    /// GET 요청을 공통 정책과 함께 전송하는 재사용 가능한 정적 유틸리티.
    /// ApiManagerBase의 시작 로그 전송 기능과 분리되어 있어, 이 재시도 정책만 필요한
    /// 코드는 ApiManagerBase를 상속하지 않고도 바로 호출할 수 있음.
    /// <para>
    /// 정책: 에디터/디벨롭 빌드에서는 실제 전송 없이 무엇을 보냈을지만 로그로 남기고,
    /// 네트워크 자체가 연결되어 있지 않으면 즉시 포기하며, 그 외 실패는
    /// retryDelaySeconds 간격으로 최대 maxAttemptCount회 재시도함.
    /// </para>
    /// </summary>
    public static class ApiRetryUtil
    {
        /// <summary>네트워크 실패 시 기본 최대 재시도 횟수(최초 시도 포함).</summary>
        public const int DefaultMaxAttemptCount = 10;

        /// <summary>재시도 사이의 기본 대기 시간(초).</summary>
        public const float DefaultRetryDelaySeconds = 3f;

        /// <param name="url">요청 URL.</param>
        /// <param name="logLabel">로그에 표시할 요청 식별용 라벨(예: "시작 로그").</param>
        /// <param name="logger">로그 출력에 사용할 로거. null이면 로그를 남기지 않음.</param>
        /// <param name="cancellationToken">취소 토큰.</param>
        /// <param name="maxAttemptCount">최대 재시도 횟수(최초 시도 포함).</param>
        /// <param name="retryDelaySeconds">재시도 사이의 대기 시간(초).</param>
        /// <returns>실제로 전송을 시도해 성공하면 true. 에디터/디벨롭 빌드·네트워크 미연결로
        /// 전송을 생략했거나 재시도를 모두 소진해 실패했으면 false.</returns>
        // 에디터/디벨롭 빌드 분기는 await 없이 즉시 반환하므로 이 컴파일 변형에서만 CS1998이
        // 발생함. 실제 빌드(#else)는 await를 포함하므로 이 심볼 조합에서만 억제를 한정함.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#pragma warning disable CS1998
#endif
        public static async UniTask<bool> SendGetRequestWithRetryAsync(
            string url,
            string logLabel,
            Microsoft.Extensions.Logging.ILogger logger,
            CancellationToken cancellationToken,
            int maxAttemptCount = DefaultMaxAttemptCount,
            float retryDelaySeconds = DefaultRetryDelaySeconds)
        {
// 에디터/디벨롭 빌드에서 매 플레이·테스트마다 서버로 로그가 나가면 실제 운영 로그가
// 오염되므로, 이 두 환경에서는 전송을 생략하고 무엇을 보냈을지만 콘솔에 남김.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (logger != null) logger.ZLogInformation($"[ApiRetryUtil] Editor/development build; skipping send: {logLabel}");
            return false;
#else
            // 네트워크 자체가 연결되어 있지 않으면 시도해도 무조건 실패하므로, 재시도 루프를
            // 돌리며 최대 대기 시간을 허비하지 않도록 먼저 걸러냄.
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                if (logger != null) logger.ZLogWarning($"[ApiRetryUtil] Network is not reachable; skipping send: {logLabel}");
                return false;
            }

            for (int attempt = 1; attempt <= maxAttemptCount; attempt++)
            {
                using UnityWebRequest request = UnityWebRequest.Get(url);
                await request.SendWebRequest().WithCancellation(cancellationToken);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    if (logger != null) logger.ZLogInformation($"[ApiRetryUtil] Send succeeded ({attempt}/{maxAttemptCount}): {logLabel}");
                    return true;
                }

                bool isLastAttempt = attempt == maxAttemptCount;

                if (isLastAttempt)
                {
                    if (logger != null) logger.ZLogError($"[ApiRetryUtil] Send failed ({attempt}/{maxAttemptCount}, giving up): {logLabel}, {request.error}");
                }
                else
                {
                    if (logger != null) logger.ZLogWarning($"[ApiRetryUtil] Send failed ({attempt}/{maxAttemptCount}), retrying in {retryDelaySeconds}s: {logLabel}, {request.error}");
                    // 재시도 대기는 Time.timeScale과 무관해야 함. 일시정지(timeScale=0) 중에도
                    // 네트워크 재시도는 계속 진행되어야 하며, 그렇지 않으면 재시도 루프가
                    // 무한정 멈추는 소프트락이 발생함(FadeManager에서 겪었던 것과 동일한 문제).
                    await UniTask.Delay(TimeSpan.FromSeconds(retryDelaySeconds), DelayType.UnscaledDeltaTime, cancellationToken: cancellationToken);
                }
            }

            return false;
#endif
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#pragma warning restore CS1998
#endif
    }
}
