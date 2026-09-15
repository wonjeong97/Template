using System;
using Microsoft.Extensions.Logging;
using R3;
using UnityEngine;
using VContainer.Unity;
using ZLogger;

namespace Wonjeong.Network
{
    /// <summary>
    /// 실시간 네트워크 연결 상태를 모니터링하는 서비스.
    /// 연결 상태 변경, 연결 끊김 및 복구 이벤트를 R3 스트림으로 제공함.
    /// 애초에 네트워크 연결이 없는 오프라인 독립형 키오스크 환경에서는
    /// 시작 시 불필요한 끊김 알림이 발생하지 않도록 방어함.
    /// </summary>
    public class NetworkStatusService : IInitializable, ITickable, IDisposable
    {
        private readonly ILogger<NetworkStatusService> _logger;
        private readonly float _checkIntervalSeconds;

        private float _elapsed;
        private NetworkReachability _lastReachability;
        private bool _hasEverConnected;

        private readonly Subject<NetworkReachability> _reachabilitySubject = new Subject<NetworkReachability>();
        private readonly Subject<Unit> _networkLostSubject = new Subject<Unit>();
        private readonly Subject<Unit> _networkRestoredSubject = new Subject<Unit>();

        /// <summary>현재 네트워크 도달성 상태.</summary>
        public NetworkReachability CurrentReachability { get; private set; }

        /// <summary>현재 네트워크가 연결되어 있는지 여부.</summary>
        public bool IsConnected => CurrentReachability != NetworkReachability.NotReachable;

        /// <summary>네트워크 도달성 상태가 변경될 때마다 발행되는 스트림.</summary>
        public Observable<NetworkReachability> OnReachabilityChanged => _reachabilitySubject;

        /// <summary>
        /// 네트워크 연결이 끊어졌을 때 발행되는 스트림.
        /// (단, 최초 시작 시점부터 오프라인이었던 환경에서는 이전에 한 번이라도 연결된 적이 있을 때만 발행됨)
        /// </summary>
        public Observable<Unit> OnNetworkLost => _networkLostSubject;

        /// <summary>네트워크 연결이 복구(또는 최초 연결)되었을 때 발행되는 스트림.</summary>
        public Observable<Unit> OnNetworkRestored => _networkRestoredSubject;

        public NetworkStatusService(ILogger<NetworkStatusService> logger = null, float checkIntervalSeconds = 1.0f)
        {
            _logger = logger;
            _checkIntervalSeconds = Mathf.Max(0.2f, checkIntervalSeconds);
        }

        public void Initialize()
        {
            CurrentReachability = Application.internetReachability;
            _lastReachability = CurrentReachability;
            _hasEverConnected = (CurrentReachability != NetworkReachability.NotReachable);

            if (_logger != null)
            {
                _logger.ZLogInformation($"[NetworkStatusService] Initial reachability: {CurrentReachability}");
            }
        }

        public void Tick()
        {
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < _checkIntervalSeconds) return;
            _elapsed = 0f;

            NetworkReachability current = Application.internetReachability;
            if (current == _lastReachability) return;

            NetworkReachability previous = _lastReachability;
            _lastReachability = current;
            CurrentReachability = current;

            _reachabilitySubject.OnNext(current);

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

        public void Dispose()
        {
            _reachabilitySubject?.OnCompleted();
            _networkLostSubject?.OnCompleted();
            _networkRestoredSubject?.OnCompleted();

            _reachabilitySubject?.Dispose();
            _networkLostSubject?.Dispose();
            _networkRestoredSubject?.Dispose();
        }
    }
}
