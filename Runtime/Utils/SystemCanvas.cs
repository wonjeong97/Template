using UnityEngine;

public class SystemCanvas : MonoBehaviour
{
    public static SystemCanvas Instance;

    private void Awake()
    {
        // 1. 싱글톤 패턴 적용 (중복 생성 방지 및 파괴 방지)
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬이 바뀌어도 파괴되지 않음
        }
        else
        {
            Destroy(gameObject); // 이미 존재하면 새로 생긴 건 삭제
            return;
        }

        // 2. 캔버스 최상단 노출 설정
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            // 화면 전체를 덮는 오버레이 모드
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; 
            
            // 소팅 오더를 int 최대값에 가깝게 설정하여 무조건 맨 위에 오도록 함
            canvas.sortingOrder = 30000; 
        }
    }
}