using UnityEngine;

namespace HuliacDev.Utils
{
    /// <summary>
    /// 씬 전환(DontDestroyOnLoad) 시 싱글톤 매니저의 중복 생성을 감지하고
    /// 복제된 인스턴스를 안전하게 파괴하여 수명주기를 일원화 관리하는 유틸리티.
    /// </summary>
    /// <typeparam name="T">가드를 적용할 MonoBehaviour 컴포넌트 타입</typeparam>
    public static class SingletonGuard<T> where T : MonoBehaviour
    {
        private static bool _isInstantiated;
        private static T _instance;

        /// <summary>
        /// 현재 타입의 인스턴스가 활성 상태로 존재하는지 여부.
        /// </summary>
        public static bool IsInstantiated => _isInstantiated && (Object)_instance != null;

        /// <summary>
        /// Awake() 시점에 호출하여 중복 생성을 검사하고 방어함.
        /// 중복으로 판정되어 파괴가 예약되면 true를 반환하므로, 호출측은 이때 Awake를 조기 반환해야 함.
        /// 최초 원본이면 false를 반환하고 isOriginal로 원본 여부를 함께 돌려줌.
        /// </summary>
        public static bool CheckDuplicate(T instance, out bool isOriginal, bool dontDestroyOnLoad = true)
        {
            // 에디터 Domain Reload 비활성화 환경이거나 이전 인스턴스가 파괴된 경우(fake null) 자가 복구
            if (!_isInstantiated || (Object)_instance == null)
            {
                _isInstantiated = true;
                _instance = instance;
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
        public static void Release(bool isOriginal)
        {
            if (isOriginal)
            {
                _isInstantiated = false;
                _instance = null;
            }
        }

        /// <summary>
        /// 테스트 또는 도메인 리로드 시 정적 상태를 강제 초기화함.
        /// </summary>
        public static void ResetForTesting()
        {
            _isInstantiated = false;
            _instance = null;
        }
    }
}
