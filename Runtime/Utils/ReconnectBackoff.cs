namespace HuliacDev.Utils
{
    /// <summary>
    /// 장치에 다시 연결하는 간격을 늘리는 규칙. 시리얼(ArduinoManager)과 TCP(TcpLineClient) 장치가 같은 규칙으로 다시 연결하게 함께 씀.
    /// 고정 간격으로 끝없이 시도하면 장치가 오래 꺼져 있을 때 시도와 로그가 몰리고, 상한이 없으면 장치가 돌아와도 한참 뒤에야 연결됨.
    /// </summary>
    internal static class ReconnectBackoff
    {
        /// <summary>다시 연결하는 간격의 최소값(ms). 잘못된 설정으로 쉬지 않고 연결을 시도하지 않게 함.</summary>
        public const int MinDelayMs = 1000;

        /// <summary>
        /// 다음 대기 시간을 정함. 두 배로 늘리되 상한을 넘지 않고, 현재 간격이 최소값보다 작으면 최소값부터 시작함.
        /// 상한이 최소값보다 작으면 최소값으로 봄. 큰 값에서도 넘치지 않게 상한의 절반부터는 상한을 돌려줌.
        /// </summary>
        public static int NextDelayMs(int currentDelayMs, int maxDelayMs)
        {
            if (maxDelayMs < MinDelayMs) maxDelayMs = MinDelayMs;
            if (currentDelayMs < MinDelayMs) return MinDelayMs;
            if (currentDelayMs >= maxDelayMs / 2) return maxDelayMs;
            return currentDelayMs * 2;
        }
    }
}
