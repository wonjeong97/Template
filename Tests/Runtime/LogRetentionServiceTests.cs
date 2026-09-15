using System;
using System.IO;
using NUnit.Framework;
using Wonjeong.Utils;

namespace Wonjeong.Tests
{
    /// <summary>
    /// LogRetentionService.CleanupOldLogs의 순수 정리 로직을 검증함.
    ///
    /// 배경: 로그 회전(AddZLoggerRollingFile)만으로는 오래된 파일이 지워지지 않아
    /// 몇 달 무중단으로 도는 키오스크에서 디렉터리 총량이 무한정 커지는 문제가 있었음.
    /// 보관 기간을 초과한 파일만 정확히 삭제하고, 최근 파일과 무관한 파일은
    /// 건드리지 않는지가 이 서비스의 핵심 계약이므로 이를 회귀 없이 검증함.
    /// </summary>
    public class LogRetentionServiceTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LogRetentionServiceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        /// <summary>
        /// 마지막 기록 시각이 보관 기간을 지난 로그 파일은 삭제되어야 함.
        /// </summary>
        [Test]
        public void 보관기간을_지난_로그파일은_삭제된다()
        {
            string oldFile = CreateLogFile("GameLog_2000-01-01_000.txt", daysAgo: 60);

            LogRetentionService.CleanupOldLogs(_tempDir, retentionDays: 30);

            Assert.IsFalse(File.Exists(oldFile), "보관 기간을 지난 로그 파일이 삭제되지 않음");
        }

        /// <summary>
        /// 보관 기간 이내의 최근 로그 파일(현재 기록 중인 파일 포함)은 보존되어야 함.
        /// 이 검증이 없으면 정리 로직이 과도하게 삭제해 진단용 최근 로그까지 날아갈 수 있음.
        /// </summary>
        [Test]
        public void 보관기간_이내의_로그파일은_보존된다()
        {
            string recentFile = CreateLogFile("GameLog_2099-01-01_000.txt", daysAgo: 1);

            LogRetentionService.CleanupOldLogs(_tempDir, retentionDays: 30);

            Assert.IsTrue(File.Exists(recentFile), "보관 기간 이내의 로그 파일이 잘못 삭제됨");
        }

        /// <summary>
        /// 검색 패턴(GameLog_*.txt)에 맞지 않는 파일은 오래됐더라도 건드리지 않아야 함.
        /// 로그 디렉터리에 다른 목적의 파일이 섞여도 안전해야 함.
        /// </summary>
        [Test]
        public void 무관한_파일은_오래돼도_삭제되지_않는다()
        {
            string unrelatedFile = CreateLogFile("OtherFile.txt", daysAgo: 60);

            LogRetentionService.CleanupOldLogs(_tempDir, retentionDays: 30);

            Assert.IsTrue(File.Exists(unrelatedFile), "정리 대상이 아닌 파일이 삭제됨");
        }

        /// <summary>
        /// 로그 디렉터리 자체가 아직 생성되지 않은 경우(첫 실행 등) 예외 없이 조용히 반환해야 함.
        /// </summary>
        [Test]
        public void 로그디렉터리가_없으면_예외없이_반환한다()
        {
            string missingDir = Path.Combine(_tempDir, "not_created_yet");

            Assert.DoesNotThrow(() => LogRetentionService.CleanupOldLogs(missingDir, retentionDays: 30));
        }

        /// <summary>
        /// 회전 도입 이전의 레거시 단일 로그 파일(GameLog.txt)도 보관 기간을 초과했다면 삭제되어야 함.
        /// 이전 버전에서 장기 구동되어 수백 MB~GB로 커진 단일 로그가 업데이트 후 영구 방치되는 것을 방지함.
        /// </summary>
        [Test]
        public void 레거시_단일_로그파일도_보관기간이_지나면_삭제된다()
        {
            string legacyFile = CreateLogFile("GameLog.txt", daysAgo: 60);

            LogRetentionService.CleanupOldLogs(_tempDir, retentionDays: 30);

            Assert.IsFalse(File.Exists(legacyFile), "보관 기간을 지난 레거시 GameLog.txt가 삭제되지 않음");
        }

        /// <summary>
        /// 레거시 단일 로그 파일(GameLog.txt)이라도 보관 기간 이내라면 보존되어야 함.
        /// </summary>
        [Test]
        public void 보관기간_이내의_레거시_단일_로그파일은_보존된다()
        {
            string recentLegacyFile = CreateLogFile("GameLog.txt", daysAgo: 5);

            LogRetentionService.CleanupOldLogs(_tempDir, retentionDays: 30);

            Assert.IsTrue(File.Exists(recentLegacyFile), "보관 기간 이내의 레거시 GameLog.txt가 잘못 삭제됨");
        }

        /// <summary>
        /// 다른 프로세스에 의해 특정 로그 파일이 잠겨 있어 삭제에 실패하더라도,
        /// 루프가 중단되지 않고 나머지 만료 파일들은 정상적으로 삭제되어야 함.
        /// </summary>
        [Test]
        public void 일부_파일_삭제_실패_시에도_다른_만료_파일은_정상_삭제된다()
        {
            string lockedFile = CreateLogFile("GameLog_2000-01-01_000.txt", daysAgo: 60);
            string otherOldFile = CreateLogFile("GameLog_2000-01-02_000.txt", daysAgo: 60);

            using (var stream = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                LogRetentionService.CleanupOldLogs(_tempDir, retentionDays: 30);
            }

            Assert.IsTrue(File.Exists(lockedFile), "잠긴 파일은 삭제 실패하여 남아있어야 함");
            Assert.IsFalse(File.Exists(otherOldFile), "잠긴 파일 뒤의 다른 만료 파일이 삭제되지 않음");
        }

        private string CreateLogFile(string fileName, int daysAgo)
        {
            string path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, "probe log line");
            File.SetLastWriteTime(path, DateTime.Now.AddDays(-daysAgo));
            return path;
        }
    }
}
