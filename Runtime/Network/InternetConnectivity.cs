using System;
using System.Runtime.InteropServices;
using UnityEngine;
using ZLogger;

namespace HuliacDev.Network
{
    /// <summary>
    /// 인터넷에 나갈 수 있는지 판정하는 공통 진입점. ApiRetryUtil의 전송 전 확인과 NetworkStatusService가 같은 판정을 씀.
    /// <para>
    /// Application.internetReachability는 네트워크 어댑터가 연결돼 있는지만 알려 줘서, 공유기에는 연결됐지만
    /// 인터넷이 끊긴 경우(작업 표시줄의 지구본 아이콘)에도 연결됨으로 나옴. 그래서 Windows에서는 지구본 아이콘을
    /// 정하는 Windows의 판정(GetNetworkConnectivityHint)을 함께 읽음. 네트워크 요청을 보내지 않고 Windows가
    /// 이미 가진 값을 읽기만 하므로 자주 불러도 부담이 없음.
    /// </para>
    /// <para>
    /// 외부 확인 주소를 막은 망이나 같은 LAN 안 서버로만 보내는 현장에서는 Windows 판정이 틀릴 수 있음.
    /// 그래서 Windows가 "인터넷 연결됨"이라고 하지 않는 동안 시작 로그가 성공하면(<see cref="ReportServerReached"/>) Windows가
    /// "인터넷 연결됨"이라고 할 때까지 어댑터 연결만 봄. 시작 로그는 이 확인을 위해 Windows 판정으로 거르지 않고 보냄.
    /// 시작 로그가 실패해 보정하지 못한 채 막히면, ApiManagerBase가 일정 간격마다 로그 하나를 거르지 않고 보내 다시 확인함
    /// (<see cref="TryStartProbe"/>).
    /// Windows 10 2004(빌드 19041)보다 오래된 Windows와 Windows가 아닌 환경에서는 어댑터 연결만 봄.
    /// </para>
    /// </summary>
    internal static class InternetConnectivity
    {
        private static InternetAccessJudge _judge = new InternetAccessJudge();

        /// <summary>네트워크 어댑터가 연결돼 있는지 여부. 인터넷에 나갈 수 있는지는 알려 주지 않음.</summary>
        public static bool IsNetworkConnected => Application.internetReachability != NetworkReachability.NotReachable;

        /// <summary>
        /// 인터넷에 나갈 수 있다고 볼지 여부. 어댑터가 끊겼거나, Windows가 "인터넷 없음"으로 판정했고 그 판정을
        /// 무시하고 있지 않으면 false임.
        /// </summary>
        public static bool IsInternetAvailable => _judge.Evaluate(IsNetworkConnected, ReadWindowsHint());

        /// <summary>
        /// 시작 로그나 확인 전송(apiUrl 로그)이 서버에 닿았음을 알림. Windows가 "인터넷 연결됨"이라고 하지 않고 있었다면 그 판정을
        /// 믿을 수 없는 것으로 보고, Windows가 "인터넷 연결됨"이라고 할 때까지 그 판정을 무시함. "인터넷 없음" 판정을 무시하기
        /// 시작할 때만 경고를 남김(판정 전이었던 경우는 정상 망의 부팅 직후에도 흔하므로 남기지 않음).
        /// 같은 LAN 안 서버 요청의 성공은 인터넷이 있다는 근거가 아니므로 여기에 알리지 않음.
        /// </summary>
        public static void ReportServerReached(Microsoft.Extensions.Logging.ILogger logger)
        {
            InternetAccessHint hint = ReadWindowsHint();
            if (!_judge.ReportServerReached(hint) || hint != InternetAccessHint.NoInternet) return;

            const string message = "[InternetConnectivity] Windows reports no internet access, but a log reached the server. Ignoring the Windows hint until it reports internet access again.";
            if (logger != null)
            {
                logger.ZLogWarning($"{message}");
            }
            else
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// 어댑터는 연결됐는데 Windows 판정 때문에 전송이 막혀 있고, 마지막 확인 전송에서 intervalSeconds가 지났으면 true를 반환함.
        /// true를 받은 호출부는 그 전송을 판정으로 거르지 않고 보내 보고, 닿으면 <see cref="ReportServerReached"/>를 부를 것.
        /// </summary>
        public static bool TryStartProbe(double now, double intervalSeconds)
        {
            return _judge.TryStartProbe(IsNetworkConnected, ReadWindowsHint(), now, intervalSeconds);
        }

        /// <summary>
        /// 도메인 리로드를 끈 에디터에서도 플레이마다 판정 무시 상태가 이전 실행에서 이어지지 않도록 초기화함.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _judge = new InternetAccessJudge();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _hintFailure = null;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static string _hintFailure;

        /// <summary>
        /// Windows 판정을 읽지 못해 어댑터만 보게 된 이유. 읽을 수 있으면 null임.
        /// 아래 경고는 Unity 로그에만 남으므로, NetworkStatusService가 시작 로그(ZLogger)에 함께 적음.
        /// </summary>
        internal static string HintFailure => _hintFailure;

        /// <summary>
        /// Windows의 인터넷 판정을 읽음. 함수가 없거나(Windows 10 2004 이전) 호출이 실패하면 경고를 한 번 남기고,
        /// 이후에는 읽지 않고 Unknown을 반환함(1초마다 불리므로 매번 예외·로그를 만들지 않음).
        /// </summary>
        internal static InternetAccessHint ReadWindowsHint()
        {
            if (_hintFailure != null) return InternetAccessHint.Unknown;

            string failure;
            try
            {
                int result = NativeMethods.GetNetworkConnectivityHint(out NativeMethods.NetworkConnectivityHint hint);
                if (result == 0)
                {
                    return InternetAccessJudge.FromWindowsLevel(hint.ConnectivityLevel);
                }

                failure = $"error code {result}";
            }
            catch (EntryPointNotFoundException)
            {
                failure = "GetNetworkConnectivityHint is not available (requires Windows 10 2004 or later)";
            }
            catch (DllNotFoundException)
            {
                failure = "iphlpapi.dll was not found";
            }

            _hintFailure = failure;
            Debug.LogWarning($"[InternetConnectivity] Could not read the Windows internet hint ({failure}); checking the network adapter only.");
            return InternetAccessHint.Unknown;
        }

        private static class NativeMethods
        {
            /// <summary>NL_NETWORK_CONNECTIVITY_HINT. 필드 순서와 크기는 Windows 헤더(nldef.h)와 같아야 함.</summary>
            [StructLayout(LayoutKind.Sequential)]
            public struct NetworkConnectivityHint
            {
                public int ConnectivityLevel;
                public int ConnectivityCost;
                public byte ApproachingDataLimit;
                public byte OverDataLimit;
                public byte Roaming;
            }

            [DllImport("iphlpapi.dll")]
            public static extern int GetNetworkConnectivityHint(out NetworkConnectivityHint hint);
        }
#else
        /// <summary>Windows가 아닌 환경에서는 판정을 읽지 않으므로 실패 이유도 없음.</summary>
        internal static string HintFailure => null;

        /// <summary>
        /// Windows가 아닌 환경에는 같은 판정이 없으므로 항상 Unknown을 반환해 어댑터 연결만 보게 함.
        /// </summary>
        internal static InternetAccessHint ReadWindowsHint()
        {
            return InternetAccessHint.Unknown;
        }
#endif
    }
}
