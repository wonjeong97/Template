namespace HuliacDev.Utils
{
    /// <summary>
    /// 앱 종료를 요청한 주체와 넘길 종료 코드를 기록해두는 정적 상태.
    /// <para>
    /// Application.wantsToQuit 자체는 "누가" 종료를 요청했는지 알려주지 않음. 그래서 종료를
    /// 직접 발동시키는 쪽(ShutdownScheduler, GameCloser 등)이 Application.Quit()을 부르기
    /// 직전에 여기 자기 이름을 기록해두면, ApiManagerBase가 종료 로그 메시지에 그대로 반영함.
    /// </para>
    /// <para>
    /// 종료 코드도 같은 이유로 여기 기록함. ApiManagerBase는 종료 로그를 보내려고 종료를 한 번 미뤘다가
    /// 다시 끝내는데, Unity는 처음 Application.Quit(코드)에 넘긴 값을 알려주지 않으므로 이 값으로 끝냄.
    /// 0이 아닌 코드로 끝내려면 Set(reason, exitCode)를 부른 뒤 Application.Quit(exitCode)를 부를 것
    /// (Application.Quit(코드)만 부르면 ApiManagerBase가 있을 때 종료 코드가 0으로 바뀜).
    /// </para>
    /// <para>
    /// 아무도 기록하지 않은 채 종료되는 경우(Alt+F4, 창 닫기 버튼처럼 OS가 직접 발생시키는
    /// 종료)는 사용자가 직접 닫은 것이므로 기본값 User와 종료 코드 0으로 남음.
    /// </para>
    /// </summary>
    public static class QuitReason
    {
        public const string User = "User";
        public const string GameCloser = "GameCloser";

        /// <summary>
        /// 로그 메시지에 그대로 노출되는 값이라 표기 규칙(공백 포함)에 맞춤: "end (by Shutdown Scheduler)".
        /// </summary>
        public const string ShutdownScheduler = "Shutdown Scheduler";

        public static string Current { get; private set; } = User;

        /// <summary>
        /// 종료 로그를 보낸 뒤 다시 끝낼 때 Application.Quit에 넘길 종료 코드. 기본값 0.
        /// </summary>
        public static int ExitCode { get; private set; }

        /// <summary>
        /// 종료 사유를 설정하여 이후 종료 로그에 반영되게 함. 일부러 끄는 경로이므로 종료 코드는 0으로 되돌림.
        /// </summary>
        public static void Set(string reason)
        {
            Set(reason, 0);
        }

        /// <summary>
        /// 종료 사유와 종료 코드를 함께 설정함. 이어서 Application.Quit(exitCode)를 부를 것.
        /// </summary>
        public static void Set(string reason, int exitCode)
        {
            Current = reason;
            ExitCode = exitCode;
        }
    }
}
