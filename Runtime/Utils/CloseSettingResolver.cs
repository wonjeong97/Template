using UnityEngine;
using HuliacDev.Data;

namespace HuliacDev.Utils
{
    /// <summary>
    /// CloseSetting과 현재 인스펙터 값을 합쳐 GameCloser에 최종 적용할 값.
    /// 위치·투명도는 JSON에서 지정된 경우에만 Has 플래그가 켜지며, 꺼져 있으면 호출부가 기존 값을 건드리지 않음.
    /// </summary>
    internal readonly struct ResolvedCloseSetting
    {
        public readonly Vector2 Position;
        public readonly float ClickTimeWindow;
        public readonly float ImageAlpha;
        public readonly int TargetClickCount;
        public readonly bool HasPosition;
        public readonly bool HasImageAlpha;

        /// <summary>
        /// 계산이 끝난 값들로 결과를 구성함.
        /// </summary>
        public ResolvedCloseSetting(int targetClickCount, float clickTimeWindow,
            bool hasImageAlpha, float imageAlpha, bool hasPosition, Vector2 position)
        {
            Position = position;
            ClickTimeWindow = clickTimeWindow;
            ImageAlpha = imageAlpha;
            TargetClickCount = targetClickCount;
            HasPosition = hasPosition;
            HasImageAlpha = hasImageAlpha;
        }
    }

    /// <summary>
    /// GameCloser 설정 병합 규칙을 담은 순수 계산 유틸리티.
    /// Unity 오브젝트나 AppSettingsProvider에 의존하지 않아, 설정 파일 없이도 규칙을 단위 테스트할 수 있음.
    /// </summary>
    internal static class CloseSettingResolver
    {
        /// <summary>
        /// JSON에서 지정된 필드만 반영하고, 미지정(표시값) 필드는 현재 인스펙터 값을 유지한 결과를 계산함.
        /// numToClose가 양수가 아니면(closeSetting 키 누락 포함) 설정 전체를 무시하도록 false를 반환하며,
        /// 이때 resolved는 인스펙터 값 그대로임. 위치는 두 성분이 모두 지정(0 이상)된 경우에만 적용함.
        /// </summary>
        public static bool TryResolve(CloseSetting setting, int currentTargetClickCount, float currentClickTimeWindow,
            out ResolvedCloseSetting resolved)
        {
            if (setting == null || setting.numToClose <= 0)
            {
                resolved = new ResolvedCloseSetting(currentTargetClickCount, currentClickTimeWindow,
                    false, 0f, false, Vector2.zero);
                return false;
            }

            float clickTimeWindow = setting.resetClickTime > 0f ? setting.resetClickTime : currentClickTimeWindow;
            bool hasImageAlpha = setting.imageAlpha >= 0f;
            bool hasPosition = setting.position.x >= 0f && setting.position.y >= 0f;

            resolved = new ResolvedCloseSetting(setting.numToClose, clickTimeWindow,
                hasImageAlpha, setting.imageAlpha, hasPosition, setting.position);
            return true;
        }
    }
}
