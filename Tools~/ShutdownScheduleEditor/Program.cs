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
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
    }
}
