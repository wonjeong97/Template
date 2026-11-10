using NUnit.Framework;
using HuliacDev.Hardware;

namespace HuliacDev.Tests
{
    /// <summary>
    /// ArduinoManager 자동 재연결 간격 계산 검증. 시리얼 장치 없이 확인할 수 있는 순수 계산만 다룸.
    /// </summary>
    public class ArduinoManagerTests
    {
        /// <summary>
        /// 재연결 간격은 실패할 때마다 두 배로 늘고 상한에서 멈춰야 함. 고정 간격이면 장치가 오래 꺼져 있을 때
        /// 시도와 로그가 계속 몰리고, 상한이 없으면 장치가 돌아와도 한참 뒤에야 다시 연결됨.
        /// </summary>
        [Test]
        public void 재연결_간격은_두_배씩_늘고_상한에서_멈춘다()
        {
            Assert.AreEqual(6000, ArduinoManager.NextReconnectDelayMs(3000, 30000));
            Assert.AreEqual(24000, ArduinoManager.NextReconnectDelayMs(12000, 30000));
            Assert.AreEqual(30000, ArduinoManager.NextReconnectDelayMs(24000, 30000), "상한을 넘음");
            Assert.AreEqual(30000, ArduinoManager.NextReconnectDelayMs(30000, 30000), "상한에서 멈추지 않음");
        }

        /// <summary>
        /// 첫 간격이 0 이하면 1초부터 늘려야 함. 그대로 두 배 하면 계속 0이라 쉬지 않고 포트를 스캔함.
        /// 상한을 1초보다 작게(0 이하 포함) 잘못 설정해도 1초 아래로 내려가지 않아야 함.
        /// </summary>
        [Test]
        public void 간격은_1초_아래로_내려가지_않는다()
        {
            Assert.AreEqual(1000, ArduinoManager.NextReconnectDelayMs(0, 30000));
            Assert.AreEqual(1000, ArduinoManager.NextReconnectDelayMs(-1, 30000));
            Assert.AreEqual(1000, ArduinoManager.NextReconnectDelayMs(0, 0), "상한이 0이면 쉬지 않고 스캔함");
            Assert.AreEqual(1000, ArduinoManager.NextReconnectDelayMs(3000, 500), "상한이 1초보다 작으면 1초로 봐야 함");
        }
    }
}
