using UnityEngine;

namespace HuliacDev.Core
{
    /// <summary>
    /// 운영자 단축키로 바뀐 상태를 화면 위쪽에 잠깐 보여 주는 IMGUI 오버레이.
    /// 씬·프리팹 배치 없이 코드로 만들어 쓰며(WindowFocusRestorer), 표시 시간이 지나면 컴포넌트를 꺼 OnGUI가 더 불리지 않게 함.
    /// IMGUI는 Screen Space Overlay 캔버스(SystemCanvas 포함)보다 나중에 그려지므로 다른 UI에 가려지지 않음.
    /// </summary>
    internal sealed class StatusMessageOverlay : MonoBehaviour
    {
        /// <summary>메시지를 보여 주는 시간(초).</summary>
        private const float DisplaySeconds = 2f;

        /// <summary>화면 높이 대비 글자 크기 비율. 해상도가 달라도 비슷한 크기로 보이게 함.</summary>
        private const float FontSizeRatio = 0.04f;

        /// <summary>글자 크기의 최소값. 낮은 해상도 창에서도 읽을 수 있게 함.</summary>
        private const int MinFontSize = 16;

        private string _message;
        private float _hideTime;
        private GUIStyle _boxStyle;

        /// <summary>
        /// 새 GameObject에 꺼진 상태의 오버레이를 만들고 씬을 넘어 유지되게 함. 파괴는 만든 쪽이 맡음.
        /// </summary>
        public static StatusMessageOverlay Create(string objectName)
        {
            GameObject go = new GameObject(objectName);
            DontDestroyOnLoad(go);

            StatusMessageOverlay overlay = go.AddComponent<StatusMessageOverlay>();
            overlay.enabled = false;
            return overlay;
        }

        /// <summary>
        /// 메시지를 표시 시간 동안 보여 줌. 이미 보이는 중이면 내용을 바꾸고 시간을 처음부터 다시 셈.
        /// </summary>
        public void Show(string message)
        {
            _message = message;
            _hideTime = Time.unscaledTime + DisplaySeconds;
            enabled = true;
        }

        /// <summary>
        /// 표시 시간이 지나면 컴포넌트를 끔. 일시정지(timeScale 0) 중에도 사라지도록 unscaledTime으로 셈.
        /// </summary>
        private void Update()
        {
            if (Time.unscaledTime >= _hideTime)
            {
                enabled = false;
            }
        }

        /// <summary>
        /// 화면 위쪽 가운데에 반투명 배경 상자와 함께 메시지를 그림.
        /// </summary>
        private void OnGUI()
        {
            // GUI.skin은 OnGUI 안에서만 쓸 수 있으므로 처음 그릴 때 스타일을 만들어 재사용함.
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
            }

            _boxStyle.fontSize = Mathf.Max(MinFontSize, Mathf.RoundToInt(Screen.height * FontSizeRatio));

            float width = Screen.width * 0.6f;
            float height = _boxStyle.fontSize * 2.5f;
            Rect rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.1f, width, height);
            GUI.Box(rect, _message, _boxStyle);
        }
    }
}
