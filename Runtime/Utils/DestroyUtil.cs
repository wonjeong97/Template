using UnityEngine;

namespace Wonjeong.Utils
{
    /// <summary>
    /// 오브젝트 파괴 시 실행 컨텍스트(플레이 모드/에디터)에 따라 알맞은 파괴 API를 선택하는 유틸리티.
    /// EditMode 테스트나 에디터 툴링 코드에서 <see cref="Object.Destroy"/>를 호출하면
    /// "Destroy can only be called from the main thread or from Play mode"
    /// 경고가 발생하며 실제로는 파괴되지 않으므로, 플레이 모드 여부에 따라
    /// Destroy/DestroyImmediate를 분기해 호출부가 신경 쓰지 않아도 되게 함.
    /// </summary>
    public static class DestroyUtil
    {
        /// <summary>
        /// obj가 null이 아니면 현재 컨텍스트에 맞는 방식으로 안전하게 파괴함.
        /// 플레이 모드에서는 Object.Destroy(다음 프레임에 파괴), 그 외(에디터/테스트)에서는
        /// Object.DestroyImmediate(즉시 파괴)를 사용함.
        /// </summary>
        public static void SafeDestroy(Object obj)
        {
            if (!obj) return;

            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj);
            }
        }
    }
}
