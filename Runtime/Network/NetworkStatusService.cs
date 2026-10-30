using System;
using Microsoft.Extensions.Logging;
using R3;
using UnityEngine;
using VContainer.Unity;
using ZLogger;

namespace HuliacDev.Network
{
    /// <summary>
    /// 실시간 네트워크 연결 상태를 모니터링하는 서비스.
    /// 연결 상태 변경, 연결 끊김 및 복구 이벤트를 R3 스트림으로 제공함.
    /// 연결 여부는 ApiRetryUtil의 전송 전 확인과 같은 판정(InternetConnectivity)을 쓰므로, 어댑터가 연결돼 있어도
    /// Windows가 인터넷 없음으로 판정하면(작업 표시줄의 지구본 아이콘) 끊긴 것으로 봄.
    /// 애초에 네트워크 연결이 없는 오프라인 독립형 키오스크 환경에서는
    /// 시작 시 불필요한 끊김 알림이 발생하지 않도록 방어함.
    /// </summary>
    public class NetworkStatusService : IInitializable, ITickable, IDisposable
    {
        private readonly ILogger<NetworkStatusService> _logger;
        private readonly float _checkIntervalSeconds;

        private float _elapsed;
        private bool _hasEverConnected;

        // 생성 시점의 실제 도달성으로 초기화한다. NotReachable로 시작하면 Initialize() 전에
        // 구독한 소비자가 실제 상태 직전에 NotReachable을 한 번 받는 깜빡임이 생긴다.
        private readonly ReactiveProperty<NetworkReachability> _reachability =
            new ReactiveProperty<NetworkReachability>(ReadReachability());
        private readonly Subject<Unit> _networkLostSubject = new Subject<Unit>();
        private readonly Subject<Unit> _networkRestoredSubject = new Subject<Unit>();

        /// <summary>
        /// 현재 네트워크 도달성 상태이자 그 변경 스트림.
        /// 상태이므로 구독 즉시 현재 값을 한 번 받고, 이후 변경될 때마다 발행됨.
        /// 인터넷에 나갈 수 있다고 볼 때는 어댑터의 연결 방식(LAN/이동통신)을, 아니면 NotReachable을 담음.
        /// </summary>
        public ReadOnlyReactiveProperty<NetworkReachability> Reachability => _reachability;

        /// <summary>현재 네트워크 도달성 상태.</summary>
        public NetworkReachability CurrentReachability => _reachability.Value;

        /// <summary>현재 네트워크가 연결되어 있는지 여부.</summary>
        public bool IsConnected => CurrentReachability != NetworkReachability.NotReachable;

        /// <summary>
        /// 네트워크 연결이 끊어졌을 때 발행되는 스트림.
        /// (단, 최초 시작 시점부터 오프라인이었던 환경에서는 이전에 한 번이라도 연결된 적이 있을 때만 발행됨)
        /// </summary>
        public Observable<Unit> OnNetworkLost => _networkLostSubject;

        /// <summary>네트워크 연결이 복구(또는 최초 연결)되었을 때 발행되는 스트림.</summary>
        public Observable<Unit> OnNetworkRestored => _networkRestoredSubject;

        /// <summary>
        /// 로거와 검사 주기를 주입받아 초기화함.
        /// </summary>
        public NetworkStatusService(ILogger<NetworkStatusService> logger = null, float checkIntervalSeconds = 1.0f)
        {
            _logger = logger;
            _checkIntervalSeconds = Mathf.Max(0.2f, checkIntervalSeconds);
        }

        /// <summary>
        /// 시작 시점의 도달성을 읽어 초기 상태로 반영함.
        /// </summary>
        public void Initialize()
        {
            _reachability.Value = ReadReachability();
            _hasEverConnected = IsConnected;

            if (_logger != null)
            {
                // 어댑터와 Windows 판정을 함께 남겨, 끊김으로 본 원인이 어느 쪽인지 로그만으로 구분할 수 있게 함.
                InternetAccessHint hint = InternetConnectivity.ReadWindowsHint();
                string hintFailure = InternetConnectivity.HintFailure;
                if (hintFailure == null)
                {
                    _logger.ZLogInformation($"[NetworkStatusService] Initial reachability: {CurrentReachability} (adapter: {Application.internetReachability}, Windows hint: {hint})");
                }
                else
                {
                    _logger.ZLogWarning($"[NetworkStatusService] Initial reachability: {CurrentReachability} (adapter: {Application.internetReachability}, Windows hint unavailable: {hintFailure}; checking the adapter only)");
                }
            }
        }

        /// <summary>
        /// 인터넷에 나갈 수 있다고 보면 어댑터의 도달성을, 아니면 NotReachable을 반환함.
        /// </summary>
        private static NetworkReachability ReadReachability()
        {
            return InternetConnectivity.IsInternetAvailable ? Application.internetReachability : NetworkReachability.NotReachable;
        }

        /// <summary>
        /// 지정된 주기마다 도달성을 확인하고 변경 시 상태와 이벤트를 발행함.
        /// </summary>
        public void Tick()
        {
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < _checkIntervalSeconds) return;
            _elapsed = 0f;

            NetworkReachability current = ReadReachability();
            NetworkReachability previous = _reachability.Value;
            if (current == previous) return;

            // ReactiveProperty는 값이 실제로 바뀔 때만 구독자에게 발행함.
            _reachability.Value = current;

            if (current == NetworkReachability.NotReachable)
            {
                // 이전에 연결된 이력이 있는 경우에만 네트워크 유실 이벤트를 발행함.
                if (_hasEverConnected)
                {
                    if (_logger != null)
                    {
                        _logger.ZLogWarning($"[NetworkStatusService] Network connection lost (was: {previous}).");
                    }
                    _networkLostSubject.OnNext(Unit.Default);
                }
            }
            else
            {
                if (_logger != null)
                {
                    _logger.ZLogInformation($"[NetworkStatusService] Network connection restored/established: {current}");
                }
                _networkRestoredSubject.OnNext(Unit.Default);
                _hasEverConnected = true;
            }
        }

        /// <summary>
        /// 컨테이너 파기 시 모든 스트림을 완료 처리하고 해제함.
        /// </summary>
        public void Dispose()
        {
            _networkLostSubject?.OnCompleted();
            _networkRestoredSubject?.OnCompleted();

            _reachability?.Dispose();
            _networkLostSubject?.Dispose();
            _networkRestoredSubject?.Dispose();
        }
    }
}
