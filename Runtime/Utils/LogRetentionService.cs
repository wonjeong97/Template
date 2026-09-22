using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer.Unity;
using ZLogger;

namespace HuliacDev.Utils
{
    /// <summary>
    /// 보관 기간을 초과한 오래된 로그 파일을 주기적으로 삭제하는 서비스.
    /// <para>
    /// ZLogger의 AddZLoggerRollingFile은 날짜/용량 기준으로 파일을 새로 만들기만 할 뿐
    /// 오래된 파일을 지우지 않으므로(2.5.10 기준 보관 개수/기간 옵션 없음), 그대로 두면
    /// 몇 주~몇 달 무중단으로 도는 키오스크에서 로그 디렉터리 총량이 무한정 커짐. 이를 보완함.
    /// </para>
    /// <para>
    /// RootLifetimeScope.ConfigureLogging(로깅 프로바이더 구성)과 책임을 분리해 별도
    /// VContainer 엔트리포인트(IStartable)로 등록함. "무엇을 어떻게 남길지"와 "오래된 것을
    /// 언제 지울지"는 서로 다른 책임이며, 분리하면 순수 정리 로직(CleanupOldLogs)을
    /// 단위 테스트로 검증할 수 있고 향후 정리 정책(주기, 보관일 등) 변경이 로깅 설정과
    /// 독립적으로 이뤄질 수 있음.
    /// </para>
    /// </summary>
    public sealed class LogRetentionService : IStartable, IDisposable
    {
        /// <summary>
        /// 회전 로그 파일 이름의 공통 접두사. RootLifetimeScope의 AddZLoggerRollingFile
        /// FilePathSelector와 이 서비스의 정리 대상 검색 패턴이 같은 접두사를 참조하도록
        /// 단일 소스로 둠(둘 중 하나만 바뀌어 정리 대상을 놓치는 것을 방지).
        /// </summary>
        public const string LogFilePrefix = "GameLog";

        /// <summary>정리 주기(실시간 기준). Time.timeScale과 무관하게 24시간마다 정리함.</summary>
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

        private readonly string _logDirectory;
        private readonly int _retentionDays;
        private readonly ILogger<LogRetentionService> _logger;
        private CancellationTokenSource _cts;

        /// <summary>
        /// 정리 대상 로그 디렉터리와 보관 기간(일)을 받아 서비스를 구성함.
        /// 로거를 넘기지 않으면 정리 결과와 실패를 Debug.Log로 대체 출력함.
        /// </summary>
        public LogRetentionService(string logDirectory, int retentionDays, ILogger<LogRetentionService> logger = null)
        {
            _logDirectory = logDirectory;
            _retentionDays = retentionDays;
            _logger = logger;
        }

        /// <summary>
        /// VContainer 컨테이너 빌드 완료 시 자동 호출됨. 정리 루프를 시작함.
        /// </summary>
        void IStartable.Start()
        {
            // 앱 종료 시 루프가 함께 정리되도록 Application.exitCancellationToken에 연결함.
            _cts = CancellationTokenSource.CreateLinkedTokenSource(Application.exitCancellationToken);
            RunRetentionLoopAsync(_cts.Token).Forget();
        }

        /// <summary>
        /// 보관 기간을 넘긴 로그 파일을 시작 시 1회 정리한 뒤 24시간마다 반복 정리함.
        /// 재시작 없이 장기간 도는 키오스크에서도 디스크 사용량을 최근 N일치로 유지하기 위해
        /// startup-only 정리가 아닌 주기 반복으로 구현함.
        /// </summary>
        private async UniTaskVoid RunRetentionLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 메인 스레드 블로킹 및 프레임 드랍(Hitch)을 방지하기 위해 백그라운드 스레드에서 파일 I/O를 수행함.
                    await UniTask.RunOnThreadPool(() => CleanupOldLogs(_logDirectory, _retentionDays, _logger), cancellationToken: cancellationToken);

                    // 게임 시간(timeScale)과 무관한 실제 경과 시간 기준으로 대기함.
                    await UniTask.Delay(CleanupInterval, DelayType.Realtime, PlayerLoopTiming.Update, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 로그 디렉터리에서 마지막 기록 시각이 보관 기간을 지난 파일을 삭제함.
        /// 정리 실패가 앱 실행을 막아선 안 되므로 예외는 경고 로그만 남기고 삼킴.
        /// 현재 기록 중인 파일은 최근 기록 시각을 가지므로 삭제 대상에서 자연히 제외됨.
        /// <para>
        /// 순수 정리 로직만 담당하는 static 메서드로 분리하여, MonoBehaviour나 VContainer
        /// 컨테이너 없이도 임시 디렉터리를 대상으로 단위 테스트가 가능하게 함.
        /// </para>
        /// </summary>
        public static void CleanupOldLogs(string logDirectory, int retentionDays, ILogger<LogRetentionService> logger = null)
        {
            try
            {
                if (!Directory.Exists(logDirectory)) return;

                DateTime threshold = DateTime.Now.AddDays(-retentionDays);

                // 1. 회전 파일(GameLog_*.txt) 정리
                foreach (string file in Directory.GetFiles(logDirectory, $"{LogFilePrefix}_*.txt"))
                {
                    TryDeleteIfExpired(file, threshold, logger);
                }

                // 2. 회전 로깅 도입 이전 레거시 단일 로그 파일(GameLog.txt) 정리
                // 구버전에서 장기 구동으로 비대해진 기존 로그가 업데이트 후 영구 방치되는 것을 방지함.
                string legacyFile = Path.Combine(logDirectory, $"{LogFilePrefix}.txt");
                if (File.Exists(legacyFile))
                {
                    TryDeleteIfExpired(legacyFile, threshold, logger);
                }
            }
            catch (Exception e)
            {
                if (logger != null) logger.ZLogWarning($"[LogRetentionService] Failed to clean up old logs: {e.Message}");
                else Debug.LogWarning($"[LogRetentionService] Failed to clean up old logs: {e.Message}");
            }
        }

        /// <summary>
        /// 파일의 마지막 기록 시각이 보관 기준을 넘긴 경우 삭제를 시도함.
        /// 특정 파일이 다른 프로세스(뷰어, 백업 등)에 잠겨 실패하더라도
        /// 전체 정리 루프가 중단되지 않도록 파일 단위로 예외를 격리함.
        /// </summary>
        private static void TryDeleteIfExpired(string filePath, DateTime threshold, ILogger<LogRetentionService> logger)
        {
            try
            {
                if (File.GetLastWriteTime(filePath) < threshold)
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception e)
            {
                if (logger != null) logger.ZLogWarning($"[LogRetentionService] Failed to delete log file ({filePath}): {e.Message}");
                else Debug.LogWarning($"[LogRetentionService] Failed to delete log file ({filePath}): {e.Message}");
            }
        }

        /// <summary>
        /// 컨테이너 파기 시 VContainer가 자동 호출함. 정리 루프를 취소하고 토큰 소스를 해제함.
        /// </summary>
        void IDisposable.Dispose()
        {
            if (_cts == null) return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }
}
