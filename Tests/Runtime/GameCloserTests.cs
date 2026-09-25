using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using HuliacDev.Data;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// GameCloser 설정 해석 규칙(CloseSettingResolver)과 컴포넌트 적용(ApplyResolvedSettings) 검증.
    ///
    /// 배경: JsonUtility는 "closeSetting" 키가 없어도 null 대신 기본 인스턴스를 만들고, JSON에 없는 필드에는
    /// 필드 초기값을 남김. 예전 CloseSetting은 초기값이 없어 빠진 필드가 0이 되었고, numToClose만 적어도
    /// 제한 시간·투명도·위치가 0으로 인스펙터 기본값을 덮어썼음. 이제 초기값을 "미지정" 표시값(-1)으로 두고,
    /// 미지정이거나 잘못 지정된 필드는 적용하지 않음.
    /// </summary>
    public class GameCloserTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go) Object.DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        /// <summary>
        /// 이 규칙의 전제인 JsonUtility 동작: 중첩 객체 안의 빠진 필드와, 중첩 객체 키 자체가 빠진 경우
        /// 모두 CloseSetting의 필드 초기값(표시값)이 유지되어야 함.
        /// </summary>
        [Test]
        public void JsonUtility는_빠진_필드에_CloseSetting_초기값을_남긴다()
        {
            Settings partial = JsonUtility.FromJson<Settings>("{\"closeSetting\":{\"numToClose\":5}}");
            Assert.AreEqual(CloseSetting.UnsetValue, partial.closeSetting.imageAlpha, "빠진 필드가 초기값을 유지하지 않음");
            Assert.AreEqual(CloseSetting.UnsetValue, partial.closeSetting.resetClickTime);
            Assert.AreEqual(new Vector2(CloseSetting.UnsetValue, CloseSetting.UnsetValue), partial.closeSetting.position);

            Settings missing = JsonUtility.FromJson<Settings>("{}");
            Assert.IsNotNull(missing.closeSetting, "키가 없어도 JsonUtility는 기본 인스턴스를 만듦");
            Assert.AreEqual(0, missing.closeSetting.numToClose);
            Assert.AreEqual(CloseSetting.UnsetValue, missing.closeSetting.imageAlpha);
        }

        /// <summary>
        /// closeSetting 키가 없으면 numToClose가 0이므로 설정 전체를 무시해야 함(GameCloser는 인스펙터 값 유지).
        /// </summary>
        [Test]
        public void closeSetting_키가_없으면_설정_전체를_무시한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>("{}");

            Assert.IsFalse(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting _),
                "numToClose가 없으면 설정을 적용하면 안 됨");
        }

        /// <summary>
        /// closeSetting이 null이면 설정 전체를 무시해야 함.
        /// GameCloser는 Settings 자체가 null일 때도 settings?.closeSetting으로 이 경로에 null을 넘김.
        /// </summary>
        [Test]
        public void closeSetting이_null이면_설정_전체를_무시한다()
        {
            Assert.IsFalse(CloseSettingResolver.TryResolve(null, out ResolvedCloseSetting _));
        }

        /// <summary>
        /// numToClose만 지정하면 클릭 횟수만 채워지고, 나머지(제한 시간·투명도·위치)는 값이 없어야 함.
        /// 미지정은 설정 실수가 아니므로 문제로 표시하지 않음. 예전에는 빠진 필드가 0으로 덮어써졌음.
        /// </summary>
        [Test]
        public void numToClose만_지정하면_나머지는_미지정으로_남는다()
        {
            Settings settings = JsonUtility.FromJson<Settings>("{\"closeSetting\":{\"numToClose\":5}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.AreEqual(5, resolved.TargetClickCount);
            Assert.IsFalse(resolved.ClickTimeWindow.HasValue, "제한 시간이 0으로 덮어써짐");
            Assert.IsFalse(resolved.ImageAlpha.HasValue, "투명도가 0으로 덮어써짐");
            Assert.IsFalse(resolved.Position.HasValue, "위치가 (0,0)으로 덮어써짐");
            Assert.AreEqual(CloseSettingIssues.None, resolved.Issues);
        }

        /// <summary>
        /// 모든 필드를 지정하면 전부 적용되어야 함. 0은 표시값이 아니라 정상 값이므로
        /// 투명도 0(완전히 투명한 숨은 버튼)과 위치 (0,0)(좌하단)도 그대로 적용되어야 함.
        /// 기존 설정 파일처럼 모든 필드를 적은 경우 결과가 바뀌지 않음을 보장함.
        /// </summary>
        [Test]
        public void 모든_필드를_지정하면_0을_포함해_전부_적용된다()
        {
            Settings settings = JsonUtility.FromJson<Settings>(
                "{\"closeSetting\":{\"position\":{\"x\":0,\"y\":0},\"numToClose\":7,\"resetClickTime\":2.5,\"imageAlpha\":0}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.AreEqual(7, resolved.TargetClickCount);
            Assert.AreEqual(2.5f, resolved.ClickTimeWindow.Value, 0.0001f);
            Assert.AreEqual(0f, resolved.ImageAlpha.Value, 0.0001f, "투명도 0은 정상 값으로 적용되어야 함");
            Assert.AreEqual(Vector2.zero, resolved.Position.Value, "위치 (0,0)은 정상 값으로 적용되어야 함");
            Assert.AreEqual(CloseSettingIssues.None, resolved.Issues);
        }

        /// <summary>
        /// 위치의 한 성분만 적으면 나머지가 표시값(-1)으로 남으므로 위치를 적용하지 않고,
        /// 설정 실수로 표시해 GameCloser가 경고를 남기게 해야 함.
        /// </summary>
        [Test]
        public void 위치의_한_성분만_지정하면_적용하지_않고_문제로_표시한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>(
                "{\"closeSetting\":{\"position\":{\"x\":1},\"numToClose\":5}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.IsFalse(resolved.Position.HasValue, "한 성분만 지정된 위치가 적용됨");
            Assert.AreEqual(CloseSettingIssues.PartialPosition, resolved.Issues);
        }

        /// <summary>
        /// 명시했지만 잘못된 값(제한 시간 0 이하, 투명도·위치가 0~1 밖)은 적용하지 않고 각각 문제로 표시해야 함.
        /// </summary>
        [Test]
        public void 잘못된_값은_적용하지_않고_문제로_표시한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>(
                "{\"closeSetting\":{\"position\":{\"x\":2,\"y\":0.5},\"numToClose\":5,\"resetClickTime\":0,\"imageAlpha\":1.5}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.IsFalse(resolved.ClickTimeWindow.HasValue);
            Assert.IsFalse(resolved.ImageAlpha.HasValue);
            Assert.IsFalse(resolved.Position.HasValue);
            Assert.AreEqual(
                CloseSettingIssues.InvalidResetClickTime | CloseSettingIssues.ImageAlphaOutOfRange | CloseSettingIssues.PositionOutOfRange,
                resolved.Issues);
        }

        /// <summary>
        /// 표시값(-1)이 아닌 음수를 명시하면 미지정이 아니라 잘못된 값이므로, 조용히 넘기지 않고 문제로 표시해야 함.
        /// </summary>
        [Test]
        public void 표시값이_아닌_음수는_잘못된_값으로_표시한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>(
                "{\"closeSetting\":{\"position\":{\"x\":-0.5,\"y\":0.5},\"numToClose\":5,\"resetClickTime\":-3,\"imageAlpha\":-0.5}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.IsFalse(resolved.ClickTimeWindow.HasValue);
            Assert.IsFalse(resolved.ImageAlpha.HasValue);
            Assert.IsFalse(resolved.Position.HasValue);
            Assert.AreEqual(
                CloseSettingIssues.InvalidResetClickTime | CloseSettingIssues.ImageAlphaOutOfRange | CloseSettingIssues.PositionOutOfRange,
                resolved.Issues);
        }

        /// <summary>
        /// 설계 한계 고정: JSON에 표시값(-1)을 직접 적으면 미지정과 구분되지 않아, 경고 없이 미지정으로 처리됨.
        /// </summary>
        [Test]
        public void 표시값과_같은_값을_직접_적으면_미지정으로_처리된다()
        {
            Settings settings = JsonUtility.FromJson<Settings>(
                "{\"closeSetting\":{\"position\":{\"x\":-1,\"y\":-1},\"numToClose\":5,\"resetClickTime\":-1,\"imageAlpha\":-1}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.IsFalse(resolved.ClickTimeWindow.HasValue);
            Assert.IsFalse(resolved.ImageAlpha.HasValue);
            Assert.IsFalse(resolved.Position.HasValue);
            Assert.AreEqual(CloseSettingIssues.None, resolved.Issues);
        }

        /// <summary>
        /// 클릭 횟수와 제한 시간이 있는 결과를 적용하면 GameCloser의 종료 조건이 그 값으로 바뀌어야 함.
        /// </summary>
        [Test]
        public void 해석_결과의_클릭_횟수와_제한_시간을_적용한다()
        {
            GameCloser closer = CreateCloser(out RectTransform _, out Image _);

            closer.ApplyResolvedSettings(new ResolvedCloseSetting(5, 2.5f, null, null, CloseSettingIssues.None));

            Assert.AreEqual(5, closer.TargetClickCount);
            Assert.AreEqual(2.5f, closer.ClickTimeWindow, 0.0001f);
        }

        /// <summary>
        /// TryResolve가 실패했을 때의 default 결과(클릭 횟수 0)를 적용해도 종료 조건이 바뀌면 안 됨.
        /// 0이 들어가면 숨은 버튼을 한 번만 눌러도 앱이 종료됨.
        /// </summary>
        [Test]
        public void 클릭_횟수가_0인_결과는_적용하지_않는다()
        {
            GameCloser closer = CreateCloser(out RectTransform _, out Image _);
            int originalTarget = closer.TargetClickCount;
            float originalWindow = closer.ClickTimeWindow;

            closer.ApplyResolvedSettings(default);

            Assert.AreEqual(originalTarget, closer.TargetClickCount, "클릭 횟수 0이 적용되어 한 번 클릭에 종료될 수 있음");
            Assert.AreEqual(originalWindow, closer.ClickTimeWindow, 0.0001f);
            Assert.Greater(closer.TargetClickCount, 0);
        }

        /// <summary>
        /// 위치가 없고 투명도만 있으면, RectTransform은 그대로 두고 Image 투명도만 바꿔야 함.
        /// </summary>
        [Test]
        public void 위치가_없으면_RectTransform은_그대로_두고_투명도만_적용한다()
        {
            GameCloser closer = CreateCloser(out RectTransform rt, out Image img);

            closer.ApplyResolvedSettings(new ResolvedCloseSetting(5, null, 0f, null, CloseSettingIssues.None));

            Assert.AreEqual(new Vector2(0.5f, 0.5f), rt.anchorMin, "위치 미지정인데 앵커가 바뀜");
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rt.pivot);
            Assert.AreEqual(0f, img.color.a, 0.0001f, "투명도가 적용되지 않음");
            Assert.AreEqual(Color.red.r, img.color.r, 0.0001f, "투명도 외 색상이 바뀜");
        }

        /// <summary>
        /// 위치가 있고 투명도가 없으면, 앵커·피벗을 위치로 맞추고 Image 투명도는 그대로 두어야 함.
        /// </summary>
        [Test]
        public void 투명도가_없으면_Image는_그대로_두고_위치만_적용한다()
        {
            GameCloser closer = CreateCloser(out RectTransform rt, out Image img);

            closer.ApplyResolvedSettings(new ResolvedCloseSetting(5, null, null, new Vector2(1f, 1f), CloseSettingIssues.None));

            Assert.AreEqual(Vector2.one, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(Vector2.one, rt.pivot);
            Assert.AreEqual(Vector2.zero, rt.anchoredPosition);
            Assert.AreEqual(1f, img.color.a, 0.0001f, "투명도 미지정인데 바뀜");
        }

        /// <summary>
        /// Image가 없는 버튼에 투명도가 지정돼도 예외 없이 건너뛰어야 함(경고는 로거가 있을 때 남음).
        /// </summary>
        [Test]
        public void Image가_없어도_투명도_적용에서_예외가_발생하지_않는다()
        {
            GameObject go = new GameObject("CloserWithoutImage", typeof(RectTransform));
            _spawned.Add(go);
            GameCloser closer = go.AddComponent<GameCloser>();

            Assert.DoesNotThrow(() =>
                closer.ApplyResolvedSettings(new ResolvedCloseSetting(5, null, 0f, null, CloseSettingIssues.None)));
        }

        /// <summary>
        /// 앵커·피벗이 가운데이고 빨간 불투명 Image를 가진 GameCloser를 만듦(Button은 RequireComponent로 자동 추가).
        /// </summary>
        private GameCloser CreateCloser(out RectTransform rt, out Image img)
        {
            GameObject go = new GameObject("Closer", typeof(RectTransform));
            _spawned.Add(go);

            rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            img = go.AddComponent<Image>();
            img.color = Color.red;

            return go.AddComponent<GameCloser>();
        }
    }
}
