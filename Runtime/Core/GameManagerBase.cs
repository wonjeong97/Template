using System;
using UnityEngine;
using Wonjeong.Data;
using Wonjeong.Utils;

namespace Wonjeong.Core
{
    public abstract class GameManagerBase<T> : MonoBehaviour where T : GameManagerBase<T>
    {
        public static T Instance { get; private set; }

        [SerializeField] private Reporter.Reporter reporter;
        [SerializeField] private GameObject systemCanvas;
        [SerializeField] private GameObject inspectorContainer;
        
        protected Settings settings;
        public event Action OnInspectorClosed;

        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = (T)this;
            DontDestroyOnLoad(gameObject);
    
            if (systemCanvas) DontDestroyOnLoad(systemCanvas);
            else  Debug.LogWarning("[GameManagerBase] systemCanvas is null.");

            TimestampLogHandler.Attach();
        }

        protected virtual void Start()
        {
            Cursor.visible = false;
    
            if (reporter) 
            {
                if (reporter.show) reporter.show = false;
                else Debug.LogWarning("[GameManagerBase] reporter is null. Debug UI will be disabled.");
            }
            
            if (inspectorContainer) inspectorContainer.SetActive(false);
            else Debug.LogWarning("[GameManagerBase] inspectorContainer is null.");
            
            LoadSettings();
        }

        protected virtual void LoadSettings()
        {
            settings = JsonLoader.Load<Settings>("Settings.json");
            if (settings == null)
            {
                Debug.LogWarning("[GameManagerBase] Settings file not found.");
                settings = new Settings();
            }
        }

        protected virtual void Update()
        {
            if (Input.GetKeyDown(KeyCode.D))
            {
                if (reporter)
                {
                    reporter.showGameManagerControl = !reporter.showGameManagerControl;
                    if (reporter.show) reporter.show = false;
                }
                else Debug.LogWarning("[GameManagerBase] reporter is null. Cannot toggle control.");
            }
            else if (Input.GetKeyDown(KeyCode.M)) Cursor.visible = !Cursor.visible;
            else if (Input.GetKeyDown(KeyCode.I))
            {
                if (inspectorContainer) 
                {
                    bool isActivating = !inspectorContainer.activeSelf;
                    inspectorContainer.SetActive(isActivating);
                    if (!isActivating) OnInspectorClosed?.Invoke(); 
                }
                else Debug.LogWarning("[GameManagerBase] inspectorContainer is null. Cannot toggle inspector.");
            }
        }
    }
}