using NUnit.Framework;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// 연속 클릭 판정(ConsecutiveClickCounter) 검증.
    /// 시각을 인자로 넘기는 순수 클래스라 프레임 진행 없이 [Test]로 검증함.
    /// </summary>
    public class ConsecutiveClickCounterTests
    {
        private const int Target = 3;
        private const float Window = 2f;

        /// <summary>
        /// 제한 시간 안에 목표 횟수만큼 누르면 마지막 클릭에서만 true를 반환해야 함.
        /// </summary>
        [Test]
        public void 제한_시간_안에_목표_횟수만큼_누르면_마지막_클릭에서_true를_반환한다()
        {
            ConsecutiveClickCounter counter = new ConsecutiveClickCounter();

            Assert.IsFalse(counter.RegisterClick(10f, Target, Window));
            Assert.IsFalse(counter.RegisterClick(10.5f, Target, Window));
            Assert.IsTrue(counter.RegisterClick(11f, Target, Window), "목표 횟수에 도달했는데 true가 아님");
            Assert.AreEqual(Target, counter.ClickCount, "도달한 클릭 직후에는 도달한 횟수를 유지해야 함");
        }

        /// <summary>
        /// 첫 클릭부터 제한 시간이 지난 뒤의 클릭은 이전 클릭을 버리고 새 1회차로 세야 함.
        /// </summary>
        [Test]
        public void 제한_시간이_지나면_방금_클릭을_새_1회차로_센다()
        {
            ConsecutiveClickCounter counter = new ConsecutiveClickCounter();
            counter.RegisterClick(10f, Target, Window);
            counter.RegisterClick(11f, Target, Window);

            Assert.IsFalse(counter.RegisterClick(12.1f, Target, Window), "제한 시간이 지났는데 이전 클릭과 합쳐 도달로 판정함");
            Assert.AreEqual(1, counter.ClickCount);
            Assert.AreEqual(12.1f, counter.FirstClickTime, 0.0001f, "새 1회차의 시각으로 바뀌지 않음");
        }

        /// <summary>
        /// 정확히 제한 시간째에 누른 클릭은 같은 연속 클릭으로 인정해야 함(GameCloser 기존 판정과 같음).
        /// </summary>
        [Test]
        public void 정확히_제한_시간째_클릭은_인정한다()
        {
            ConsecutiveClickCounter counter = new ConsecutiveClickCounter();
            counter.RegisterClick(10f, Target, Window);
            counter.RegisterClick(11f, Target, Window);

            Assert.IsTrue(counter.RegisterClick(12f, Target, Window));
        }

        /// <summary>
        /// 목표에 도달한 다음 클릭은 1회차부터 다시 세야 함. 그러지 않으면 한 번 더 누를 때마다 바로 동작함.
        /// </summary>
        [Test]
        public void 목표에_도달한_다음_클릭은_1회차부터_다시_센다()
        {
            ConsecutiveClickCounter counter = new ConsecutiveClickCounter();
            counter.RegisterClick(10f, Target, Window);
            counter.RegisterClick(10.2f, Target, Window);
            counter.RegisterClick(10.4f, Target, Window);

            Assert.IsFalse(counter.RegisterClick(10.6f, Target, Window), "도달 직후 한 번 더 눌렀는데 다시 동작함");
            Assert.AreEqual(1, counter.ClickCount);
        }

        /// <summary>
        /// 앱 시작 직후(시각이 제한 시간보다 작음)의 첫 클릭도 그 시각을 첫 클릭 시각으로 기록해야 함.
        /// 초기 첫 클릭 시각 0을 기준으로 세면 앱 시작 몇 초 안의 클릭이 엉뚱한 기준으로 판정됨.
        /// </summary>
        [Test]
        public void 첫_클릭은_그_시각을_첫_클릭_시각으로_기록한다()
        {
            ConsecutiveClickCounter counter = new ConsecutiveClickCounter();

            counter.RegisterClick(1.5f, Target, Window);

            Assert.AreEqual(1, counter.ClickCount);
            Assert.AreEqual(1.5f, counter.FirstClickTime, 0.0001f);
        }
    }
}
