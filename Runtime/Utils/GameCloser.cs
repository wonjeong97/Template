using UnityEngine;
using UnityEngine.UI;
using Wonjeong.Data;

namespace Wonjeong.Utils
{
    /// <summary>  화면 특정 위치를 연타하여 게임을 강제 종료함. </summary>
    public class GameCloser : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private RectTransform rectTransform; 

        private CloseSetting closeSetting; 

        private int clickCount;     
        private float timer;       
        private bool counting;  

        private void Start()
        {
            LoadSettings();

            if (closeSetting == null)
            {
                Debug.LogWarning("[GameCloser] CloseSetting is null or Load Failed. Script disabled.");
                enabled = false;
                return;
            }

            InitializeUI();
        }

        private void LoadSettings()
        {
            // GameConstants.Path.JsonSetting 경로에서 Settings 데이터를 로드
            Settings settings = JsonLoader.Load<Settings>("Settings.json");

            if (settings != null)
            {
                closeSetting = settings.closeSetting; // Settings 클래스에 closeSetting이 정의되어 있어야 함
            }
        }

        private void InitializeUI()
        {
            if (!rectTransform)
            {
                if (!TryGetComponent(out rectTransform))
                {
                    rectTransform = gameObject.AddComponent<RectTransform>();
                    Debug.LogWarning("[GameCloser] RectTransform component missing. Added default at runtime.");
                }
            }

            if (rectTransform)
            {
                // 설정 파일에서 위치 가져오기
                Vector2 anchor = closeSetting.position;
    
                rectTransform.anchorMin = anchor;
                rectTransform.anchorMax = anchor;
                rectTransform.pivot = anchor;
                rectTransform.anchoredPosition = Vector2.zero; 

                // Image 컴포넌트 최적화 및 Fallback 처리
                if (!TryGetComponent(out Image image))
                {
                    image = gameObject.AddComponent<Image>();
                    Debug.LogWarning("[GameCloser] Image component missing. Added default at runtime.");
                }
        
                image.color = new Color(1, 1, 1, closeSetting.imageAlpha);
            }
        }

        private void Update()
        {
            if (!counting) return;

            timer += Time.deltaTime;

            // 일정 시간이 지나면 클릭 횟수 초기화
            if (timer >= closeSetting.resetClickTime)
            {
                ResetClickCount();
            }
        }

        /// <summary> 버튼(이미지) 클릭 시 호출. Inspector의 Button OnClick 이벤트에 연결 필요. </summary>
        public void Click()
        {
            if (closeSetting == null) return;

            counting = true;
            clickCount++;

            // 목표 횟수 도달 시 종료
            if (clickCount >= closeSetting.numToClose)
            {
                Debug.Log("[GameCloser] Force Exit Triggered!");
                Application.Quit();
            }
        }

        private void ResetClickCount()
        {
            clickCount = 0;
            timer = 0f;
            counting = false;
        }
    }
}