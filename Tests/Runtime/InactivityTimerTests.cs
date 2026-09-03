using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wonjeong.App;
using Wonjeong.Core;

namespace Wonjeong.Tests
{
    /// <summary>
    /// InactivityTimer의 타임아웃 발동/미발동 조건 검증.
    ///
    /// Settings.json 비동기 로드(AppSettingsProvider/DI)를 거치지 않고, UIManagerTests와
    /// 동일하게 리플렉션으로 설정 로드 완료 상태(_isEnabled/_timeoutSeconds)를 직접
    /// 주입하여 파일 I/O·DI 컨테이너 없이 Update() 타임아웃 로직만 검증함.
    /// 메시지 파이프 퍼블리셔도 전체 DI 컨테이너 없이, Publish 시 콜백만 실행하는
    /// 최소 테스트 더블(FakePublisher)을 리플렉션으로 주입해 검증함.
    /// </summary>
    public class InactivityTimerTests
    {
        private static readonly BindingFlags Nonpublic = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _go;
        private InactivityTimer _timer;
        private bool _invoked;

        /// <summary> Publish 호출을 콜백으로만 전달하는 최소 IPublisher 테스트 더블. </summary>
        private class FakePublisher : IPublisher<InactivityTimeoutEvent>
        {
            private readonly Action _onPublish;

            public FakePublisher(Action onPublish)
            {
                _onPublish = onPublish;
            }

            public void Publish(InactivityTimeoutEvent message)
            {
                _onPublish();
            }
        }

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("InactivityTimerTests");
            _timer = _go.AddComponent<InactivityTimer>();
            _invoked = false;

            typeof(InactivityTimer).GetField("_publisher", Nonpublic)
                .SetValue(_timer, new FakePublisher(() => _invoked = true));
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        /// <summary>
        /// useInactivityTimer가 false인 경우(=_isEnabled 미설정)에는 시간이 아무리 지나도
        /// 이벤트가 발동하면 안 됨.
        /// </summary>
        [UnityTest]
        public IEnumerator 비활성_상태면_시간이_지나도_이벤트가_발동하지_않는다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingDependencyLog();
            SetTimeout(0.05f, isEnabled: false);

            await UniTask.Delay(TimeSpan.FromSeconds(0.2), DelayType.UnscaledDeltaTime);

