using UnityEngine;
using Wonjeong.Data;
using Wonjeong.Utils;

namespace Wonjeong.Core
{
    public abstract class GameManagerBase : MonoBehaviour
    {
        public static GameManagerBase Instance;

        [SerializeField] private Reporter.Reporter reporter;
        [SerializeField] private GameObject systemCanvas;

        private Settings settings;

        private void Awake()
        {
            if (Instance)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
    
            if (systemCanvas)
            {
                DontDestroyOnLoad(systemCanvas);
            }
            else
            {
                Debug.LogWarning("[GameManager] systemCanvas is null.");
            }

            TimestampLogHandler.Attach();
        }

        private void Start()
        {
            Cursor.visible = false;
    
            if (reporter)
            {
                if (reporter.show) reporter.show = false;
            }
            else
            {
                Debug.LogWarning("[GameManager] reporter is null. Debug UI will be disabled.");
            }

            LoadSettings();
        }

        private void LoadSettings()
        {
            settings = JsonLoader.Load<Settings>("Settings.json");
            if (settings == null)
            {
                Debug.LogWarning("[GameManager] Settings file not found.");
                settings = new Settings();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.D))
            {
                if (reporter)
                {
                    reporter.showGameManagerControl = !reporter.showGameManagerControl;
                    if (reporter.show) reporter.show = false;
                }
                else
                {
                    Debug.LogWarning("[GameManager] reporter is null. Cannot toggle control.");
                }
            }
            else if (Input.GetKeyDown(KeyCode.M)) 
            {
                Cursor.visible = !Cursor.visible;
            }
        }
    }
}