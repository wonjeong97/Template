using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Wonjeong.UI;
using Wonjeong.Utils;
using Wonjeong.Data;
using Wonjeong.Reporter; // Reporter 네임스페이스 참조

namespace Wonjeong.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;
        
        [SerializeField] private Wonjeong.Reporter.Reporter reporter; 

        private float _currentInactivityTimer;
        private bool _isTransitioning;
        private float _inactivityLimit = 60f;
        private float _fadeTime = 1.0f;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (reporter == null)
            {
                reporter = FindObjectOfType<Wonjeong.Reporter.Reporter>();
            }
        }

        private void Start()
        {
            Cursor.visible = false;
            LoadSettings();

            if (reporter != null && reporter.show)
            {
                reporter.show = false;
            }
        }

        private void LoadSettings()
        {
            Settings settings = JsonLoader.Load<Settings>("Settings.json");
            if (settings != null)
            {
                _inactivityLimit = settings.inactivityTime;
                _fadeTime = settings.fadeTime;
            }
        }

        private void Update()
        {   
            // D키: 디버그 패널 제어
            if (Input.GetKeyDown(KeyCode.D) && reporter != null)
            {
                reporter.showGameManagerControl = !reporter.showGameManagerControl;
                if (reporter.show) reporter.show = false;
            }
            // M키: 마우스 커서 토글
            else if (Input.GetKeyDown(KeyCode.M))
            {
                Cursor.visible = !Cursor.visible;
            }

            if (_isTransitioning) return;

            HandleInactivity();
        }

        private void HandleInactivity()
        {
            // 타이틀 씬 이름을 나중에 실제 이름으로 변경하여 사용하세요.
            // if (SceneManager.GetActiveScene().name == "Title") 
            // {
            //     _currentInactivityTimer = 0f;
            //     return;
            // }

            if (Input.anyKey || Input.touchCount > 0)
            {
                _currentInactivityTimer = 0f;
            }
            else
            {
                _currentInactivityTimer += Time.deltaTime;
                if (_currentInactivityTimer >= _inactivityLimit)
                {
                    ReturnToTitle();
                }
            }
        }

        public void ReturnToTitle()
        {
            if (_isTransitioning) return;
            _isTransitioning = true;
            Debug.Log("[GameManager] Inactivity Detected: Ready to transition");
            StartCoroutine(ReturnToTitleRoutine());
        }

        private IEnumerator ReturnToTitleRoutine()
        {
            if (FadeManager.Instance == null)
            {
                Debug.LogError("[GameManager] FadeManager instance not found. Cannot perform transition.");
                _isTransitioning = false;
                yield break;
            }
            // 1. 페이드 아웃 실행
            bool fadeDone = false;
            FadeManager.Instance.FadeOut(_fadeTime, () => { fadeDone = true; });

            while (!fadeDone) yield return null;

            // 2. 씬 전환 (테스트를 위해 주석 처리됨. 실제 씬 이름을 입력하세요.)
            /*
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("YourTitleSceneName");
            while (!asyncLoad.isDone) yield return null;
            */
            
            Debug.Log("[GameManager] Scene transition logic is commented out for testing.");

            // 3. 상태 초기화 및 페이드 인
            _currentInactivityTimer = 0f;
            _isTransitioning = false;
            FadeManager.Instance.FadeIn(_fadeTime);
        }
    }
}