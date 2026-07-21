using UnityEngine;
using System.Collections;

namespace Wonjeong.Reporter
{
    public class ReporterGUI : MonoBehaviour
    {
        Reporter reporter;
        void Awake()
        {
            reporter = gameObject.GetComponent<Reporter>();
            if (reporter == null)
            {
                Debug.LogError("ReporterGUI requires a Reporter component on the same GameObject.");
                enabled = false;
            }
        }

        void OnGUI()
        {
            if (reporter != null) reporter.OnGUIDraw();
        }
    }

}
