using NUnit.Framework;
using HuliacDev.Network;

namespace HuliacDev.Tests
{
    /// <summary>
    /// 인터넷 연결 판정(InternetAccessJudge) 검증.
    /// 어댑터 연결 여부와 Windows 판정을 인자로 넘기는 순수 클래스라 Windows API 없이 [Test]로 검증함.
    /// </summary>
    public class InternetAccessJudgeTests
    {
        /// <summary>
        /// 어댑터가 끊기면 Windows 판정이나 판정 무시 상태와 무관하게 인터넷이 없다고 봐야 함.
        /// </summary>
        [Test]
        public void 어댑터가_끊기면_Windows_판정과_무관하게_인터넷이_없다고_본다()
        {
            InternetAccessJudge judge = new InternetAccessJudge();

            Assert.IsFalse(judge.Evaluate(false, InternetAccessHint.Internet));
            Assert.IsFalse(judge.Evaluate(false, InternetAccessHint.Unknown));

            judge.ReportServerReached(InternetAccessHint.NoInternet);
            Assert.IsFalse(judge.Evaluate(false, InternetAccessHint.NoInternet), "판정 무시 중이어도 어댑터가 끊기면 보낼 수 없음");
        }

        /// <summary>
        /// 어댑터는 연결됐지만 Windows가 인터넷 없음으로 판정하면 인터넷이 없다고 봐야 함.
        /// 예전에는 어댑터만 봐서 지구본 아이콘인데도 연결됨으로 판정했음.
        /// </summary>
        [Test]
        public void 어댑터가_연결돼도_Windows가_인터넷_없음으로_판정하면_인터넷이_없다고_본다()
        {
            InternetAccessJudge judge = new InternetAccessJudge();

            Assert.IsFalse(judge.Evaluate(true, InternetAccessHint.NoInternet));
            Assert.IsTrue(judge.Evaluate(true, InternetAccessHint.Internet));
        }

        /// <summary>
        /// Windows 판정을 읽을 수 없거나 아직 판정 전이면 전송을 막지 않아야 함.
        /// </summary>
        [Test]
        public void Windows_판정을_알_수_없으면_전송을_막지_않는다()
        {
            InternetAccessJudge judge = new InternetAccessJudge();

            Assert.IsTrue(judge.Evaluate(true, InternetAccessHint.Unknown));
        }

        /// <summary>
        /// Windows가 인터넷 없음으로 판정한 채 서버에 닿으면, 그 뒤로는 그 판정을 무시하고 어댑터 연결만 봐야 함.
        /// 무시를 새로 시작한 보고에서만 true를 반환해 로그가 한 번만 남게 함.
        /// </summary>
        [Test]
        public void 인터넷_없음_판정_중_서버에_닿으면_이후_그_판정을_무시한다()
        {
            InternetAccessJudge judge = new InternetAccessJudge();

            Assert.IsTrue(judge.ReportServerReached(InternetAccessHint.NoInternet), "무시를 새로 시작했는데 false를 반환함");
            Assert.IsTrue(judge.IsHintIgnored);
            Assert.IsTrue(judge.Evaluate(true, InternetAccessHint.NoInternet), "서버에 닿았는데 Windows 판정으로 계속 막음");
            Assert.IsFalse(judge.ReportServerReached(InternetAccessHint.NoInternet), "이미 무시 중인데 다시 true를 반환함");

            // 판정 전(Unknown)이 끼어도 무시 상태는 유지되어야 함. 풀리는 것은 Windows가 인터넷 연결됨이라고 할 때뿐임.
            Assert.IsTrue(judge.Evaluate(true, InternetAccessHint.Unknown));
            Assert.IsTrue(judge.IsHintIgnored, "Unknown 판정에서 무시 상태가 풀림");
        }

