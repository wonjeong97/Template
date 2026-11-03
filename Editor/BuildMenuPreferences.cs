using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HuliacDev.Editor
{
    /// <summary>빌드가 끝난 뒤 복사 폴더에 복사할지 정하는 방식.</summary>
    public enum BuildCopyMode
    {
        /// <summary>빌드할 때마다 복사할지 물음.</summary>
        Ask = 0,

        /// <summary>묻지 않고 복사함.</summary>
        Always = 1,

        /// <summary>복사하지 않음.</summary>
        Never = 2
    }

    /// <summary>
    /// Build 메뉴(<see cref="BuildMenu"/>)가 쓰는 PC별 설정. Edit > Preferences > HuliacDev Build에서 정함.
    /// <para>
    /// 빌드 보관 폴더(버전 관리용 zip, 예: 공유 드라이브)와 복사 폴더(현장 PC로 옮길 빌드)는 팀원·PC마다 다르고,
    /// 공개 저장소에 들어가면 안 되므로 프로젝트 파일이 아니라 EditorPrefs에 저장함. 그 PC의 모든 프로젝트가 같은 값을 씀.
    /// </para>
    /// </summary>
    public static class BuildMenuPreferences
    {
        const string SettingsPath = "Preferences/HuliacDev Build";
        const string ArchiveRootKey = "HuliacDev.Build.ArchiveRoot";
        const string CopyRootKey = "HuliacDev.Build.CopyRoot";
        const string CopyModeKey = "HuliacDev.Build.CopyMode";

        static readonly string[] CopyModeLabels = { "매번 묻기", "항상 복사", "복사 안 함" };

        /// <summary>버전 관리용 빌드 zip을 &lt;프로젝트 폴더 이름&gt;/&lt;YYMMDD&gt;-&lt;N&gt;/(그날 N번째 빌드) 아래에 보관할 폴더. 비어 있으면 보관하지 않음.</summary>
        public static string ArchiveRoot
        {
            get => EditorPrefs.GetString(ArchiveRootKey, string.Empty);
            set => SetIfChanged(ArchiveRootKey, value);
        }

        /// <summary>현장 PC로 옮길 빌드를 &lt;프로젝트 폴더 이름&gt; 폴더에 복사할 폴더. 비어 있으면 복사하지 않음.</summary>
        public static string CopyRoot
        {
            get => EditorPrefs.GetString(CopyRootKey, string.Empty);
            set => SetIfChanged(CopyRootKey, value);
        }

        /// <summary>빌드 뒤 복사 폴더에 복사할지 정하는 방식. 복사 대화상자에서 "다시 묻지 않음"을 고르면 바뀜.</summary>
        public static BuildCopyMode CopyMode
        {
            get
            {
                int value = EditorPrefs.GetInt(CopyModeKey, (int)BuildCopyMode.Ask);
                return value >= (int)BuildCopyMode.Ask && value <= (int)BuildCopyMode.Never ? (BuildCopyMode)value : BuildCopyMode.Ask;
            }
            set
            {
                if (EditorPrefs.GetInt(CopyModeKey, (int)BuildCopyMode.Ask) != (int)value)
                {
                    EditorPrefs.SetInt(CopyModeKey, (int)value);
                }
            }
        }

        /// <summary>Preferences 창의 HuliacDev Build 항목을 엶.</summary>
        public static void Open()
        {
            SettingsService.OpenUserPreferences(SettingsPath);
        }

        [SettingsProvider]
        static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "HuliacDev Build",
                guiHandler = _ => DrawGui(),
                keywords = new HashSet<string> { "Build", "Archive", "Copy", "빌드", "보관", "복사" }
            };
        }

        static void DrawGui()
        {
            EditorGUILayout.HelpBox("Build > Windows Build 메뉴가 쓰는 설정입니다. 이 PC의 모든 프로젝트에 같이 적용됩니다.", MessageType.Info);
            EditorGUILayout.Space();

            DrawFolderField(
                ArchiveRootKey,
                "빌드 보관 폴더",
                "버전 관리용 빌드 zip을 <프로젝트 폴더 이름>/<YYMMDD>-<N>/ 아래에 보관합니다. N은 그날 몇 번째로 보관한 빌드인지이며, 빌드마다 새 폴더를 만듭니다. 비우면 보관하지 않습니다.");

            DrawFolderField(
                CopyRootKey,
                "복사 폴더",
                "현장 PC로 옮길 빌드를 <프로젝트 폴더 이름> 폴더에 압축 없이 복사합니다(그 폴더를 비운 뒤 최신 빌드만 둠). 빌드할 때 이 폴더가 없으면(드라이브가 연결되지 않았으면) 건너뜁니다. 비우면 복사하지 않습니다.");

            EditorGUILayout.LabelField("빌드 뒤 복사", EditorStyles.boldLabel);
            CopyMode = (BuildCopyMode)EditorGUILayout.Popup((int)CopyMode, CopyModeLabels);
            EditorGUILayout.LabelField("복사 대화상자에서 '다시 묻지 않음'을 고르면 '항상 복사'나 '복사 안 함'으로 바뀝니다. 다시 물으려면 '매번 묻기'로 돌립니다.", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>경로 입력란과 폴더 선택 버튼을 그리고, 바뀐 값을 바로 EditorPrefs에 씀.</summary>
        static void DrawFolderField(string key, string label, string help)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            string value = EditorGUILayout.TextField(EditorPrefs.GetString(key, string.Empty));
            SetIfChanged(key, value);
            if (GUILayout.Button("찾기...", GUILayout.Width(60)))
            {
                string picked = EditorUtility.OpenFolderPanel(label, value, string.Empty);
                if (!string.IsNullOrEmpty(picked))
                {
                    SetIfChanged(key, picked);
                    // 입력란에 포커스가 남아 있으면 고른 경로가 화면에 바로 반영되지 않으므로 포커스를 뺌.
                    GUI.FocusControl(null);
                }

                // 폴더 선택 창을 띄운 뒤에는 레이아웃이 어긋날 수 있어, 남은 GUI 처리를 건너뛰고 다음 프레임에 다시 그림.
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
        }

        /// <summary>GUI가 매 프레임 값을 넘기므로 바뀐 때만 EditorPrefs에 씀.</summary>
        static void SetIfChanged(string key, string value)
        {
            // 탐색기의 '경로로 복사'로 붙여 넣으면 앞뒤에 큰따옴표가 붙으므로 함께 벗김.
            string trimmed = value == null ? string.Empty : value.Trim().Trim('"');
            if (EditorPrefs.GetString(key, string.Empty) != trimmed)
            {
                EditorPrefs.SetString(key, trimmed);
            }
        }
    }
}
