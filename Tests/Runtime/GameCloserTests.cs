using NUnit.Framework;
using UnityEngine;
using HuliacDev.Data;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// GameCloser 설정 병합 규칙(CloseSettingResolver) 검증.
    ///
    /// 배경: JsonUtility는 "closeSetting" 키가 없어도 null 대신 기본 인스턴스를 만들고, JSON에 없는 필드에는
    /// 필드 초기값을 남김. 예전 CloseSetting은 초기값이 없어 빠진 필드가 0이 되었고, numToClose만 적어도
    /// 제한 시간·투명도·위치가 0으로 인스펙터 기본값을 덮어썼음. 이제 초기값을 "미지정" 표시값으로 두고,
    /// 표시값인 필드는 인스펙터 값을 유지함.
    /// </summary>
    public class GameCloserTests
    {
        private const int InspectorTargetClickCount = 10;
        private const float InspectorClickTimeWindow = 3f;

        /// <summary>
        /// 이 규칙의 전제인 JsonUtility 동작: 중첩 객체 안의 빠진 필드와, 중첩 객체 키 자체가 빠진 경우
        /// 모두 CloseSetting의 필드 초기값(표시값)이 유지되어야 함.
        /// </summary>
        [Test]
        public void JsonUtility는_빠진_필드에_CloseSetting_초기값을_남긴다()
        {
            Settings partial = JsonUtility.FromJson<Settings>("{\"closeSetting\":{\"numToClose\":5}}");
            Assert.AreEqual(-1f, partial.closeSetting.imageAlpha, "빠진 필드가 초기값을 유지하지 않음");
            Assert.AreEqual(-1f, partial.closeSetting.resetClickTime);
            Assert.AreEqual(new Vector2(-1f, -1f), partial.closeSetting.position);

            Settings missing = JsonUtility.FromJson<Settings>("{}");
            Assert.IsNotNull(missing.closeSetting, "키가 없어도 JsonUtility는 기본 인스턴스를 만듦");
            Assert.AreEqual(0, missing.closeSetting.numToClose);
            Assert.AreEqual(-1f, missing.closeSetting.imageAlpha);
        }

        /// <summary>
        /// closeSetting 키가 없으면 numToClose가 0이므로 설정 전체를 무시하고 인스펙터 값을 유지해야 함.
        /// </summary>
        [Test]
        public void closeSetting_키가_없으면_인스펙터_값을_유지한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>("{}");

            bool applied = Resolve(settings.closeSetting, out ResolvedCloseSetting resolved);

            Assert.IsFalse(applied, "numToClose가 없으면 설정을 적용하면 안 됨");
            Assert.AreEqual(InspectorTargetClickCount, resolved.TargetClickCount);
            Assert.AreEqual(InspectorClickTimeWindow, resolved.ClickTimeWindow);
            Assert.IsFalse(resolved.HasPosition);
            Assert.IsFalse(resolved.HasImageAlpha);
        }

        /// <summary>
        /// Settings 자체나 closeSetting이 null이어도 설정 전체를 무시하고 인스펙터 값을 유지해야 함.
        /// </summary>
        [Test]
        public void closeSetting이_null이면_인스펙터_값을_유지한다()
        {
            bool applied = Resolve(null, out ResolvedCloseSetting resolved);

            Assert.IsFalse(applied);
            Assert.AreEqual(InspectorTargetClickCount, resolved.TargetClickCount);
            Assert.AreEqual(InspectorClickTimeWindow, resolved.ClickTimeWindow);
        }

        /// <summary>
        /// numToClose만 지정하면 클릭 횟수만 바뀌고, 나머지(제한 시간·투명도·위치)는 인스펙터 값을 유지해야 함.
        /// 예전에는 빠진 필드가 0으로 덮어써졌음.
        /// </summary>
        [Test]
        public void numToClose만_지정하면_나머지는_인스펙터_값을_유지한다()
        {
            Settings settings = JsonUtility.FromJson<Settings>("{\"closeSetting\":{\"numToClose\":5}}");

            bool applied = Resolve(settings.closeSetting, out ResolvedCloseSetting resolved);

            Assert.IsTrue(applied);
            Assert.AreEqual(5, resolved.TargetClickCount);
            Assert.AreEqual(InspectorClickTimeWindow, resolved.ClickTimeWindow, "제한 시간이 0으로 덮어써짐");
            Assert.IsFalse(resolved.HasImageAlpha, "투명도가 0으로 덮어써짐");
            Assert.IsFalse(resolved.HasPosition, "위치가 (0,0)으로 덮어써짐");
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

            bool applied = Resolve(settings.closeSetting, out ResolvedCloseSetting resolved);

            Assert.IsTrue(applied);
            Assert.AreEqual(7, resolved.TargetClickCount);
            Assert.AreEqual(2.5f, resolved.ClickTimeWindow);
            Assert.IsTrue(resolved.HasImageAlpha, "투명도 0은 정상 값으로 적용되어야 함");
            Assert.AreEqual(0f, resolved.ImageAlpha);
            Assert.IsTrue(resolved.HasPosition, "위치 (0,0)은 정상 값으로 적용되어야 함");
            Assert.AreEqual(Vector2.zero, resolved.Position);
        }

        /// <summary>
        /// 위치는 두 성분이 모두 지정된 경우에만 적용함. 한 성분만 적으면 나머지가 표시값(-1)으로 남으므로
        /// 위치 전체를 미지정으로 보고 인스펙터 값을 유지해야 함.
        /// </summary>
        [Test]
        public void 위치의_한_성분만_지정하면_위치는_적용하지_않는다()
        {
            Settings settings = JsonUtility.FromJson<Settings>(
                "{\"closeSetting\":{\"position\":{\"x\":1},\"numToClose\":5}}");

            bool applied = Resolve(settings.closeSetting, out ResolvedCloseSetting resolved);

            Assert.IsTrue(applied);
            Assert.IsFalse(resolved.HasPosition, "한 성분만 지정된 위치가 적용됨");
        }

        /// <summary>
        /// 테스트용 인스펙터 값으로 CloseSettingResolver를 호출함.
        /// </summary>
        private static bool Resolve(CloseSetting setting, out ResolvedCloseSetting resolved)
        {
            return CloseSettingResolver.TryResolve(setting, InspectorTargetClickCount, InspectorClickTimeWindow, out resolved);
        }
    }
}