        /// <summary>
        /// 판정을 무시하던 중 Windows가 인터넷 연결됨으로 판정하면 다시 Windows 판정을 따라야 함.
        /// 부팅 직후 판정이 늦어 무시하게 된 경우, 이후 실제로 인터넷이 끊기면 다시 거를 수 있어야 하기 때문임.
        /// </summary>
        [Test]
        public void 무시하던_중_Windows가_인터넷_연결됨으로_판정하면_다시_그_판정을_따른다()
        {
            InternetAccessJudge judge = new InternetAccessJudge();
            judge.ReportServerReached(InternetAccessHint.NoInternet);

            Assert.IsTrue(judge.Evaluate(true, InternetAccessHint.Internet));
            Assert.IsFalse(judge.IsHintIgnored);
            Assert.IsFalse(judge.Evaluate(true, InternetAccessHint.NoInternet), "Windows가 다시 판정하기 시작했는데 무시 상태가 남음");
        }

        /// <summary>
        /// Windows가 인터넷 연결됨으로 판정할 때 서버에 닿은 것은 판정이 맞다는 뜻이므로 무시 상태가 되면 안 됨.
        /// 그 뒤 실제로 인터넷이 끊기면 다시 걸러야 함.
        /// </summary>
        [Test]
        public void 인터넷_연결됨_판정_중_서버에_닿으면_무시_상태가_되지_않는다()
        {
            InternetAccessJudge judge = new InternetAccessJudge();

            Assert.IsFalse(judge.ReportServerReached(InternetAccessHint.Internet));
            Assert.IsFalse(judge.IsHintIgnored);
            Assert.IsFalse(judge.Evaluate(true, InternetAccessHint.NoInternet));
        }

        /// <summary>
        /// 부팅 직후처럼 Windows가 판정하기 전에 서버에 닿고 나서 인터넷 없음으로 판정되면(같은 LAN 안 서버로만 보내는 현장)
        /// 그 판정을 무시해야 함. 정상 망이면 Windows가 인터넷 연결됨으로 판정하는 순간 무시 상태가 풀려야 함.
        /// </summary>
        [Test]
        public void 판정_전에_서버에_닿으면_이후_인터넷_없음_판정을_무시하고_인터넷_연결됨이면_푼다()
        {
            InternetAccessJudge lanOnlyJudge = new InternetAccessJudge();
            Assert.IsTrue(lanOnlyJudge.ReportServerReached(InternetAccessHint.Unknown), "판정 전에 서버에 닿았는데 무시를 시작하지 않음");
            Assert.IsTrue(lanOnlyJudge.Evaluate(true, InternetAccessHint.NoInternet), "판정 전에 서버에 닿은 망에서 이후 인터넷 없음 판정으로 막음");

            InternetAccessJudge normalJudge = new InternetAccessJudge();
            normalJudge.ReportServerReached(InternetAccessHint.Unknown);
            Assert.IsTrue(normalJudge.Evaluate(true, InternetAccessHint.Internet));
            Assert.IsFalse(normalJudge.Evaluate(true, InternetAccessHint.NoInternet), "정상 망에서 인터넷 연결됨을 본 뒤에도 무시 상태가 남음");
        }

        /// <summary>
        /// Windows의 연결 수준 값(NL_NETWORK_CONNECTIVITY_LEVEL_HINT)을 세 갈래로 바꿔야 함.
        /// 연결 없음·로컬만·캡티브 포털은 인터넷 없음, 알 수 없음·숨은 인터페이스·모르는 값은 알 수 없음.
        /// </summary>
        [Test]
        public void Windows_연결_수준_값을_세_갈래로_바꾼다()
        {
            Assert.AreEqual(InternetAccessHint.Unknown, InternetAccessJudge.FromWindowsLevel(0), "Unknown");
            Assert.AreEqual(InternetAccessHint.NoInternet, InternetAccessJudge.FromWindowsLevel(1), "None");
            Assert.AreEqual(InternetAccessHint.NoInternet, InternetAccessJudge.FromWindowsLevel(2), "LocalAccess");
            Assert.AreEqual(InternetAccessHint.Internet, InternetAccessJudge.FromWindowsLevel(3), "InternetAccess");
            Assert.AreEqual(InternetAccessHint.NoInternet, InternetAccessJudge.FromWindowsLevel(4), "ConstrainedInternetAccess");
            Assert.AreEqual(InternetAccessHint.Unknown, InternetAccessJudge.FromWindowsLevel(5), "Hidden");
            Assert.AreEqual(InternetAccessHint.Unknown, InternetAccessJudge.FromWindowsLevel(99), "모르는 값");
        }
    }
}
