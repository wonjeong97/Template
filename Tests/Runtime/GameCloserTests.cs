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
        /// 1초 미만의 양수 제한 시간은 사람이 제시간에 누를 수 없어 앱을 끌 수 없게 되므로,
        /// 최소값(1초)으로 올려 적용하고 문제로 표시해 경고를 남기게 해야 함.
        /// </summary>
        [Test]
        public void 제한_시간이_1초_미만이면_1초로_올리고_문제로_표시한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>("{\"closeSetting\":{\"numToClose\":5,\"resetClickTime\":0.003}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.AreEqual(CloseSettingResolver.MinClickTimeWindow, resolved.ClickTimeWindow.Value, 0.0001f);
            Assert.AreEqual(CloseSettingIssues.ResetClickTimeBelowMinimum, resolved.Issues);
        }

        /// <summary>
        /// 최소값(1초) 이상인 제한 시간은 경계값을 포함해 그대로 적용되어야 함.
        /// </summary>
        [Test]
        public void 제한_시간이_1초_이상이면_그대로_적용한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>("{\"closeSetting\":{\"numToClose\":5,\"resetClickTime\":1}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.AreEqual(1f, resolved.ClickTimeWindow.Value, 0.0001f);
            Assert.AreEqual(CloseSettingIssues.None, resolved.Issues);
        }

        /// <summary>
        /// 해석 결과의 클릭 횟수와 제한 시간이 최종 종료 조건이 되어야 하고,
        /// 제한 시간이 없으면 현재 값을 유지해야 함.
        /// </summary>
        [Test]
        public void 해석_결과의_클릭_횟수와_제한_시간을_최종_값으로_쓴다()
        {
            Assert.IsTrue(CloseSettingResolver.TryGetClickSettings(
                new ResolvedCloseSetting(7, 2.5f, null, null, CloseSettingIssues.None), 3f,
                out int target, out float window));
            Assert.AreEqual(7, target);
            Assert.AreEqual(2.5f, window, 0.0001f);

            Assert.IsTrue(CloseSettingResolver.TryGetClickSettings(
                new ResolvedCloseSetting(7, null, null, null, CloseSettingIssues.None), 3f,
                out int _, out float keptWindow));
            Assert.AreEqual(3f, keptWindow, 0.0001f, "제한 시간 미지정인데 현재 값이 바뀜");
        }

        /// <summary>
        /// TryResolve가 실패했을 때의 default 결과(클릭 횟수 0)는 거부되어야 함.
        /// 0이 적용되면 숨은 버튼을 한 번만 눌러도 앱이 종료됨.
        /// </summary>
        [Test]
        public void 클릭_횟수가_0인_결과는_거부한다()
        {
            Assert.IsFalse(CloseSettingResolver.TryGetClickSettings(default, 3f, out int _, out float _),
                "클릭 횟수 0 결과가 적용되어 한 번 클릭에 종료될 수 있음");
        }

        /// <summary>
        /// numToClose가 권장 최소값(3회)보다 작아도 설정 오류가 아니므로 그대로 적용해야 함.
        /// 오터치 경고는 JSON·인스펙터 구분 없이 최종 클릭 횟수 기준으로 GameCloser가 따로 남김.
        /// </summary>
        [Test]
        public void numToClose가_3회_미만이어도_그대로_적용한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>("{\"closeSetting\":{\"numToClose\":2}}");

            Assert.IsTrue(CloseSettingResolver.TryResolve(settings.closeSetting, out ResolvedCloseSetting resolved));
            Assert.AreEqual(2, resolved.TargetClickCount, "경고만 하고 값은 그대로 적용해야 함");
            Assert.AreEqual(CloseSettingIssues.None, resolved.Issues);
        }

        /// <summary>
        /// 최종 클릭 횟수가 권장 최소값(3회)보다 작으면 오터치 위험으로 판정해야 함.
        /// 값의 출처(JSON·인스펙터)와 관계없이 같은 기준을 씀.
        /// </summary>
        [Test]
        public void 최종_클릭_횟수가_3회_미만이면_오터치_위험으로_판정한다()
        {
            Assert.IsTrue(CloseSettingResolver.IsBelowRecommendedClickCount(1));
            Assert.IsTrue(CloseSettingResolver.IsBelowRecommendedClickCount(2));
            Assert.IsFalse(CloseSettingResolver.IsBelowRecommendedClickCount(3));
            Assert.IsFalse(CloseSettingResolver.IsBelowRecommendedClickCount(10));
        }

        /// <summary>
        /// [Min]은 인스펙터 편집 시에만 동작하므로, 이미 저장된 최소값 미만 값(클릭 0회, 0.5초 등)은
        /// 실행 시 최소값으로 올리고 문제로 표시해야 함.
        /// </summary>
        [Test]
        public void 인스펙터_값이_최소값_미만이면_최소값으로_올린다()
        {
            int targetClickCount = 0;
            float clickTimeWindow = 0.5f;

            InspectorValueCorrections corrections = CloseSettingResolver.ClampInspectorValues(ref targetClickCount, ref clickTimeWindow);

            Assert.AreEqual(CloseSettingResolver.MinClickCount, targetClickCount);
            Assert.AreEqual(CloseSettingResolver.MinClickTimeWindow, clickTimeWindow, 0.0001f);
            Assert.AreEqual(
                InspectorValueCorrections.ClickCountRaised | InspectorValueCorrections.ClickTimeWindowRaised,
                corrections);
        }

        /// <summary>
        /// 최소값 이상인 인스펙터 값은 바꾸지 않아야 함.
        /// </summary>
        [Test]
        public void 인스펙터_값이_최소값_이상이면_그대로_둔다()
        {
            int targetClickCount = 10;
            float clickTimeWindow = 3f;

            InspectorValueCorrections corrections = CloseSettingResolver.ClampInspectorValues(ref targetClickCount, ref clickTimeWindow);

            Assert.AreEqual(10, targetClickCount);
            Assert.AreEqual(3f, clickTimeWindow, 0.0001f);
            Assert.AreEqual(InspectorValueCorrections.None, corrections);
        }

        /// <summary>
        /// ApplyResolvedSettings가 해석된 클릭 횟수를 실제 종료 조건에 반영해야 함.
        /// 3회로 적용하면 두 번째 클릭까지는 종료되지 않고 세 번째 클릭에 종료되어야 함.
        /// </summary>
        [Test]
        public void 적용한_클릭_횟수만큼_눌러야_종료된다()
        {
            QuitRecordingGameCloser closer = CreateRecordingCloser(out Button button);

            closer.ApplyResolvedSettings(new ResolvedCloseSetting(3, 2f, null, null, CloseSettingIssues.None));

            button.onClick.Invoke();
            button.onClick.Invoke();
            Assert.AreEqual(0, closer.QuitCount, "적용한 클릭 횟수보다 먼저 종료됨");

            button.onClick.Invoke();
            Assert.AreEqual(1, closer.QuitCount, "적용한 클릭 횟수에 도달했는데 종료되지 않음(설정이 반영되지 않음)");
        }

        /// <summary>
        /// 해석 실패 시의 default 결과(클릭 횟수 0)를 적용해도 종료 조건이 바뀌지 않아,
        /// 한 번 클릭으로 종료되면 안 됨(인스펙터 기본값 10회 유지).
        /// </summary>
        [Test]
        public void 클릭_횟수_0_결과를_적용해도_한_번_클릭에_종료되지_않는다()
        {
            QuitRecordingGameCloser closer = CreateRecordingCloser(out Button button);

            closer.ApplyResolvedSettings(default);
            button.onClick.Invoke();

            Assert.AreEqual(0, closer.QuitCount, "클릭 횟수 0이 적용되어 한 번 클릭에 종료됨");
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

        /// <summary>
        /// 실제로 앱을 끄지 않고 종료 요청만 기록하는 GameCloser를 만듦(Button은 RequireComponent로 자동 추가).
        /// </summary>
        private QuitRecordingGameCloser CreateRecordingCloser(out Button button)
        {
            GameObject go = new GameObject("RecordingCloser", typeof(RectTransform));
            _spawned.Add(go);

            QuitRecordingGameCloser closer = go.AddComponent<QuitRecordingGameCloser>();
            Assert.IsTrue(go.TryGetComponent(out button), "Button이 자동으로 추가되어야 함");
            return closer;
        }
    }

    /// <summary>
    /// 에디터에서 플레이 모드를 끄는 실제 종료 대신 종료 요청 횟수만 기록하는 테스트용 GameCloser.
    /// </summary>
    internal class QuitRecordingGameCloser : GameCloser
    {
        public int QuitCount;

        /// <summary>
        /// 앱을 끄지 않고 종료 요청 횟수만 늘림.
        /// </summary>
        protected override void QuitApplication()
        {
            QuitCount++;
        }
    }
}
