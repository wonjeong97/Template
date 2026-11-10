using NUnit.Framework;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// 종료 사유와 종료 코드(QuitReason) 검증.
    /// ApiManagerBase는 종료 로그를 보내려고 종료를 미뤘다가 QuitReason.ExitCode로 다시 끝내므로,
    /// 이 값이 틀리면 자동 재시작 감시가 다시 켤지 판단이 틀어짐.
    /// </summary>
    public class QuitReasonTests
    {
        private string _reasonBefore;
        private int _exitCodeBefore;

        [SetUp]
        public void SetUp()
        {
            _reasonBefore = QuitReason.Current;
            _exitCodeBefore = QuitReason.ExitCode;
        }

        [TearDown]
        public void TearDown()
        {
            QuitReason.Set(_reasonBefore, _exitCodeBefore);
        }

        /// <summary>
        /// 종료 코드를 함께 적으면 그 값이 남아야 함. 남지 않으면 ApiManagerBase가 0으로 끝내 감시가 다시 켜지 않음.
        /// </summary>
        [Test]
        public void 종료_코드를_함께_적으면_그_값이_남는다()
        {
            QuitReason.Set("Monitor Reconnect", 75);

            Assert.AreEqual("Monitor Reconnect", QuitReason.Current);
            Assert.AreEqual(75, QuitReason.ExitCode);
        }

        /// <summary>
        /// 사유만 적는 경로(GameCloser·ShutdownScheduler)는 일부러 끄는 것이므로 앞서 적힌 종료 코드를 0으로 되돌려야 함.
        /// 남아 있으면 숨은 종료 버튼으로 끈 앱을 감시가 다시 켬.
        /// </summary>
        [Test]
        public void 사유만_적으면_종료_코드를_0으로_되돌린다()
        {
            QuitReason.Set("Monitor Reconnect", 75);

            QuitReason.Set(QuitReason.GameCloser);

            Assert.AreEqual(QuitReason.GameCloser, QuitReason.Current);
            Assert.AreEqual(0, QuitReason.ExitCode);
        }
    }
}
