using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonjeong.Editor
{
    /// <summary>
    /// Tools~/ShutdownScheduleEditor(독립 .NET WinForms 프로젝트)를 로컬에서 원클릭으로
    /// dotnet publish 게시하고, 결과 exe를 현재 프로젝트의 StreamingAssets로 복사하는 유틸리티.
    /// 게시된 exe는 self-contained 단일 파일이라 용량이 커서(약 150MB) git에 커밋하지 않고,
    /// 필요할 때 로컬에서 매번 새로 만드는 방식을 씀.
    /// </summary>
    public static class ShutdownScheduleEditorBuilder
    {
        const string ToolRelativePath = "Tools~/ShutdownScheduleEditor";
        const string PublishRelativePath = "bin/Release/net8.0-windows/win-x64/publish";
        const string ExeFileName = "ShutdownScheduleEditor.exe";

        [MenuItem("Tools/Build Shutdown Scheduler Exe")]
        static void Build()
        {
            try
            {
                string toolDir = FindToolDirectory();
                if (toolDir == null)
                {
                    Debug.LogError($"[ShutdownScheduleEditorBuilder] {ToolRelativePath} 폴더를 찾을 수 없습니다. 패키지 설치 상태를 확인하세요.");
                    return;
                }

                if (!IsDotnetAvailable())
                {
                    Debug.LogError("[ShutdownScheduleEditorBuilder] .NET SDK가 필요합니다. dotnet --version으로 설치를 확인하세요(.NET 8 SDK 이상 필요).");
                    return;
                }

                EditorUtility.DisplayProgressBar("Shutdown Scheduler Exe 빌드", "dotnet publish 게시 중... (15~30초 소요)", 0.3f);

                if (!RunPublish(toolDir, out string output, out string error, out int exitCode))
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogError($"[ShutdownScheduleEditorBuilder] dotnet publish 실패 (종료 코드 {exitCode}).\n--- stdout ---\n{output}\n--- stderr ---\n{error}");
                    return;
                }

                string publishedExePath = Path.Combine(toolDir, PublishRelativePath, ExeFileName);
                if (!File.Exists(publishedExePath))
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogError($"[ShutdownScheduleEditorBuilder] 게시는 성공했지만 결과 파일을 찾을 수 없습니다: {publishedExePath}");
                    return;
                }

                EditorUtility.DisplayProgressBar("Shutdown Scheduler Exe 빌드", "StreamingAssets로 복사 중...", 0.9f);

                string streamingAssetsDir = Path.Combine(Application.dataPath, "StreamingAssets");
                Directory.CreateDirectory(streamingAssetsDir);
                string destExePath = Path.Combine(streamingAssetsDir, ExeFileName);
                File.Copy(publishedExePath, destExePath, overwrite: true);

                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();

                long sizeMb = new FileInfo(destExePath).Length / (1024 * 1024);
                Debug.Log($"[ShutdownScheduleEditorBuilder] 완료: Assets/StreamingAssets/{ExeFileName} ({sizeMb}MB)");
                EditorUtility.DisplayDialog("Shutdown Scheduler Exe 빌드 완료",
                    $"Assets/StreamingAssets/{ExeFileName}\n크기: {sizeMb}MB", "확인");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[ShutdownScheduleEditorBuilder] 예외 발생: {e}");
            }
        }

        /// <summary>
        /// 이 스크립트가 속한 패키지(com.wonjeong.template)의 실제 설치 경로를 찾아
        /// Tools~/ShutdownScheduleEditor의 절대 경로를 구성함. Git 소스로 설치되면
        /// Library/PackageCache/com.wonjeong.template@&lt;커밋해시&gt;/ 형태라 해시가 패키지
        /// 갱신마다 바뀌므로, 경로를 하드코딩하지 않고 PackageInfo로 조회함.
        /// </summary>
        static string FindToolDirectory()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ShutdownScheduleEditorBuilder).Assembly);
            if (packageInfo == null)
            {
                return null;
            }

            string toolDir = Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, ToolRelativePath));
            return Directory.Exists(toolDir) ? toolDir : null;
        }

        /// <summary>dotnet 실행 파일이 PATH에 있는지 확인함.</summary>
        static bool IsDotnetAvailable()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("dotnet", "--version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process process = Process.Start(startInfo);
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                // dotnet 실행 파일 자체를 찾지 못한 경우(PATH 미등록 등)
                return false;
            }
        }

        /// <summary>
        /// Tools~/ShutdownScheduleEditor를 작업 디렉터리로 dotnet publish를 동기 실행함.
        /// 표준 출력/에러를 ReadToEnd로 한 번에 읽으면 출력량이 많을 때 프로세스가 버퍼가 찰
        /// 때까지 서로를 기다리며 멈추는 교착이 발생할 수 있어, 비동기 이벤트 구독으로 읽음.
        /// </summary>
        static bool RunPublish(string toolDir, out string output, out string error, out int exitCode)
        {
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();

            ProcessStartInfo startInfo = new ProcessStartInfo("dotnet",
                "publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true")
            {
                WorkingDirectory = toolDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            output = outputBuilder.ToString();
            error = errorBuilder.ToString();
            exitCode = process.ExitCode;

            return exitCode == 0;
        }
    }
}
