namespace Wonjeong.Utils
{
    /// <summary>
    /// 앱 종료를 요청한 주체를 기록해두는 정적 상태.
    /// <para>
    /// Application.wantsToQuit 자체는 "누가" 종료를 요청했는지 알려주지 않음. 그래서 종료를
    /// 직접 발동시키는 쪽(ShutdownScheduler, GameCloser 등)이 Application.Quit()을 부르기
    /// 직전에 여기 자기 이름을 기록해두면, ApiManagerBase가 종료 로그 메시지에 그대로 반영함.
    /// </para>
    /// <para>
    /// 아무도 기록하지 않은 채 종료되는 경우(Alt+F4, 창 닫기 버튼처럼 OS가 직접 발생시키는
    /// 종료)는 사용자가 직접 닫은 것이므로 기본값 User로 남음.
    /// </para>
    /// </summary>
    public static class QuitReason
    {
        public const string User = "User";
        public const string GameCloser = "GameCloser";
        public const string ShutdownScheduler = "ShutdownScheduler";

        public static string Current { get; private set; } = User;

        public static void Set(string reason)
        {
            Current = reason;
        }
    }
}
