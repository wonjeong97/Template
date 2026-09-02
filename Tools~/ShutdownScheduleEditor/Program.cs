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
    /// 인자가 없을 때, exe와 같은 폴더에 있는 ShutdownSettings.json을 자동으로 찾음.
    /// 이 exe를 ShutdownSettings.json 옆(StreamingAssets 등)에 두고 바로 실행하는 배포
    /// 방식을 위함. self-contained 단일 파일 게시본은 실행 시 내용을 임시 폴더에 풀지만,
    /// Environment.ProcessPath는 그 임시 경로가 아니라 사용자가 실제로 더블클릭한 exe의
    /// 위치를 가리키므로 이를 기준으로 찾음.
    /// </summary>
    private static string? ResolveDefaultPath()
    {
        string? exeDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDirectory == null)
        {
            return null;
        }

        string candidate = Path.Combine(exeDirectory, MainForm.TargetFileName);
        return File.Exists(candidate) ? candidate : null;
    }
}
