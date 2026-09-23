using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using HuliacDev.UI;

namespace HuliacDev.Tests
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

            await _fade.FadeOutAsync(0.1f).AwaitWithRealtimeTimeout();

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

            await _fade.FadeInAsync(0.1f).AwaitWithRealtimeTimeout();

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

            await _fade.FadeOutAsync(0.1f).AwaitWithRealtimeTimeout();

            Assert.AreEqual(1f, GetAlpha(), 0.001f);
            Assert.IsFalse(GetIsTransitioning());
        });

        /// <summary>
        /// 페이드인 완료(알파 0) 시 불필요한 GPU 오버드로우를 막기 위해 캔버스가 비활성화되는지 확인함.
        /// </summary>
        [UnityTest]
        public IEnumerator 페이드인_완료_시_오버드로우_방지를_위해_캔버스가_비활성화된다() => UniTask.ToCoroutine(async () =>
        {
            await _fade.FadeOutAsync(0.05f).AwaitWithRealtimeTimeout();
            Assert.IsTrue(GetCanvasEnabled(), "페이드아웃(alpha=1) 상태에서는 캔버스가 활성화되어 있어야 함");

            await _fade.FadeInAsync(0.05f).AwaitWithRealtimeTimeout();
            Assert.IsFalse(GetCanvasEnabled(), "페이드인 완료(alpha=0) 시 GPU 오버드로우 방지를 위해 캔버스가 비활성화되어야 함");
        });

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

        private bool GetCanvasEnabled()
        {
            Canvas canvas = _go.GetComponentInChildren<Canvas>(true);
            return canvas != null && canvas.enabled;
        }
    }
}
