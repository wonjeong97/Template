using System.Collections.Generic;
using NUnit.Framework;
using R3;
using UnityEngine;
using HuliacDev.Network;

namespace HuliacDev.Tests
{
    public class NetworkStatusServiceTests
    {
        [Test]
        public void 초기_상태_속성이_정상적으로_초기화된다()
        {
            var service = new NetworkStatusService(null, 1.0f);
            service.Initialize();

            Assert.AreEqual(Application.internetReachability, service.CurrentReachability);
            Assert.AreEqual(Application.internetReachability != NetworkReachability.NotReachable, service.IsConnected);

            service.Dispose();
        }

        [Test]
        public void 틱_호출_시_구독_스트림이_예외없이_동작한다()
        {
            var service = new NetworkStatusService(null, 0.2f);
            service.Initialize();

            bool lostFired = false;
            bool restoredFired = false;
            var reachabilities = new List<NetworkReachability>();

            using (service.OnNetworkLost.Subscribe(_ => lostFired = true))
            using (service.OnNetworkRestored.Subscribe(_ => restoredFired = true))
            using (service.OnReachabilityChanged.Subscribe(r => reachabilities.Add(r)))
            {
                // 같은 상태 유지 시 이벤트 미발행 검증
                service.Tick();
                Assert.IsFalse(lostFired);
                Assert.IsFalse(restoredFired);
                Assert.AreEqual(0, reachabilities.Count);
            }

            service.Dispose();
        }
    }
}
