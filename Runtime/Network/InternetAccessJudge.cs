namespace HuliacDev.Network
{
    /// <summary>
    /// Windows가 판정한 인터넷 연결 수준을 전송 판단에 필요한 세 갈래로 줄인 값.
    /// </summary>
    internal enum InternetAccessHint
    {
        /// <summary>판정을 읽을 수 없거나 Windows가 아직 판정하지 못함(부팅 직후 등). 전송을 막지 않음.</summary>
        Unknown,

        /// <summary>네트워크에는 연결됐지만 인터넷에 나갈 수 없다고 판정함(작업 표시줄의 지구본 아이콘).</summary>
        NoInternet,

        /// <summary>인터넷에 연결됐다고 판정함.</summary>
        Internet
    }

    /// <summary>
    /// 어댑터 연결 여부, Windows의 인터넷 판정, 실제 서버 도달 여부를 합쳐 인터넷에 나갈 수 있다고 볼지 정하는 판정기.
    /// Windows가 "인터넷 연결됨"이라고 하지 않는 동안(인터넷 없음 또는 판정 전) 서버에 닿았다면 그 망에서는 Windows 판정을
    /// 믿을 수 없는 것으로 보고, Windows가 "인터넷 연결됨"이라고 할 때까지 그 판정을 쓰지 않음(어댑터 연결만 봄).
    /// 판정 전에 닿은 경우도 포함하는 이유는, 부팅 직후 판정 전에 서버에 닿은 뒤 Windows가 인터넷 없음으로 판정하는
    /// 망(같은 LAN 안 서버로만 보내는 현장)에서도 전송이 막히지 않게 하기 위함임. 정상 망이면 곧 "인터넷 연결됨"이 되어 풀림.
    /// 시작 로그가 실패해 보정하지 못한 채 막히는 동안에는 일정 간격마다 한 번 확인 전송을 허용해(<see cref="TryStartProbe"/>)
    /// 그 전송이 닿으면 보정할 수 있게 함.
    /// 입력을 호출부가 넘기므로 Windows API 없이 단위 테스트할 수 있음.
    /// </summary>
    internal sealed class InternetAccessJudge
    {
        private const int WindowsLevelNone = 1;
        private const int WindowsLevelLocalAccess = 2;
        private const int WindowsLevelInternetAccess = 3;
        private const int WindowsLevelConstrainedInternetAccess = 4;

        private bool _isHintIgnored;
        private bool _hasProbed;
        private double _lastProbeTime;

        /// <summary>Windows의 "인터넷 없음" 판정을 무시하고 있는지 여부.</summary>
        public bool IsHintIgnored => _isHintIgnored;

        /// <summary>
        /// 인터넷에 나갈 수 있다고 볼지 판정함. Windows가 "인터넷 연결됨"이라고 하면 판정 무시 상태를 풂.
        /// </summary>
        public bool Evaluate(bool isNetworkConnected, InternetAccessHint hint)
        {
            if (!isNetworkConnected) return false;

            switch (hint)
            {
                case InternetAccessHint.Internet:
                    // 부팅 직후처럼 판정이 늦어 무시하게 된 경우라면 이제 Windows 판정이 맞으므로 다시 따름.
                    _isHintIgnored = false;
                    return true;
                case InternetAccessHint.NoInternet:
                    return _isHintIgnored;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 서버에 닿았음을 기록하고, 이 때문에 Windows 판정을 새로 무시하기 시작했으면 true를 반환함.
        /// Windows가 이미 "인터넷 연결됨"이라고 하면 판정이 맞으므로 아무것도 바꾸지 않음.
        /// </summary>
        public bool ReportServerReached(InternetAccessHint hint)
        {
            if (hint == InternetAccessHint.Internet || _isHintIgnored) return false;

            _isHintIgnored = true;
            return true;
        }

        /// <summary>
        /// 어댑터는 연결됐는데 Windows 판정 때문에 막혀 있고, 마지막 확인 전송에서 intervalSeconds가 지났으면(처음이면 바로)
        /// 이번 전송을 판정으로 거르지 않는 확인 전송으로 쓰도록 true를 반환하고 시각을 기록함.
        /// 막혀 있지 않거나 어댑터가 끊겼으면 확인할 필요가 없으므로 false임.
        /// </summary>
        public bool TryStartProbe(bool isNetworkConnected, InternetAccessHint hint, double now, double intervalSeconds)
        {
            if (!isNetworkConnected || Evaluate(isNetworkConnected, hint)) return false;
            if (_hasProbed && now - _lastProbeTime < intervalSeconds) return false;

            _hasProbed = true;
            _lastProbeTime = now;
            return true;
        }

        /// <summary>
        /// Windows의 연결 수준 값(NL_NETWORK_CONNECTIVITY_LEVEL_HINT)을 세 갈래 판정으로 바꿈.
        /// 연결 없음·로컬만·캡티브 포털은 인터넷 없음으로, 알 수 없음·숨은 인터페이스·모르는 값은 알 수 없음으로 봄.
        /// </summary>
        public static InternetAccessHint FromWindowsLevel(int level)
        {
            switch (level)
            {
                case WindowsLevelInternetAccess:
                    return InternetAccessHint.Internet;
                case WindowsLevelNone:
                case WindowsLevelLocalAccess:
                case WindowsLevelConstrainedInternetAccess:
                    return InternetAccessHint.NoInternet;
                default:
                    return InternetAccessHint.Unknown;
            }
        }
    }
}
