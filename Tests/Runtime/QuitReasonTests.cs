using NUnit.Framework;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// 종료 사유와 종료 코드, 진행 중 표시(QuitReason) 검증.
    /// ApiManagerBase는 종료 로그를 보내려고 종료를 미뤘다가 QuitReason.ExitCode로 다시 끝내므로,
    /// 이 값이 틀리면 자동 재시작 감시가 다시 켤지 판단이 틀어짐.
    /// </summary>
    public class QuitReasonTests
    {
        [SetUp]
        public void SetUp()
        {
            QuitReason.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            QuitReason.Clear();
        }

        /// <summary>
        /// 종료 코드를 함께 적으면 그 값이 남고 종료 진행 중으로 표시돼야 함. 코드가 남지 않으면 ApiManagerBase가 0으로 끝내 감시가 다시 켜지 않음.
        /// </summary>
        [Test]
        public void 종료_코드를_함께_적으면_그_값이_남고_진행_중으로_표시된다()
        {
            QuitReason.Set("Monitor Reconnect", 75);

            Assert.AreEqual("Monitor Reconnect", QuitReason.Current);
            Assert.AreEqual(75, QuitReason.ExitCode);
            Assert.IsTrue(QuitReason.IsQuitRequested);
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

        /// <summary>
        /// 종료를 취소한 뒤 Clear하면 기본값으로 돌아가야 함. 75가 남으면 이후 Alt+F4로 끌 때 감시가 다시 켜고,
        /// 진행 중 표시가 남으면 재시작 요청이 계속 거절됨.
        /// </summary>
        [Test]
        public void 취소한_종료를_지우면_기본값으로_돌아간다()
        {
            QuitReason.Set("Monitor Reconnect", 75);

            QuitReason.Clear();

            Assert.AreEqual(QuitReason.User, QuitReason.Current);
            Assert.AreEqual(0, QuitReason.ExitCode);
            Assert.IsFalse(QuitReason.IsQuitRequested);
        }

        /// <summary>
        /// 사유를 적지 않은 종료(Alt+F4)도 ApiManagerBase가 미룰 때 진행 중으로 표시돼야 하며, 사유·코드는 기본값을 유지해야 함.
        /// </summary>
        [Test]
        public void 사유_없는_종료도_진행_중으로_표시하되_사유와_코드는_그대로다()
        {
            QuitReason.MarkQuitRequested();

            Assert.IsTrue(QuitReason.IsQuitRequested);
            Assert.AreEqual(QuitReason.User, QuitReason.Current);
            Assert.AreEqual(0, QuitReason.ExitCode);
        }
    }
}
