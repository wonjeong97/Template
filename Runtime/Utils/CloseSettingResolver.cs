using System;
using UnityEngine;
using HuliacDev.Data;

namespace HuliacDev.Utils
{
    /// <summary>
    /// CloseSetting을 해석하다 발견한 설정 문제. 해당 필드는 적용하지 않고, 호출부가 경고로 남김.
    /// </summary>
    [Flags]
    internal enum CloseSettingIssues
    {
        None = 0,

        /// <summary>resetClickTime을 적었지만 0 이하임.</summary>
        InvalidResetClickTime = 1 << 0,

        /// <summary>imageAlpha를 적었지만 0~1 범위를 벗어남.</summary>
        ImageAlphaOutOfRange = 1 << 1,

        /// <summary>position의 x·y 중 한 성분만 적음.</summary>
        PartialPosition = 1 << 2,

        /// <summary>position을 적었지만 0~1 정규화 범위를 벗어남.</summary>
        PositionOutOfRange = 1 << 3
    }

    /// <summary>
    /// CloseSetting에서 GameCloser에 적용할 값만 골라낸 결과.
    /// 값이 null인 필드는 JSON에서 지정되지 않았거나 잘못 지정된 것이므로, 호출부가 기존(인스펙터) 값을 유지함.
    /// </summary>
    internal readonly struct ResolvedCloseSetting
    {
        public readonly Vector2? Position;
        public readonly float? ClickTimeWindow;
        public readonly float? ImageAlpha;
        public readonly int TargetClickCount;
        public readonly CloseSettingIssues Issues;

        /// <summary>
        /// 해석이 끝난 값들로 결과를 구성함.
        /// </summary>
        public ResolvedCloseSetting(int targetClickCount, float? clickTimeWindow, float? imageAlpha,
            Vector2? position, CloseSettingIssues issues)
        {
            Position = position;
            ClickTimeWindow = clickTimeWindow;
            ImageAlpha = imageAlpha;
            TargetClickCount = targetClickCount;
            Issues = issues;
        }
    }

    /// <summary>
    /// GameCloser 설정 해석 규칙을 담은 순수 계산 유틸리티.
    /// Unity 오브젝트나 AppSettingsProvider, 인스펙터 값에 의존하지 않아 설정 파일 없이도 규칙을 단위 테스트할 수 있음.
    /// </summary>
    internal static class CloseSettingResolver
    {
        /// <summary>
        /// numToClose가 양수가 아니면(closeSetting 키 누락 포함) 설정 전체를 무시하도록 false를 반환함.
        /// 그 외에는 JSON에서 올바르게 지정된 필드만 값으로 채우고, 미지정(표시값)이거나 잘못된 필드는 null로 둠.
        /// 잘못된 필드(범위 밖 값, 위치 한 성분만 지정)는 Issues에 표시해 호출부가 경고를 남기게 함.
        /// </summary>
        public static bool TryResolve(CloseSetting setting, out ResolvedCloseSetting resolved)
        {
            if (setting == null || setting.numToClose <= 0)
            {
                resolved = default;
                return false;
            }

            CloseSettingIssues issues = CloseSettingIssues.None;
            float? clickTimeWindow = ResolveResetClickTime(setting.resetClickTime, ref issues);
            float? imageAlpha = ResolveImageAlpha(setting.imageAlpha, ref issues);
            Vector2? position = ResolvePosition(setting.position, ref issues);

            resolved = new ResolvedCloseSetting(setting.numToClose, clickTimeWindow, imageAlpha, position, issues);
            return true;
        }

        /// <summary>
        /// 제한 시간이 양수면 그 값을, 미지정이면 null을 반환함. 0 이하를 명시했으면 null과 함께 문제로 표시함.
        /// </summary>
        private static float? ResolveResetClickTime(float value, ref CloseSettingIssues issues)
        {
            if (IsUnset(value)) return null;
            if (value > 0f) return value;

            issues |= CloseSettingIssues.InvalidResetClickTime;
            return null;
        }

        /// <summary>
        /// 투명도가 0~1이면 그 값을, 미지정이면 null을 반환함. 범위를 벗어나면 null과 함께 문제로 표시함.
        /// 0은 완전히 투명한 숨은 버튼을 뜻하는 정상 값임.
        /// </summary>
        private static float? ResolveImageAlpha(float value, ref CloseSettingIssues issues)
        {
            if (IsUnset(value)) return null;
            if (IsNormalized(value)) return value;

            issues |= CloseSettingIssues.ImageAlphaOutOfRange;
            return null;
        }

        /// <summary>
        /// 두 성분이 모두 0~1이면 그 위치를, 둘 다 미지정이면 null을 반환함.
        /// 한 성분만 지정했거나 범위를 벗어나면 null과 함께 문제로 표시함(부분 적용은 하지 않음).
        /// </summary>
        private static Vector2? ResolvePosition(Vector2 value, ref CloseSettingIssues issues)
        {
            bool isXUnset = IsUnset(value.x);
            bool isYUnset = IsUnset(value.y);

            if (isXUnset && isYUnset) return null;

            if (isXUnset || isYUnset)
            {
                issues |= CloseSettingIssues.PartialPosition;
                return null;
            }

            if (IsNormalized(value.x) && IsNormalized(value.y)) return value;

            issues |= CloseSettingIssues.PositionOutOfRange;
            return null;
        }

        /// <summary>
        /// 값이 "미지정" 표시값인지 확인함. JSON에 적지 않은 필드에는 초기값 -1이 그대로 남음.
        /// </summary>
        private static bool IsUnset(float value)
        {
            return Mathf.Approximately(value, CloseSetting.UnsetValue);
        }

        /// <summary>
        /// 값이 0~1 정규화 범위 안인지 확인함.
        /// </summary>
        private static bool IsNormalized(float value)
        {
            return value >= 0f && value <= 1f;
        }
    }
}
