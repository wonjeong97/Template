using UnityEngine;

namespace Wonjeong.Utils
{
    /// <summary>
    /// 씬 전환(DontDestroyOnLoad) 시 싱글톤 매니저의 중복 생성을 감지하고
    /// 복제된 인스턴스를 안전하게 파괴하여 수명주기를 일원화 관리하는 유틸리티.
    /// </summary>
    /// <typeparam name="T">가드를 적용할 MonoBehaviour 컴포넌트 타입</typeparam>
    public static class SingletonGuard<T> where T : MonoBehaviour
    {
        private static bool _isInstantiated;

        /// <summary>
        /// 현재 타입의 인스턴스가 활성 상태로 존재하는지 여부.
        /// </summary>
        public static bool IsInstantiated => _isInstantiated;

        /// <summary>
        /// Awake() 시점에 호출하여 중복 생성을 검사하고 방어함.
        /// </summary>
        /// <param name="instance">현재 Awake가 실행 중인 MonoBehaviour 인스턴스</param>
        /// <param name="isOriginal">호출 컴포넌트의 원본 여부 (out)</param>
        /// <param name="dontDestroyOnLoad">최초 인스턴스를 DontDestroyOnLoad로 등록할지 여부 (기본 true)</param>
        /// <returns>중복으로 판정되어 파괴 예약된 경우 true (호출측 Awake 조기 반환 필요), 최초 원본이면 false</returns>
        public static bool CheckDuplicate(T instance, out bool isOriginal, bool dontDestroyOnLoad = true)
        {
            if (!_isInstantiated)
            {
                _isInstantiated = true;
                isOriginal = true;

                if (dontDestroyOnLoad && instance.transform.parent == null)
                {
                    Object.DontDestroyOnLoad(instance.gameObject);
                }
                return false;
            }
            else
            {
                isOriginal = false;
                // 파괴 예약된 프레임 동안 Start, Update 등이 실행되지 않도록 비활성화
                instance.enabled = false;
                DestroyUtil.SafeDestroy(instance.gameObject);
                return true;
            }
        }

        /// <summary>
        /// OnDestroy() 시점에 호출하여 원본 인스턴스가 파괴될 때 인스턴스 플래그를 해제함.
        /// </summary>
        /// <param name="isOriginal">해당 인스턴스가 원본이었는지 여부</param>
        public static void Release(bool isOriginal)
        {
            if (isOriginal)
            {
                _isInstantiated = false;
            }
        }

        /// <summary>
        /// 테스트 또는 도메인 리로드 시 정적 상태를 강제 초기화함.
        /// </summary>
        public static void ResetForTesting()
        {
            _isInstantiated = false;
        }
    }
}
