using System.Text;

namespace Wonjeong.Tools.ShutdownScheduleEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // schtasks.exe 출력을 OS의 OEM 코드페이지(한글 Windows는 949)로 읽으려면 필요함.
        // 등록하지 않으면 "No data is available for encoding 949" 예외가 발생함.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Length > 0 ? args[0] : ResolveDefaultPath()));
    }

    /// <summary>
    /// 인자가 없을 때, exe와 같은 폴더(또는 StreamingAssets)의 ShutdownSettings.json을 자동으로 찾거나 생성함.
    /// 이 exe를 ShutdownSettings.json 옆(StreamingAssets 등)에 두고 바로 실행하는 배포 방식을 위함.
    /// self-contained 단일 파일 게시본은 실행 시 내용을 임시 폴더에 풀지만, Environment.ProcessPath는
    /// 그 임시 경로가 아니라 사용자가 실제로 더블클릭한 exe의 위치를 가리키므로 이를 기준으로 찾음.
    /// 파일이 아직 없으면 기본값으로 자동 생성하여 바로 편집할 수 있게 함.
    /// </summary>
    private static string? ResolveDefaultPath()
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return null;
        }

        string? rawDir = Path.GetDirectoryName(processPath);
        if (string.IsNullOrEmpty(rawDir))
        {
            return null;
        }

        string exeDirectory = rawDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 1. exe가 있는 폴더에 이미 파일이 있으면 그 파일 사용
        string directCandidate = Path.Combine(exeDirectory, MainForm.TargetFileName);
        if (File.Exists(directCandidate))
        {
            return directCandidate;
        }

        // 2. Unity 프로젝트 루트 등에서 실행된 경우 Assets/StreamingAssets 또는 StreamingAssets 확인
        string assetsStreamingDir = Path.Combine(exeDirectory, "Assets", "StreamingAssets");
        string assetsCandidate = Path.Combine(assetsStreamingDir, MainForm.TargetFileName);
        if (File.Exists(assetsCandidate))
        {
            return assetsCandidate;
        }

        string subStreamingDir = Path.Combine(exeDirectory, "StreamingAssets");
        string subCandidate = Path.Combine(subStreamingDir, MainForm.TargetFileName);
        if (File.Exists(subCandidate))
        {
            return subCandidate;
        }

        // 3. 파일이 아직 없는 경우: 자동 생성 대상 경로 결정
        string? targetPath = null;

        if (Path.GetFileName(exeDirectory).Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase))
        {
            // exe 자체가 StreamingAssets 폴더 안에 있는 경우
            targetPath = directCandidate;
        }
        else if (Directory.Exists(assetsStreamingDir))
        {
            // Assets/StreamingAssets 폴더가 존재하는 프로젝트 루트인 경우
            targetPath = assetsCandidate;
        }
        else if (Directory.Exists(subStreamingDir))
        {
            targetPath = subCandidate;
        }
        else if (!IsDevelopmentBinaryPath(exeDirectory))
        {
            // 개발 빌드 출력 폴더(bin/Debug 등)가 아닌 실제 배포 폴더인 경우 exe 옆에 생성
            targetPath = directCandidate;
        }

        if (targetPath != null)
        {
            try
            {
                MainForm.CreateDefaultSettingsFile(targetPath);
                return targetPath;
            }
            catch
            {
                // 쓰기 권한 부족 등으로 생성 실패 시 null 반환하여 일반 시작으로 유도
                return null;
            }
        }

        return null;
    }

    private static bool IsDevelopmentBinaryPath(string path)
    {
        string normalized = path.Replace('/', '\\');
        return normalized.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);
    }
}
