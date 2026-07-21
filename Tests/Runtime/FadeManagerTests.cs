using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wonjeong.UI;

namespace Wonjeong.Tests
{
    /// <summary>
    /// FadeManager가 Time.timeScale에 영향받지 않고 완료되는지 검증.
    ///
    /// 배경: 경과 시간을 Time.deltaTime으로 누적하던 구현은 timeScale이 0일 때
    /// deltaTime도 0이 되어 페이드가 영원히 끝나지 않았음. _isTransitioning이 true로
    /// 굳어 이후 모든 페이드 호출이 무시되고, raycastTarget이 켜진 채 남아
    /// 화면 전체 입력이 차단되는 소프트락이 발생했음.
    /// </summary>
    public class FadeManagerTests
    {
        private float _originalTimeScale;
        private GameObject _go;
        private FadeManager _fade;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            _go = new GameObject("FadeManagerTests");
            _fade = _go.AddComponent<FadeManager>();
        }

        [TearDown]
        public void TearDown()
        {
            // 다른 테스트에 영향을 주지 않도록 전역 상태를 반드시 복구함.
            Time.timeScale = _originalTimeScale;

            // using System; 이 있으면 Object가 System.Object와 모호해지므로 정규화함.
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        /// <summary>
        /// timeScale이 0이어도 페이드아웃이 완료되어야 함.
        /// 완료 판정은 알파값과 전환 플래그로 확인함.
        /// </summary>
        [UnityTest]
        public IEnumerator timeScale이_0이어도_페이드아웃이_완료된다() => UniTask.ToCoroutine(async () =>
        {
            Time.timeScale = 0f;

            await AwaitWithRealtimeTimeout(_fade.FadeOutAsync(0.1f));

            Assert.AreEqual(1f, GetAlpha(), 0.001f, "timeScale=0에서 페이드아웃이 완료되지 않음");
            Assert.IsFalse(GetIsTransitioning(), "_isTransitioning이 해제되지 않아 이후 페이드가 모두 무시됨");
        });

        /// <summary>
        /// timeScale이 0이어도 페이드인이 완료되고 입력 차단이 해제되어야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator timeScale이_0이어도_페이드인이_완료되고_입력차단이_해제된다() => UniTask.ToCoroutine(async () =>
        {
            Time.timeScale = 0f;

            await AwaitWithRealtimeTimeout(_fade.FadeInAsync(0.1f));

            Assert.AreEqual(0f, GetAlpha(), 0.001f, "timeScale=0에서 페이드인이 완료되지 않음");
            Assert.IsFalse(GetIsTransitioning(), "_isTransitioning이 해제되지 않음");
            Assert.IsFalse(GetRaycastTarget(), "raycastTarget이 켜진 채 남아 화면 입력이 차단됨");
        });

        /// <summary>
        /// 정상 시간에서도 기존 동작이 유지되는지 확인함(회귀 방지).
        /// </summary>
        [UnityTest]
        public IEnumerator timeScale이_1일때도_정상_완료된다() => UniTask.ToCoroutine(async () =>
        {
            Time.timeScale = 1f;

            await AwaitWithRealtimeTimeout(_fade.FadeOutAsync(0.1f));

            Assert.AreEqual(1f, GetAlpha(), 0.001f);
            Assert.IsFalse(GetIsTransitioning());
        });

        /// <summary>
        /// 페이드 완료를 기다리되, 실시간 기준 제한 시간을 넘기면 Assert 실패로 끝냄.
        /// <para>
        /// 이 가드가 없으면 결함이 있는 구현에서 테스트가 '실패'가 아니라 '무한 대기'로 멈춰
        /// 테스트 러너 전체를 막아버림. 실제로 수정 전 코드에서 이 현상을 확인했음.
        /// 대기 자체도 timeScale의 영향을 받으면 안 되므로 UnscaledDeltaTime을 사용함.
        /// </para>
        /// </summary>
        private static async UniTask AwaitWithRealtimeTimeout(UniTask task, float timeoutSeconds = 3f)
        {
            int finishedIndex = await UniTask.WhenAny(
                task,
                UniTask.Delay(TimeSpan.FromSeconds(timeoutSeconds), DelayType.UnscaledDeltaTime));

            if (finishedIndex != 0)
            {
                Assert.Fail($"제한 시간 {timeoutSeconds}초 내에 완료되지 않음. " +
                            "timeScale에 묶여 진행이 멈춘 것으로 보임.");
            }
        }

        private float GetAlpha()
        {
            CanvasGroup cg = _go.GetComponentInChildren<CanvasGroup>(true);
            return cg == null ? -1f : cg.alpha;
        }

        private bool GetIsTransitioning()
        {
            return (bool)typeof(FadeManager)
                .GetField("_isTransitioning",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(_fade);
        }

        private bool GetRaycastTarget()
        {
            UnityEngine.UI.RawImage img = _go.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
            return img != null && img.raycastTarget;
        }
    }
}
