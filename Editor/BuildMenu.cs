using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HuliacDev.Editor
{
    /// <summary>
    /// 에디터 상단의 Build 메뉴. "Windows Build"는 Build Settings의 씬 목록과 Development 설정으로
    /// Windows 64비트 빌드를 프로젝트의 Build/ 폴더에 만들고, 성공하면 아래를 이어서 함.
    /// <list type="bullet">
    /// <item>빌드 보관 폴더가 정해져 있으면 &lt;프로젝트 폴더 이름&gt;/&lt;YYMMDD&gt;-&lt;N&gt;/에 버전 관리용 zip과 BuildInfo.txt(커밋·버전·빌드 시각)를 보관함.
    /// N은 그날 몇 번째 빌드인지이며 빌드마다 새 폴더를 만듦.</item>
    /// <item>복사 폴더가 있으면(드라이브가 연결돼 있으면) 빌드 뒤 복사 설정에 따라 묻거나 바로 &lt;복사 폴더&gt;/&lt;프로젝트 폴더 이름&gt;/에
    /// 빌드 폴더를 압축 없이 복사함(현장 PC로 옮길 빌드, 그 폴더를 비운 뒤 항상 최신 빌드만 둠).</item>
    /// </list>
    /// 두 경로는 팀원·PC마다 다르므로 <see cref="BuildMenuPreferences"/>(Edit > Preferences > HuliacDev Build)에서 정함.
    /// 설치 태그는 붙이지 않음(설치할 때 따로 붙임).
    /// </summary>
    public static class BuildMenu
    {
        const string BuildFolderName = "Build";
        const string BuildInfoFileName = "BuildInfo.txt";

        /// <summary>
        /// Unity가 빌드와 함께 만드는, 현장에 보내면 안 되는 폴더의 이름 끝. 보관·복사에서 뺌.
        /// Burst 디버그 정보(*_BurstDebugInformation_DoNotShip)와 IL2CPP 백업(*_BackUpThisFolder_ButDontShipItWithYourGame).
        /// </summary>
        static readonly string[] DoNotShipSuffixes = { "_DoNotShip", "_ButDontShipItWithYourGame" };

        /// <summary>git이 멈췄을 때 Editor가 함께 멈추지 않도록 기다리는 최대 시간(밀리초).</summary>
        const int GitTimeoutMilliseconds = 10000;

        [MenuItem("Build/Windows Build", false, 0)]
        static void BuildWindowsAndArchive()
        {
            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Windows Build", "No scenes are enabled in Build Settings. Add scenes in File > Build Settings.", "OK");
                return;
            }

            if (!TryResolveArchiveRoot(out string archiveRoot))
            {
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string projectFolderName = Path.GetFileName(projectRoot);
            string buildDir = Path.Combine(projectRoot, BuildFolderName);
            string exePath = Path.Combine(buildDir, PlayerSettings.productName + ".exe");
            bool isDevelopment = EditorUserBuildSettings.development;

            if (!TryCleanBuildFolder(buildDir))
            {
                return;
            }

            // 빌드가 URP 에셋의 셰이더 필터 값 같은 파일을 다시 쓰므로, 빌드 뒤에 읽으면 그 변경까지 "커밋하지 않은 변경"으로 잡힘.
            // 빌드에 들어간 내용이 커밋돼 있었는지를 남기려고 빌드 전에 읽어 둠.
            GitState gitState = string.IsNullOrEmpty(archiveRoot) ? default : GitState.Read(projectRoot);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = isDevelopment ? BuildOptions.Development : BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[BuildMenu] Build failed ({report.summary.result}, {report.summary.totalErrors} error(s)). Skipping archive and copy.");
                return;
            }

            DateTime buildTime = DateTime.Now;
            Debug.Log($"[BuildMenu] Build finished: {exePath} ({report.summary.totalSize / (1024 * 1024)}MB, {report.summary.totalTime.TotalSeconds:F0}s)");

            // 보관(공유 드라이브)이 실패해도 현장으로 옮길 복사는 하도록 둘을 따로 처리함.
            if (!string.IsNullOrEmpty(archiveRoot))
            {
                try
                {
                    ArchiveBuild(buildDir, archiveRoot, projectFolderName, buildTime, isDevelopment, gitState);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BuildMenu] The build finished, but archiving failed: {e}");
                }
            }

            try
            {
                CopyToCopyFolder(buildDir, projectFolderName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildMenu] The build finished, but copying failed: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 빌드 전에 Build/ 폴더를 비움. 비우지 않으면 제품 이름을 바꾼 뒤 이전 &lt;옛 이름&gt;.exe·_Data가 남아 보관 zip과 복사에 섞임.
        /// 빌드한 앱이 실행 중이라 지우지 못하면 알리고 멈춤.
        /// </summary>
        static bool TryCleanBuildFolder(string buildDir)
        {
            if (!Directory.Exists(buildDir)) return true;

            try
            {
                Directory.Delete(buildDir, true);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("Windows Build", $"Could not empty the build folder. If the built app is running, close it and try again.\n\n{buildDir}\n{e.Message}", "OK");
                return false;
            }
        }

        [MenuItem("Build/Setting", false, 100)]
        static void OpenSettings()
        {
            BuildMenuPreferences.Open();
        }

        static string[] GetEnabledScenes()
        {
            int count = 0;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled) count++;
            }

            string[] paths = new string[count];
            int index = 0;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled) paths[index++] = scene.path;
            }

            return paths;
        }

        /// <summary>
        /// 빌드 보관 폴더를 확인함. 비어 있거나 찾을 수 없으면 설정을 열지, 보관 없이 빌드할지, 취소할지 물음.
        /// 빌드를 계속하면 true(보관하지 않으면 archiveRoot는 null), 멈추면 false.
        /// </summary>
        static bool TryResolveArchiveRoot(out string archiveRoot)
        {
            archiveRoot = BuildMenuPreferences.ArchiveRoot;
            if (!string.IsNullOrEmpty(archiveRoot) && Directory.Exists(archiveRoot))
            {
                return true;
            }

            string reason = string.IsNullOrEmpty(archiveRoot)
                ? "The build archive folder is not set."
                : $"The build archive folder was not found.\n{archiveRoot}";

            int choice = EditorUtility.DisplayDialogComplex("Build Archive Folder", reason + "\n\nYou can set it in Edit > Preferences > HuliacDev Build.", "Open Settings", "Cancel", "Build Without Archiving");
            archiveRoot = null;

            switch (choice)
            {
                case 0:
                    BuildMenuPreferences.Open();
                    return false;
                case 2:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 빌드 폴더를 zip으로 묶어 &lt;보관 폴더&gt;/&lt;프로젝트 폴더 이름&gt;/&lt;YYMMDD&gt;-&lt;N&gt;/에 두고 BuildInfo.txt를 씀.
        /// N은 그날 몇 번째 빌드인지(1부터)이며, 빌드마다 새 폴더를 만들어 이전 보관본을 덮어쓰지 않음.
        /// 공유 드라이브 같은 동기화 폴더에 직접 쓰다 끊기면 깨진 zip이 동기화되므로 임시 폴더에 만든 뒤 복사함.
        /// </summary>
        static void ArchiveBuild(string buildDir, string archiveRoot, string projectFolderName, DateTime buildTime, bool isDevelopment, GitState gitState)
        {
            string projectArchiveDir = Path.Combine(archiveRoot, projectFolderName);
            int buildNumber = NextBuildNumber(projectArchiveDir, buildTime);
            string dateFolder = Path.Combine(projectArchiveDir, $"{buildTime:yyMMdd}-{buildNumber}");
            Directory.CreateDirectory(dateFolder);

            string zipName = $"{projectFolderName}_v{PlayerSettings.bundleVersion}.zip";
            string tempZip = Path.Combine(Path.GetTempPath(), zipName);

            try
            {
                CreateZip(buildDir, tempZip);

                EditorUtility.DisplayProgressBar("Archiving Build", "Copying to the archive folder...", 0.95f);
                string archivedZip = Path.Combine(dateFolder, zipName);
                File.Copy(tempZip, archivedZip, true);

                File.WriteAllText(Path.Combine(dateFolder, BuildInfoFileName),
                    BuildInfoText(projectFolderName, buildTime, buildNumber, isDevelopment, gitState), new UTF8Encoding(true));

                Debug.Log($"[BuildMenu] Archived: {archivedZip} ({new FileInfo(archivedZip).Length / (1024 * 1024)}MB)");
            }
            catch
            {
                // 실패해 빈 폴더가 남으면 다음 번호를 차지하고 설치 기록이 그 폴더를 고를 수 있으므로 지움.
                TryDeleteQuietly(() =>
                {
                    if (Directory.Exists(dateFolder) && Directory.GetFileSystemEntries(dateFolder).Length == 0)
                    {
                        Directory.Delete(dateFolder);
                    }
                });

                throw;
            }
            finally
            {
                // 이어서 복사할지 묻는 창이 뜰 때 진행 표시줄이 남아 겹치지 않게 먼저 닫음.
                EditorUtility.ClearProgressBar();
                // 압축·복사가 실패해도 빌드 크기만 한 임시 zip이 남지 않게 함.
                TryDeleteQuietly(() =>
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                });
            }
        }

        /// <summary>
        /// 정리용 삭제를 하고, 파일 잠금(동기화·백신 검사 등)으로 실패하면 로그만 남김.
        /// 정리 실패가 원래 오류를 덮거나 보관 성공을 실패로 바꾸지 않게 하기 위함.
        /// </summary>
        static void TryDeleteQuietly(Action delete)
        {
            try
            {
                delete();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[BuildMenu] Could not clean up some files: {e.Message}");
            }
        }

        /// <summary>
        /// 그날 몇 번째 빌드인지 정함. 이미 있는 &lt;YYMMDD&gt;-&lt;N&gt; 폴더 가운데 가장 큰 N 다음 번호를 돌려줌(없으면 1).
        /// 개수가 아니라 가장 큰 번호를 보므로 중간 폴더를 지워도 번호가 겹치지 않음.
        /// </summary>
        static int NextBuildNumber(string projectArchiveDir, DateTime buildTime)
        {
            string datePrefix = buildTime.ToString("yyMMdd") + "-";
            int lastNumber = 0;

            if (Directory.Exists(projectArchiveDir))
            {
                foreach (string dir in Directory.GetDirectories(projectArchiveDir, datePrefix + "*"))
                {
                    string suffix = Path.GetFileName(dir).Substring(datePrefix.Length);
                    if (int.TryParse(suffix, out int number) && number > lastNumber)
                    {
                        lastNumber = number;
                    }
                }
            }

            return lastNumber + 1;
        }

        /// <summary>빌드 폴더의 파일을 현장에 필요 없는 폴더(<see cref="IsDoNotShip"/>)를 빼고 zip으로 묶음.</summary>
        static void CreateZip(string buildDir, string zipPath)
        {
            string[] files = Directory.GetFiles(buildDir, "*", SearchOption.AllDirectories);

            using (FileStream stream = new FileStream(zipPath, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string relativePath = files[i].Substring(buildDir.Length + 1);
                    if (IsDoNotShip(relativePath)) continue;

                    if (i % 20 == 0)
                    {
                        EditorUtility.DisplayProgressBar("Archiving Build", $"Compressing: {relativePath}", (float)i / files.Length * 0.9f);
                    }

                    ZipArchiveEntry entry = zip.CreateEntry(relativePath.Replace('\\', '/'), System.IO.Compression.CompressionLevel.Optimal);
                    using (Stream entryStream = entry.Open())
                    using (FileStream source = File.OpenRead(files[i]))
                    {
                        source.CopyTo(entryStream);
                    }
                }
            }
        }

        /// <summary>
        /// 복사 폴더가 정해져 있고 그 폴더가 있으면(드라이브가 연결돼 있으면) 빌드 뒤 복사 설정에 따라 묻거나 바로
        /// &lt;복사 폴더&gt;/&lt;프로젝트 폴더 이름&gt;/에 빌드 폴더를 복사함.
        /// 항상 최신 빌드와 같게 두도록 그 폴더를 비운 뒤 복사함(제품 이름을 바꾸거나 빠진 파일이 남지 않게).
        /// </summary>
        static void CopyToCopyFolder(string buildDir, string projectFolderName)
        {
            string copyRoot = BuildMenuPreferences.CopyRoot;
            if (string.IsNullOrEmpty(copyRoot)) return;

            BuildCopyMode mode = BuildMenuPreferences.CopyMode;
            if (mode == BuildCopyMode.Never)
            {
                Debug.Log("[BuildMenu] Copy after build is set to never copy, so the build was not copied (change it in Edit > Preferences > HuliacDev Build).");
                return;
            }

            if (!Directory.Exists(copyRoot))
            {
                Debug.Log($"[BuildMenu] The copy folder ({copyRoot}) was not found (the drive may not be connected), so the build was not copied.");
                return;
            }

            string target = Path.Combine(copyRoot, projectFolderName);
            if (mode == BuildCopyMode.Ask && !BuildCopyPromptWindow.Ask(target))
            {
                return;
            }

            if (Directory.Exists(target))
            {
                try
                {
                    Directory.Delete(target, true);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogError($"[BuildMenu] Could not empty the copy folder, so the build was not copied (another program may be using its files): {target} ({e.Message})");
                    return;
                }
            }

            string[] files = Directory.GetFiles(buildDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string relativePath = files[i].Substring(buildDir.Length + 1);
                if (IsDoNotShip(relativePath)) continue;

                if (i % 20 == 0)
                {
                    EditorUtility.DisplayProgressBar("Copying Build", relativePath, (float)i / files.Length);
                }

                string destination = Path.Combine(target, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(files[i], destination, true);
            }

            Debug.Log($"[BuildMenu] Copied: {target}");
        }

        static bool IsDoNotShip(string relativePath)
        {
            int separator = relativePath.IndexOfAny(new[] { '\\', '/' });
            string firstSegment = separator < 0 ? relativePath : relativePath.Substring(0, separator);
            foreach (string suffix in DoNotShipSuffixes)
            {
                if (firstSegment.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// 보관한 zip이 어느 커밋에서 나왔는지 알 수 있게 빌드 정보를 만듦. 설치 태그를 붙일 커밋을 고를 때 씀.
        /// git 상태는 빌드 전에 읽은 값이며, git이 없거나 저장소가 아니면 "알 수 없음"으로 적음.
        /// </summary>
        static string BuildInfoText(string projectFolderName, DateTime buildTime, int buildNumber, bool isDevelopment, GitState gitState)
        {
            string changes = gitState.Changes;
            string changeText = changes == null ? "알 수 없음" : (changes.Length == 0 ? "없음" : "있음(빌드에 커밋하지 않은 변경이 들어갔을 수 있음)\n" + changes);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"프로젝트 폴더: {projectFolderName}");
            builder.AppendLine($"제품 이름: {PlayerSettings.productName}");
            builder.AppendLine($"버전(bundleVersion): {PlayerSettings.bundleVersion}");
            builder.AppendLine($"빌드 시각: {buildTime:yyyy-MM-dd HH:mm:ss} (그날 보관한 {buildNumber}번째 빌드)");
            builder.AppendLine($"대상: Windows 64비트{(isDevelopment ? " (Development)" : string.Empty)}");
            builder.AppendLine($"Unity: {Application.unityVersion}");
            builder.AppendLine($"git 브랜치: {gitState.Branch ?? "알 수 없음"}");
            builder.AppendLine($"git 커밋: {gitState.Commit ?? "알 수 없음"}");
            builder.AppendLine($"커밋하지 않은 변경(빌드 전, Assets·Packages·ProjectSettings): {changeText}");
            return builder.ToString();
        }

        /// <summary>빌드 정보에 적을 git 상태(커밋, 브랜치, 커밋하지 않은 변경). 읽지 못한 값은 null.</summary>
        readonly struct GitState
        {
            public readonly string Commit;
            public readonly string Branch;
            public readonly string Changes;

            GitState(string commit, string branch, string changes)
            {
                Commit = commit;
                Branch = branch;
                Changes = changes;
            }

            /// <summary>프로젝트 폴더의 현재 커밋·브랜치와 Assets·Packages·ProjectSettings의 커밋하지 않은 변경을 읽음.</summary>
            public static GitState Read(string projectRoot)
            {
                return new GitState(
                    RunGit(projectRoot, "rev-parse HEAD"),
                    RunGit(projectRoot, "rev-parse --abbrev-ref HEAD"),
                    RunGit(projectRoot, "status --porcelain -- Assets Packages ProjectSettings"));
            }
        }

        /// <summary>프로젝트 폴더에서 git 명령을 실행해 표준 출력을 돌려줌. 실패하면 null.</summary>
        static string RunGit(string workingDirectory, string arguments)
        {
            try
            {
                // core.quotepath=false: 한글 파일 이름을 8진수 이스케이프 없이 UTF-8 그대로 받음.
                ProcessStartInfo startInfo = new ProcessStartInfo("git", "-c core.quotepath=false " + arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    // stderr를 비워 주지 않으면 git이 경고를 많이 쓸 때 파이프가 차서 서로 기다리며 멈출 수 있으므로 버림.
                    process.ErrorDataReceived += (_, __) => { };
                    process.Start();
                    process.BeginErrorReadLine();
                    // 표준 출력을 동기로 끝까지 읽으면 git이 멈췄을 때 시간 제한에 닿기 전에 막히므로 비동기로 읽음.
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();

                    if (!process.WaitForExit(GitTimeoutMilliseconds))
                    {
                        process.Kill();
                        return null;
                    }

                    if (!outputTask.Wait(GitTimeoutMilliseconds))
                    {
                        return null;
                    }

                    // porcelain 출력은 줄 앞 공백도 상태 표시라 끝 공백만 자름.
                    return process.ExitCode == 0 ? outputTask.Result.TrimEnd() : null;
                }
            }
            catch (Exception)
            {
                // git이 없거나(Win32Exception) 실행에 실패하면 빌드 정보에 "알 수 없음"으로 적고 보관은 계속함.
                return null;
            }
        }
    }
}
