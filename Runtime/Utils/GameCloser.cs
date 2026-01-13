using UnityEngine;
using UnityEngine.UI;
using Wonjeong.Data;
using Wonjeong.Utils;

/// <summary>  화면 특정 위치를 연타하여 게임을 강제 종료함. </summary>
public class GameCloser : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private RectTransform rectTransform; 

    private CloseSetting closeSetting; 

    private int clickCount = 0;     
    private float timer = 0f;       
    private bool counting = false;  

    private void Start()
    {
        // [수정] 현재 프로젝트의 JsonLoader 방식에 맞춰 설정 로드
        LoadSettings();

        if (closeSetting == null)
        {
            Debug.LogWarning("[GameCloser] CloseSetting is null or Load Failed. Script disabled.");
            this.enabled = false;
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
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }
    
        if (rectTransform != null)
        {
            // 설정 파일에서 위치 및 투명도 가져오기
            Vector2 anchor = closeSetting.position;
            
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = anchor;
            rectTransform.anchoredPosition = Vector2.zero; 

            if (rectTransform.TryGetComponent(out Image image))
            {
                image.color = new Color(1, 1, 1, closeSetting.imageAlpha);
            }
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