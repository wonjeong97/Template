using UnityEngine;

namespace Wonjeong.Utils
{
    public class SystemCanvas : MonoBehaviour
    {
        public static SystemCanvas Instance;

        private void Awake()
        {
            if (!Instance)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); 
            }
            else
            {
                Destroy(gameObject); 
                return;
            }

            if (!TryGetComponent(out Canvas canvas))
            {
                canvas = gameObject.AddComponent<Canvas>();
                Debug.LogWarning("[SystemCanvas] Canvas component missing. Added default at runtime.");
            }
    
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; 
            canvas.sortingOrder = 30000;
        }
    }
}