            Assert.IsFalse(_invoked, "비활성 상태인데 타임아웃 이벤트가 발동함");
        });

        /// <summary>
        /// 활성 상태에서 설정된 시간만큼 입력이 없으면 이벤트가 정확히 한 번 발동해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 활성_상태에서_설정_시간이_지나면_이벤트가_한번_발동한다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingDependencyLog();
            SetTimeout(0.05f, isEnabled: true);

            await AwaitInvocation();

            Assert.IsTrue(_invoked, "제한 시간을 넘겼는데도 타임아웃 이벤트가 발동하지 않음");

            // 재입력(ResetTimer) 없이는 다시 발동하지 않아야 함(대기 화면에서 반복 발동 방지).
            _invoked = false;
            await UniTask.Delay(TimeSpan.FromSeconds(0.2), DelayType.UnscaledDeltaTime);
            Assert.IsFalse(_invoked, "타임아웃 이후 재입력 없이 이벤트가 다시 발동함");
        });

        /// <summary>
        /// 타임아웃 전에 ResetTimer(입력 감지 시뮬레이션)를 호출하면 발동이 그만큼 늦춰져야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 타임아웃_전_ResetTimer_호출시_발동이_늦춰진다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingDependencyLog();
            SetTimeout(0.15f, isEnabled: true);

            await UniTask.Delay(TimeSpan.FromSeconds(0.08), DelayType.UnscaledDeltaTime);
            _timer.ResetTimer();

            await UniTask.Delay(TimeSpan.FromSeconds(0.08), DelayType.UnscaledDeltaTime);
            Assert.IsFalse(_invoked, "ResetTimer 이후에도 원래 시간 기준으로 발동함");

            await AwaitInvocation();
            Assert.IsTrue(_invoked, "ResetTimer 이후 재설정된 시간이 지났는데도 발동하지 않음");
        });

        /// <summary>
        /// Pause() 중에는 시간이 아무리 지나도(=영상 재생처럼 긴 무입력 구간) 발동하면 안 됨.
        /// </summary>
        [UnityTest]
        public IEnumerator Pause_중에는_시간이_지나도_이벤트가_발동하지_않는다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingDependencyLog();
            SetTimeout(0.05f, isEnabled: true);
            _timer.Pause();

            await UniTask.Delay(TimeSpan.FromSeconds(0.2), DelayType.UnscaledDeltaTime);

            Assert.IsFalse(_invoked, "Pause 중인데 타임아웃 이벤트가 발동함");
        });

        /// <summary>
        /// Resume()은 정지 중 흐른 실제 시간을 그대로 반영하지 않고 재개 시점을 새 활동으로
        /// 취급해야 함. 그렇지 않으면 영상이 끝나자마자 곧바로 타임아웃되어 버림.
        /// </summary>
        [UnityTest]
        public IEnumerator Resume_직후에는_바로_발동하지_않고_전체_시간이_다시_주어진다() => UniTask.ToCoroutine(async () =>
        {
            ExpectMissingDependencyLog();
            SetTimeout(0.1f, isEnabled: true);
            _timer.Pause();

            // Pause 중 타임아웃 시간보다 훨씬 긴 시간이 흐름(긴 영상 재생 시뮬레이션).
            await UniTask.Delay(TimeSpan.FromSeconds(0.3), DelayType.UnscaledDeltaTime);

            _timer.Resume();

            await UniTask.Delay(TimeSpan.FromSeconds(0.05), DelayType.UnscaledDeltaTime);
            Assert.IsFalse(_invoked, "Resume 직후 곧바로 타임아웃됨 - 정지 중 흐른 시간이 그대로 반영된 것으로 보임");

            await AwaitInvocation();
            Assert.IsTrue(_invoked, "Resume 이후 재설정된 시간이 지났는데도 발동하지 않음");
        });

        /// <summary>
        /// DI 없이 AddComponent로 생성하므로 Construct()가 호출되지 않아 Start()에서
        /// "Dependencies were not injected" 폴백 에러 로그가 항상 발생함(정상 동작).
        /// [SetUp](동기 메서드)에서 기대하면 Start()가 아직 실행되지 않은 시점(프레임 경계 전)에
        /// 검증돼 "로그가 나타나지 않음"으로 실패하므로, 첫 await 전인 각 테스트 코루틴
        /// 시작부에서 기대하여 Test 스텝 안에서 검증되도록 함.
        /// </summary>
        private void ExpectMissingDependencyLog()
        {
            LogAssert.Expect(LogType.Error, new Regex("Dependencies were not injected"));
        }

        private void SetTimeout(float seconds, bool isEnabled)
        {
            typeof(InactivityTimer).GetField("_timeoutSeconds", Nonpublic).SetValue(_timer, seconds);
            typeof(InactivityTimer).GetField("_isEnabled", Nonpublic).SetValue(_timer, isEnabled);
            _timer.ResetTimer();
        }

        /// <summary>
        /// 발동을 폴링하되, 결함이 있는 구현에서 테스트가 무한 대기로 멈추지 않도록
        /// 실시간 기준 제한 시간을 둠(FadeManagerTests의 AwaitWithRealtimeTimeout과 동일한 이유).
        /// </summary>
        private async UniTask AwaitInvocation(float timeoutSeconds = 3f)
        {
            float start = Time.realtimeSinceStartup;

            while (!_invoked)
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds)
                {
                    Assert.Fail($"제한 시간 {timeoutSeconds}초 내에 타임아웃 이벤트가 발동하지 않음");
                }

                await UniTask.Yield();
            }
        }
    }
}
