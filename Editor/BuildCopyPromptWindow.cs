using UnityEditor;
using UnityEngine;

namespace HuliacDev.Editor
{
    /// <summary>
    /// 빌드 뒤 복사 폴더에 복사할지 묻는 작은 모달 창. EditorUtility.DisplayDialog에는 체크박스를 붙일 수 없어
    /// (기본 opt-out은 문구가 영어이고 건너뛰기를 기억하는 방식이 분명하지 않음) "다시 묻지 않음"을 직접 둠.
    /// 체크하고 누른 버튼에 따라 <see cref="BuildMenuPreferences.CopyMode"/>를 항상 복사 / 복사 안 함으로 바꾸며,
    /// Edit > Preferences > HuliacDev Build에서 매번 묻기로 되돌림.
    /// </summary>
    internal sealed class BuildCopyPromptWindow : EditorWindow
    {
        // 창을 닫으면 인스턴스가 파괴되므로 결과는 정적 필드로 넘김.
        static bool _isCopyChosen;
        static bool _isAnswered;

        string _targetPath;
        bool _isDontAskAgain;

        /// <summary>
        /// 복사할지 묻고 결과를 돌려줌. 창을 닫기 버튼으로 닫으면 복사하지 않고 설정도 바꾸지 않음.
        /// </summary>
        public static bool Ask(string targetPath)
        {
            _isCopyChosen = false;
            _isAnswered = false;

            BuildCopyPromptWindow window = CreateInstance<BuildCopyPromptWindow>();
            window.titleContent = new GUIContent("Copy Build");
            window._targetPath = targetPath;
            Vector2 size = new Vector2(480f, 150f);
            window.minSize = size;
            window.maxSize = size;
            // 위치를 주지 않으면 주 모니터 왼쪽 위에 떠서, Unity를 다른 모니터에 띄운 경우 창을 찾기 어려움.
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.x + (main.width - size.x) * 0.5f, main.y + (main.height - size.y) * 0.5f, size.x, size.y);
            window.ShowModalUtility();

            return _isAnswered && _isCopyChosen;
        }

        /// <summary>
        /// 복사 여부를 묻는 문구와 다시 묻지 않음 체크, 복사·건너뛰기 버튼을 그림.
        /// </summary>
        void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Copy the build to the copy folder?", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_targetPath, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("This folder is emptied before the latest build is copied. Any other files in it are deleted too.", EditorStyles.wordWrappedMiniLabel);

            GUILayout.FlexibleSpace();
            _isDontAskAgain = EditorGUILayout.ToggleLeft("Don't ask again (undo in Edit > Preferences > HuliacDev Build)", _isDontAskAgain);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", GUILayout.Width(90f)))
            {
                Answer(true);
            }

            if (GUILayout.Button("Skip", GUILayout.Width(90f)))
            {
                Answer(false);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// 누른 버튼과 다시 묻지 않음 선택을 기록하고 창을 닫음.
        /// </summary>
        void Answer(bool isCopy)
        {
            _isCopyChosen = isCopy;
            _isAnswered = true;

            if (_isDontAskAgain)
            {
                BuildMenuPreferences.CopyMode = isCopy ? BuildCopyMode.Always : BuildCopyMode.Never;
            }

            Close();
            // 레이아웃 그룹 안에서 창을 닫았으므로 남은 GUI 처리를 건너뛰어 레이아웃 오류가 나지 않게 함.
            GUIUtility.ExitGUI();
        }
    }
}
