using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using R3;
using Unity.Profiling;
using UnityEngine;
using VContainer;
using HuliacDev.Utils;
using ZLogger;

namespace HuliacDev.Hardware
{
    /// <summary>
    /// 아두이노 등 시리얼 장치와 핸드셰이크로 포트를 찾아 연결하고, 전용 스레드로 줄 단위 수신을 받아 메인 스레드로 넘기는 매니저.
    /// 연결된 뒤 끊기면 간격을 늘려 가며 자동으로 다시 연결함.
    /// </summary>
    public class ArduinoManager : MonoBehaviour
    {
        /// <summary>전체 포트 스캔을 반복할 기본 최대 횟수.</summary>
        public const int DefaultMaxRetries = 10;

        /// <summary>재시도 사이의 기본 대기 시간(ms).</summary>
        public const int DefaultRetryDelayMs = 1000;

        /// <summary>
        /// 포트를 연 뒤 핸드셰이크를 기다리는 기본 시간(ms). 포트를 열면 DTR 때문에 보드가 리셋되고, 부트로더를 거쳐 스케치가
        /// 첫 줄을 보내기까지 Uno·Nano 계열에서 약 1.5~2초가 걸리므로 그보다 넉넉히 잡음.
        /// </summary>
        public const int DefaultHandshakeTimeoutMs = 3000;

        /// <summary>핸드셰이크 대기 시간의 최소값(ms). 너무 짧으면 리셋 뒤 부팅을 기다리지 못해 연결되지 않음.</summary>
        private const int MinHandshakeTimeoutMs = 500;

        /// <summary>핸드셰이크 중 쓰기를 기다리는 시간(ms).</summary>
        private const int HandshakeWriteTimeoutMs = 500;

        /// <summary>연결된 뒤 읽기 스레드가 한 줄을 기다리는 시간(ms). 연결 해제 대기(ReadThreadJoinTimeoutMs)보다 짧아야 함.</summary>
        private const int ReadLoopTimeoutMs = 500;

        /// <summary>핸드셰이크를 기다리며 바이트 하나를 기다리는 시간(ms). 받은 바이트가 없을 때 취소를 이 간격으로 알아챔.</summary>
        private const int HandshakeReadTimeoutMs = 100;

        /// <summary>핸드셰이크로 확인할 한 줄의 최대 길이(바이트). 줄바꿈 없이 계속 보내는 장치에서도 이 길이마다 한 줄로 봄.</summary>
        private const int MaxHandshakeLineLength = 256;

        /// <summary>동시 스캔에서 포트 열기가 늦게 끝나는 드라이버를 핸드셰이크 대기보다 더 기다려 주는 시간(ms).</summary>
        private const int PortOpenGraceMs = 2000;

        /// <summary>연결을 끊을 때 읽기 스레드가 끝나기를 기다리는 최대 시간(ms).</summary>
        private const int ReadThreadJoinTimeoutMs = 600;

        /// <summary>자동 재연결 간격의 최소값(ms). 잘못된 설정으로 쉬지 않고 포트를 스캔하지 않게 함.</summary>
        private const int MinReconnectDelayMs = 1000;

        private static readonly ProfilerMarker DispatchMarker = new ProfilerMarker("ArduinoManager.DispatchMessage");
        private static readonly ProfilerMarker JoinReadThreadMarker = new ProfilerMarker("ArduinoManager.JoinReadThread");

        private readonly Subject<string> _messageSubject = new Subject<string>();
        private readonly Subject<Unit> _connectedSubject = new Subject<Unit>();
        private readonly Subject<Unit> _disconnectedSubject = new Subject<Unit>();

        // DisconnectInternal()은 메인 스레드(OnDestroy/OnApplicationQuit/Disconnect)와 읽기 스레드(읽기 오류) 양쪽에서
        // 호출될 수 있으므로 상태 전이를 직렬화함.
        private readonly object _connectionLock = new object();

        // 첫 연결(ConnectAsync)의 포트 스캔을 한 번에 하나만 돌게 함. 새 호출이 앞선 호출을 멈추라고 한 뒤에도
        // 스레드 풀에서 돌던 앞선 스캔은 끝까지 진행되므로, 그 스캔이 포트를 닫고 끝날 때까지 기다렸다가 스캔함.
        // 기다리지 않으면 두 스캔이 같은 COM 포트를 동시에 열려다 한쪽이 접근 거부로 실패함.
        // 열기가 끝나지 않아 앞선 스캔이 남겨 둔 포트는 다음 스캔이 건너뜀(PortScan).
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);

        private SerialPort _serialPort;
        private Thread _readThread;
        private ILogger<ArduinoManager> _logger;
        private string _lastExpectedHandshake;
        private CancellationTokenSource _reconnectCts;

        // 진행 중인 첫 연결(ConnectAsync)의 스캔을 수동 해제로 멈추기 위한 토큰 소스(파괴 토큰과 연결함).
        private CancellationTokenSource _connectCts;

        private int _lastBaudRate;

        // ConnectAsync를 부를 때마다 늘리는 번호. 스캔 차례를 기다리는 사이 더 새 호출이 오면 앞선 호출은 스캔하지 않음.
        private int _connectGeneration;

        private bool _isOriginal;
        private volatile bool _isRunning;
        private bool _isReconnecting;

        // 수동으로 끊은 뒤에는 읽기 오류로 들어온 자동 재연결 요청도 시작하지 않도록 표시함. 다음 ConnectAsync에서 풂.
        private volatile bool _isManuallyDisconnected;

        // OnDestroy가 파괴 토큰 취소보다 먼저 불려도 ConnectAsync가 파괴로 인한 취소를 수동 해제로 착각하지 않게 함.
        private bool _isDestroyed;

        /// <summary>
        /// 외부에서 구독할 수 있는 아두이노 수신 데이터 스트림.
        /// 백그라운드 스레드 데이터를 유니티 메인 스레드로 안전하게 전달함.
        /// </summary>
        public Observable<string> OnDataReceived => _messageSubject.ObserveOnMainThread();

        /// <summary>연결 성공 시 발행되는 스트림. 현재 상태가 아니라 이벤트이므로, 구독 시점의 상태는 IsConnected로 읽음.</summary>
        public Observable<Unit> OnConnected => _connectedSubject.ObserveOnMainThread();

        /// <summary>연결 끊김 발생 시 발행되는 스트림.</summary>
        public Observable<Unit> OnDisconnected => _disconnectedSubject.ObserveOnMainThread();

        /// <summary>연결된 뒤 끊어졌을 때 자동으로 재연결을 시도할지 여부. 첫 연결(ConnectAsync)이 실패한 경우에는 동작하지 않음.</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 포트마다 핸드셰이크를 기다리는 시간(ms). 이 시간 안에 받은 줄 중 하나라도 핸드셰이크를 포함하면 연결함(최소 0.5초).
        /// 모든 포트를 동시에 확인하므로 스캔 한 번은 포트 수와 상관없이 보통 이 시간 안에 끝나고(열기가 막히는 포트가 있으면 최대 2초 더),
        /// 맞는 포트를 찾으면 그때 바로 끝남.
        /// </summary>
        public int HandshakeTimeoutMs { get; set; } = DefaultHandshakeTimeoutMs;

        /// <summary>자동 재연결의 첫 대기 시간(ms). 실패할 때마다 두 배로 늘어 AutoReconnectMaxIntervalMs에서 멈추고, 다시 끊기면 이 값부터 시작함.</summary>
        public int AutoReconnectIntervalMs { get; set; } = 3000;

        /// <summary>
        /// 자동 재연결 대기 시간의 상한(ms). 장치가 오래 꺼져 있어도 이 간격보다 드물게 시도하지 않음.
        /// AutoReconnectIntervalMs가 더 크면 그 값을 상한으로 쓰고, 1초보다 작게 두어도 1초로 봄(쉬지 않고 스캔하지 않게 함).
        /// </summary>
        public int AutoReconnectMaxIntervalMs { get; set; } = 30000;

        /// <summary>
        /// 시리얼 포트가 열려 있는지 여부.
        /// 읽기 스레드의 DisconnectInternal이 _serialPort를 언제든 null로 바꿀 수 있으므로,
        /// 필드를 두 번 읽지 않고 지역 변수에 한 번 복사해 검사함(검사와 사용 사이 NRE 방지).
        /// </summary>
        public bool IsConnected
        {
            get
            {
                SerialPort port = _serialPort;
                return port != null && port.IsOpen;
            }
        }

        /// <summary>
        /// VContainer 의존성 주입으로 로거를 할당함.
        /// </summary>
        [Inject]
        public void Construct(ILogger<ArduinoManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 중복 생성 시 기존 객체를 보존하고 새로 생성된 객체를 파괴함.
        /// </summary>
        private void Awake()
        {
            if (SingletonGuard<ArduinoManager>.CheckDuplicate(this, out _isOriginal))
            {
                return;
            }
        }

        /// <summary>
        /// 아두이노 장치와 시리얼 통신 연결을 비동기로 시도함.
        /// expectedHandshake로 장치를 식별하며, 전체 포트 스캔을 maxRetries만큼 반복함.
        /// 부팅 직후 장치 인식이 늦는 환경에서는 maxRetries를 늘리고, 빠른 실패가 필요하면 줄임.
        /// 모두 실패하면 마지막 스캔에서 포트별로 실패한 이유를 경고 로그에 남김.
        /// 앞선 ConnectAsync는 멈추고, 앞선 스캔이나 진행 중인 자동 재연결 스캔이 끝난 뒤 스캔함. 기다리는 사이 더 새 ConnectAsync가 오거나
        /// Disconnect가 불리면 스캔하지 않고 끝나므로, 끝난 뒤 연결됐는지는 IsConnected로 확인함.
        /// </summary>
        public async UniTask ConnectAsync(int baudRate, string expectedHandshake,
            int maxRetries = DefaultMaxRetries, int retryDelayMs = DefaultRetryDelayMs)
        {
            if (string.IsNullOrEmpty(expectedHandshake))
            {
                if (_logger != null) _logger.ZLogError($"[ArduinoManager] expectedHandshake is empty. Cannot identify the device.");
                return;
            }

            // 대기 중 파괴되면 파괴 토큰을 얻는 것 자체가 실패할 수 있으므로, 살아 있을 때 한 번 얻어 넘김.
            CancellationToken destroyToken = this.GetCancellationTokenOnDestroy();

            _lastBaudRate = baudRate;
            _lastExpectedHandshake = expectedHandshake;
            _isManuallyDisconnected = false;

            if (IsConnected) return;

            if (maxRetries < 1) maxRetries = 1;
            if (retryDelayMs < 0) retryDelayMs = 0;

            // 앞서 진행 중인 첫 연결이 있으면 멈추게 하고, 그 스캔이 끝날 때까지 기다림(파괴되면 기다림을 멈추고 OCE를 전달함).
            // 자동 재연결은 멈추지 않음(이 연결이 실패해도 장치가 돌아오면 다시 연결되게 함). 두 스캔은 같은 차례(_connectGate)를
            // 거쳐 같은 포트를 동시에 열지 않음.
            int generation = Interlocked.Increment(ref _connectGeneration);
            StopConnect();
            await _connectGate.WaitAsync(destroyToken);

            try
            {
                await ConnectWithGateAsync(generation, baudRate, expectedHandshake, maxRetries, retryDelayMs, destroyToken);
            }
            finally
            {
                _connectGate.Release();
            }
        }

        /// <summary>
        /// 스캔 차례를 얻은 뒤 실제로 포트를 스캔해 연결함. 기다리는 사이 더 새 ConnectAsync가 왔거나 수동 해제됐거나
        /// 이미 연결됐으면 스캔하지 않으며, 이번 연결은 수동 해제(Disconnect)나 새 ConnectAsync로도 멈출 수 있게 함.
        /// </summary>
        private async UniTask ConnectWithGateAsync(int generation, int baudRate, string expectedHandshake, int maxRetries, int retryDelayMs,
            CancellationToken destroyToken)
        {
            // 차례를 얻은 직후 파괴됐으면 OnDestroy가 수동 해제 표시를 세워도 파괴 취소로 전달함(예전처럼 OCE).
            if (_isDestroyed) throw new OperationCanceledException(destroyToken);
            destroyToken.ThrowIfCancellationRequested();

            if (generation != Volatile.Read(ref _connectGeneration))
            {
                if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Connection attempt skipped because a newer ConnectAsync was called.");
                return;
            }

            if (_isManuallyDisconnected)
            {
                if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Connection attempt stopped before scanning because Disconnect was called.");
                return;
            }

            if (IsConnected) return;

            CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(destroyToken);
            _connectCts = connectCts;

            try
            {
                (bool isSuccess, string lastFailures) = await ExecuteConnectionRetriesAsync(baudRate, expectedHandshake, maxRetries, retryDelayMs, connectCts.Token);

                if (!isSuccess && _logger != null)
                {
                    _logger.ZLogWarning($"[ArduinoManager] Failed to connect to Arduino after {maxRetries} attempts. Last scan: {lastFailures}");
                }
            }
            catch (OperationCanceledException) when (!destroyToken.IsCancellationRequested && !_isDestroyed)
            {
                // 수동 해제나 새 ConnectAsync로 멈춘 경우. 파괴로 인한 취소만 예전처럼 호출자에게 전달함.
                if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Connection attempt stopped.");
            }
            finally
            {
                Interlocked.CompareExchange(ref _connectCts, null, connectCts);
                connectCts.Dispose();
            }
        }

        /// <summary>
        /// 진행 중인 첫 연결(ConnectAsync)의 스캔과 대기를 멈춤. 해제는 ConnectAsync가 끝날 때 맡음.
        /// </summary>
        private void StopConnect()
        {
            CancellationTokenSource cts = Interlocked.Exchange(ref _connectCts, null);
            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 다른 스레드에서 Disconnect를 부르는 사이 ConnectAsync가 끝나 이미 해제된 경우로, 멈출 연결이 없음.
            }
        }

        /// <summary>
        /// 지정된 횟수만큼 포트 스캔과 대기를 반복하고, 실패하면 마지막 스캔의 포트별 실패 이유를 돌려줌.
        /// 시도마다 로그를 남기지 않고 결과만 남김(통신 로그는 상태가 바뀔 때만 남기는 규칙).
        /// </summary>
        private async UniTask<(bool isSuccess, string lastFailures)> ExecuteConnectionRetriesAsync(int baudRate, string expectedHandshake,
            int maxRetries, int delayMs, CancellationToken cancellationToken)
        {
            string lastFailures = string.Empty;

            for (int i = 0; i < maxRetries; i++)
            {
                // 기다리는 사이 다른 경로로 이미 연결됐으면 더 스캔하지 않고 성공으로 끝냄(연결됐는데 실패 경고가 남지 않게 함).
                if (IsConnected) return (true, string.Empty);

                (bool isEstablished, string failures) = await TryEstablishConnectionAsync(baudRate, expectedHandshake, cancellationToken);
                if (isEstablished)
                {
                    return (true, string.Empty);
                }

                lastFailures = failures;

                if (i < maxRetries - 1)
                {
                    await UniTask.Delay(delayMs, cancellationToken: cancellationToken);
                }
            }

            return (false, lastFailures);
        }

        /// <summary>
        /// 백그라운드 스레드에서 포트를 스캔해 핸드셰이크가 맞는 포트를 찾고, 메인 스레드 복귀 후 연결을 할당함.
        /// 스캔하는 사이 취소(파괴, 수동 해제로 인한 재연결 중단)되면 찾은 포트가 있어도 닫고 연결하지 않으며,
        /// OperationCanceledException을 던져 재시도를 멈춤(파괴면 호출자에게 전달되고, 수동 해제면 중단 로그로 끝남).
        /// </summary>
        private async UniTask<(bool isEstablished, string failures)> TryEstablishConnectionAsync(int baudRate, string expectedHandshake,
            CancellationToken cancellationToken)
        {
            // 스레드 풀에서 속성을 읽지 않도록 메인 스레드에서 값을 정해 넘김.
            int handshakeTimeoutMs = Math.Max(MinHandshakeTimeoutMs, HandshakeTimeoutMs);
            (SerialPort validPort, string failures) = await UniTask.RunOnThreadPool(() => TryHandshake(baudRate, expectedHandshake, handshakeTimeoutMs, cancellationToken));

            // 취소된 스캔은 실패가 아니라 취소로 전달함(마지막 재시도여도 실패 경고 대신 중단으로 처리되게 함).
            if (cancellationToken.IsCancellationRequested)
            {
                if (validPort != null) ClosePortQuietly(validPort);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (validPort == null)
            {
                return (false, failures);
            }

            EstablishConnection(validPort, validPort.PortName);
            return (true, string.Empty);
        }

        /// <summary>
        /// 사용 가능한 모든 시리얼 포트를 동시에 열어 핸드셰이크 응답을 확인함. 포트마다 보드 부팅을 기다려야 해서 차례로 확인하면
        /// 응답 없는 포트 수만큼 스캔이 늦어지므로, 포트마다 확인 스레드를 하나씩 띄움.
        /// 맞는 포트를 찾으면 그 SerialPort를, 실패하면 null과 포트별 실패 이유를 돌려줌. 하나가 맞으면 나머지 포트를 닫은 뒤 돌아오고,
        /// 두 포트가 함께 맞으면 먼저 응답한 포트를 씀. 모든 포트를 한 번씩 열므로 다른 코드가 아직 열지 않은 다른 보드도 리셋되며,
        /// 이미 열린 포트는 접근 거부로 건너뜀. 수동 해제·파괴로 취소되면 모든 포트를 닫고 멈춰, 해제한 뒤에도 포트를 점유하지 않게 함.
        /// </summary>
        private static (SerialPort validPort, string failures) TryHandshake(int baudRate, string expectedHandshake, int handshakeTimeoutMs,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return (null, "canceled");

            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0) return (null, "no serial ports");

            PortScan scan = new PortScan(ports, baudRate, expectedHandshake, handshakeTimeoutMs, cancellationToken);
            return scan.Run();
        }

        /// <summary>
        /// 동시 스캔 한 번의 상태. 포트마다 띄운 확인 스레드가 결과를 여기 모으고, 스캔을 기다리는 스레드는 맞는 포트가 나오거나
        /// 모든 포트 확인이 끝나거나 대기 한도가 지나면 결과를 가져감. 그 뒤에 끝난 확인 스레드가 찾은 포트는 받을 곳이 없으므로 닫음.
        /// </summary>
        private sealed class PortScan
        {
            // 확인 스레드가 아직 잡고 있는 포트. 스캔이 끝난 뒤에도 열기가 끝나지 않은 스레드가 남을 수 있어,
            // 다음 스캔이 같은 포트에 스레드를 또 띄우지 않도록 스캔 사이에 공유함.
            private static readonly HashSet<string> ProbingPorts = new HashSet<string>();

            private readonly object _sync = new object();
            private readonly string[] _ports;
            private readonly string[] _failureReasons;
            private readonly string _expectedHandshake;

            // 다른 포트에서 찾았거나 스캔을 끝낸 뒤 남은 확인 스레드를 멈춤. 늦게 끝나는 스레드가 Cancel 뒤에도 토큰을 읽으므로
            // Dispose하지 않음(대기 핸들·타이머를 만들지 않아 해제할 자원이 없음).
            private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
            private readonly CancellationToken _cancellationToken;
            private SerialPort _foundPort;

            private readonly int _baudRate;
            private readonly int _handshakeTimeoutMs;
            private int _remainingCount;

            // Open()이 아직 돌아오지 않은 확인 스레드 수. 이 스레드들은 포트를 잡고 있지 않으므로 스캔을 끝낼 때 기다리지 않음.
            // 스캔이 끝난 뒤 열기가 끝나면 멈추라는 신호를 보고 곧 닫으며, 그 짧은 동안 다음 스캔은 그 포트를 건너뜀(다음 재시도에서 확인).
            private int _openingCount;

            private bool _isFinished;

            public PortScan(string[] ports, int baudRate, string expectedHandshake, int handshakeTimeoutMs, CancellationToken cancellationToken)
            {
                _ports = ports;
                _failureReasons = new string[ports.Length];
                _expectedHandshake = expectedHandshake;
                _cancellationToken = cancellationToken;
                _baudRate = baudRate;
                _handshakeTimeoutMs = handshakeTimeoutMs;
                _remainingCount = ports.Length;
            }

            /// <summary>
            /// 포트마다 확인 스레드를 띄우고, 맞는 포트가 나오거나 모든 확인이 끝날 때까지 기다림. 맞는 포트를 찾았거나 취소되면
            /// 나머지 스레드를 멈추고, 포트를 연 스레드가 포트를 닫을 때까지(최대 HandshakeReadTimeoutMs 남짓) 기다린 뒤 돌아옴
            /// (바로 이어지는 다음 스캔이 아직 닫히지 않은 포트를 열다 접근 거부로 실패하지 않게 함). Open()이 끝나지 않은 스레드는
            /// 포트를 잡고 있지 않으므로 기다리지 않으며, 열기가 끝나면 스스로 닫고 그동안 다음 스캔은 그 포트를 건너뜀.
            /// 대기는 handshakeTimeoutMs에 PortOpenGraceMs를 더한 시간을 넘지 않음.
            /// </summary>
            public (SerialPort validPort, string failures) Run()
            {
                for (int i = 0; i < _ports.Length; i++)
                {
                    StartProbe(i);
                }

                int waitLimitMs = _handshakeTimeoutMs + PortOpenGraceMs;
                System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();

                lock (_sync)
                {
                    while (_foundPort == null && _remainingCount > 0)
                    {
                        // 취소됐으면 포트를 연 스레드만 끝나기를 기다림(열기가 막힌 포트 때문에 해제가 늦어지지 않게).
                        if (_cancellationToken.IsCancellationRequested && _remainingCount <= _openingCount) break;
                        if (!WaitForProbe(elapsed, waitLimitMs)) break;
                    }
                }

                // 맞는 포트를 찾았거나 취소·한도로 끝내면 남은 확인 스레드를 멈추고, 포트를 연 스레드가 닫고 끝날 때까지 기다림.
                _stopCts.Cancel();

                SerialPort foundPort;
                string failures = string.Empty;
                lock (_sync)
                {
                    while (_remainingCount > _openingCount)
                    {
                        if (!WaitForProbe(elapsed, waitLimitMs)) break;
                    }

                    _isFinished = true;
                    foundPort = _foundPort;
                    if (foundPort == null) failures = BuildFailures();
                }

                return (foundPort, failures);
            }

            /// <summary>
            /// 포트 하나의 확인 스레드를 띄움. 앞선 스캔의 확인 스레드가 아직 그 포트를 잡고 있으면(열기가 끝나지 않는 드라이버)
            /// 띄우지 않음. 띄우면 재시도마다 멈춘 스레드가 쌓이고, 열어도 접근 거부로 실패함.
            /// </summary>
            private void StartProbe(int index)
            {
                string port = _ports[index];
                bool isAlreadyProbing;
                lock (ProbingPorts)
                {
                    isAlreadyProbing = !ProbingPorts.Add(port);
                }

                if (isAlreadyProbing)
                {
                    FinishProbe(index, "previous check still running");
                    return;
                }

                try
                {
                    Thread probeThread = new Thread(() => ProbePort(index))
                    {
                        IsBackground = true,
                        Name = "ArduinoManager.PortProbe"
                    };
                    probeThread.Start();
                }
                catch (Exception e)
                {
                    // 스레드를 만들지 못하면 그 포트를 확인 중 목록에서 빼 다음 스캔이 다시 확인하게 함.
                    lock (ProbingPorts)
                    {
                        ProbingPorts.Remove(port);
                    }

                    FinishProbe(index, e.Message);
                }
            }

            /// <summary>
            /// 확인 스레드의 소식을 대기 한도 안에서 기다림. 취소를 알아채도록 HandshakeReadTimeoutMs보다 길게 기다리지 않음.
            /// 한도가 지났으면 false. 호출자가 _sync를 잡은 상태에서 부름.
            /// </summary>
            private bool WaitForProbe(System.Diagnostics.Stopwatch elapsed, int waitLimitMs)
            {
                int remainingMs = waitLimitMs - (int)elapsed.ElapsedMilliseconds;
                if (remainingMs <= 0) return false;

                Monitor.Wait(_sync, Math.Min(remainingMs, HandshakeReadTimeoutMs));
                return true;
            }

            private void ProbePort(int index)
            {
                SerialPort port = TryConnectPort(_ports[index], out string failureReason);

                if (port != null)
                {
                    bool isTaken = false;
                    lock (_sync)
                    {
                        if (_foundPort == null && !_isFinished)
                        {
                            _foundPort = port;
                            isTaken = true;
                        }
                    }

                    // 다른 포트가 먼저 맞았거나 스캔이 이미 끝났으면 받을 곳이 없으므로 닫음.
                    if (!isTaken) ClosePortQuietly(port);
                }

                lock (ProbingPorts)
                {
                    ProbingPorts.Remove(_ports[index]);
                }

                // 포트를 닫은 뒤에 끝났다고 알려, Run이 돌아올 때는 맞은 포트 말고 열린 포트가 남지 않게 함.
                FinishProbe(index, failureReason);
            }

            private void FinishProbe(int index, string failureReason)
            {
                lock (_sync)
                {
                    _failureReasons[index] = failureReason;
                    _remainingCount--;
                    Monitor.PulseAll(_sync);
                }
            }

            private void AddOpeningCount(int delta)
            {
                lock (_sync)
                {
                    _openingCount += delta;
                    Monitor.PulseAll(_sync);
                }
            }

            /// <summary>
            /// 포트 하나를 열고 핸드셰이크 대기 시간 동안 들어오는 줄을 차례로 확인함(포트를 열면 보드가 리셋되므로 리셋 직후의
            /// 깨진 첫 줄이나 다른 출력이 먼저 와도 놓치지 않게 함). 한 바이트씩 읽어 직접 줄로 나누므로 줄이 들어오는 도중에
            /// 취소를 확인해도 앞부분을 잃지 않고(Mono의 ReadLine은 시간 초과 때 읽던 줄을 버림), 핸드셰이크 줄 뒤에 온 바이트는
            /// 읽지 않고 남겨 연결 뒤 읽기 스레드가 받게 함. 시간 제한이 짧아 받은 바이트가 없을 때도 취소를 곧 알아챔.
            /// 확인 스레드에서 실행되므로 매니저 상태를 바꾸지 않고 포트 객체만 반환하며, 실패하면 이유를 failureReason에 담음.
            /// </summary>
            private SerialPort TryConnectPort(string port, out string failureReason)
            {
                CancellationToken stopToken = _stopCts.Token;
                SerialPort testPort = null;
                bool ownershipTransferred = false;
                failureReason = string.Empty;

                try
                {
                    // 스레드가 늦게 시작해 이미 멈추라고 했으면 포트를 열지 않음(열면 보드가 리셋되고 다음 스캔과 포트를 다툼).
                    if (_cancellationToken.IsCancellationRequested || stopToken.IsCancellationRequested)
                    {
                        failureReason = "canceled";
                        return null;
                    }

                    // 열기에 걸린 시간도 대기 시간에 넣어, 스캔 전체의 대기 한도와 같은 기준으로 끝나게 함.
                    System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
                    testPort = new SerialPort(port, _baudRate);

                    testPort.DtrEnable = true;
                    testPort.ReadTimeout = HandshakeReadTimeoutMs;
                    testPort.WriteTimeout = HandshakeWriteTimeoutMs;

                    AddOpeningCount(1);
                    try
                    {
                        testPort.Open();
                    }
                    finally
                    {
                        AddOpeningCount(-1);
                    }

                    byte[] lineBuffer = new byte[MaxHandshakeLineLength];
                    int lineLength = 0;
                    string lastReceived = null;

                    while (elapsed.ElapsedMilliseconds < _handshakeTimeoutMs
                           && !_cancellationToken.IsCancellationRequested && !stopToken.IsCancellationRequested)
                    {
                        // 시간 초과는 바이트를 읽지 않은 경우라 잃는 데이터가 없음.
                        int value;
                        try
                        {
                            value = testPort.ReadByte();
                        }
                        catch (TimeoutException)
                        {
                            continue;
                        }

                        if (value < 0) break;

                        // 줄바꿈 없이 계속 보내는 장치도 버퍼가 차면 한 줄로 보고 확인함.
                        bool isLineEnd = value == '\n';
                        if (!isLineEnd) lineBuffer[lineLength++] = (byte)value;
                        if (!isLineEnd && lineLength < lineBuffer.Length) continue;

                        string received = testPort.Encoding.GetString(lineBuffer, 0, lineLength).Trim();
                        lineLength = 0;

                        if (received.Contains(_expectedHandshake))
                        {
                            // 성공 시 포트를 닫지 않고 그대로 반환하여 메인 스레드에 소유권을 넘김. 읽기 스레드가 해제 요청을
                            // 연결 해제 대기(ReadThreadJoinTimeoutMs)보다 먼저 알아채도록 읽기 대기를 ReadLoopTimeoutMs로 둠.
                            testPort.ReadTimeout = ReadLoopTimeoutMs;
                            ownershipTransferred = true;
                            return testPort;
                        }

                        // 빈 줄은 진단에 쓸 내용이 없으므로 실패 이유로 남길 마지막 줄을 덮어쓰지 않음.
                        if (received.Length > 0) lastReceived = received;
                    }

                    // 줄바꿈 없이 끝난 마지막 내용도 진단에 남김.
                    if (lineLength > 0)
                    {
                        string partial = testPort.Encoding.GetString(lineBuffer, 0, lineLength).Trim();
                        if (partial.Length > 0) lastReceived = partial;
                    }

                    // 아두이노가 아닌 장치이거나 핸드셰이크를 보내지 않는 장치면 아무 줄도 받지 못함.
                    failureReason = _cancellationToken.IsCancellationRequested || stopToken.IsCancellationRequested
                        ? "canceled"
                        : lastReceived == null
                        ? ZString.Concat("no handshake in ", _handshakeTimeoutMs, "ms")
                        : ZString.Concat("unexpected handshake '", lastReceived, "'");
                }
                catch (Exception e)
                {
                    // 다른 프로그램이 포트를 점유하는 등 열지 못한 경우. 다른 포트 확인에는 영향이 없음.
                    failureReason = e.Message;
                }
                finally
                {
                    // 아두이노가 아닌 장치는 Open()은 성공하고 핸드셰이크가 오지 않음.
                    // 이때 포트를 닫지 않으면 OS 점유가 유지되어, 다음 재시도에서 같은 포트를 열 수 없게 되고
                    // 정작 그 포트에 장치가 있어도 영영 찾지 못함. 따라서 모든 실패 경로에서 반드시 해제함.
                    if (!ownershipTransferred && testPort != null)
                    {
                        ClosePortQuietly(testPort);
                    }
                }

                return null;
            }

            /// <summary>
            /// 포트별 실패 이유를 한 줄로 묶음. 연결에 모두 실패했을 때 한 번 남기는 용도라 할당을 아끼지 않음.
            /// </summary>
            private string BuildFailures()
            {
                string failures = string.Empty;
                for (int i = 0; i < _ports.Length; i++)
                {
                    string reason = _failureReasons[i] ?? "port open did not finish in time";
                    failures = failures.Length == 0
                        ? ZString.Concat(_ports[i], ": ", reason)
                        : ZString.Concat(failures, "; ", _ports[i], ": ", reason);
                }

                return failures;
            }
        }

        /// <summary>
        /// 포트를 해제함. 장치가 물리적으로 분리된 경우 해제에서도 예외가 날 수 있으므로 무시함.
        /// </summary>
        private static void ClosePortQuietly(SerialPort port)
        {
            try
            {
                port.Dispose();
            }
            catch (Exception)
            {
                // 이미 분리된 장치의 해제 실패는 복구할 수 없고 다음 연결에 영향이 없으므로 무시함.
            }
        }

        /// <summary>
        /// 검증 완료된 시리얼 포트를 인스턴스에 할당하고 수신 스레드를 가동함.
        /// 메인 스레드 점유율을 낮추기 위해 우선순위를 BelowNormal로 설정함.
        /// </summary>
        private void EstablishConnection(SerialPort validPort, string portName)
        {
            lock (_connectionLock)
            {
                _serialPort = validPort;
                _isRunning = true;
                StartReadThread();
            }

            if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Connection success: {portName}");
            _connectedSubject.OnNext(Unit.Default);
        }

        /// <summary>
        /// 수신 스레드를 새로 만들어 시작함. 호출자가 _connectionLock을 잡은 상태에서 부름.
        /// </summary>
        private void StartReadThread()
        {
            _readThread = new Thread(ReadSerialLoop)
            {
                Priority = System.Threading.ThreadPriority.BelowNormal
            };
            _readThread.Start();
        }

        /// <summary>
        /// DTR 핀을 제어하여 아두이노를 하드웨어 리셋함.
        /// 비동기 대기 중 오브젝트가 파괴되면 작업을 안전하게 취소함.
        /// </summary>
        public async UniTask RebootDeviceAsync()
        {
            if (!IsConnected)
            {
                if (_logger != null) _logger.ZLogWarning($"[ArduinoManager] Cannot reboot. Device is not connected.");
                return;
            }

            // 대기 중 파괴되면 파괴 토큰을 얻는 것 자체가 실패할 수 있으므로, 살아 있을 때 한 번 얻어 둠.
            CancellationToken destroyToken = this.GetCancellationTokenOnDestroy();

            try
            {
                // 기존 읽기 스레드를 확실히 종료시킨 뒤 재기동함.
                // 조인하지 않으면 이전 스레드가 살아 있는 채로 새 스레드가 붙어
                // 같은 포트를 두 스레드가 읽는 상태가 될 수 있음.
                SerialPort port = _serialPort;
                _isRunning = false;
                JoinReadThread(_readThread);

                port.DtrEnable = true;
                await UniTask.Delay(100, cancellationToken: destroyToken);
                if (IsDisconnectedDuringReboot(port)) return;
                port.DtrEnable = false;

                // 부트로더 대기
                await UniTask.Delay(2000, cancellationToken: destroyToken);
                if (IsDisconnectedDuringReboot(port)) return;

                lock (_connectionLock)
                {
                    _isRunning = true;
                    StartReadThread();
                }

                if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Device reboot complete. Clean state restored.");
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴로 인한 정상적인 취소이므로 경고를 띄우지 않음.
                if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Reboot canceled due to object destruction.");
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogWarning($"[ArduinoManager] Device reboot failed: {e.Message}");
                Disconnect();
            }
        }

        /// <summary>
        /// 재부팅을 기다리는 사이 연결이 끊겼는지(수동 해제, 다른 연결로 바뀜) 확인하고, 끊겼으면 로그를 남김.
        /// 끊긴 뒤 읽기 스레드를 다시 띄우거나 해제된 포트를 만지지 않게 하기 위함.
        /// </summary>
        private bool IsDisconnectedDuringReboot(SerialPort port)
        {
            if (_serialPort == port) return false;

            if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Reboot stopped because the device was disconnected.");
            return true;
        }

        /// <summary>
        /// 읽기 스레드가 끝나기를 제한 시간까지 기다림. 메인 스레드를 멈출 수 있어 프로파일러에 따로 보이도록 마커로 감쌈.
        /// 읽기 스레드가 스스로 부른 경우 자기 자신을 기다리면 제한 시간만큼 무의미하게 멈추므로 건너뜀.
        /// </summary>
        private static void JoinReadThread(Thread thread)
        {
            if (thread == null || !thread.IsAlive || thread == Thread.CurrentThread) return;

            using (JoinReadThreadMarker.Auto())
            {
                thread.Join(ReadThreadJoinTimeoutMs);
            }
        }

        /// <summary>
        /// 현재 열려있는 시리얼 포트를 닫고 통신 스레드를 종료함.
        /// 메인 스레드와 읽기 스레드 양쪽에서 호출되어도 안전함.
        /// 사용자가 명시적으로 호출한 경우 자동 재연결 루틴은 중지됨.
        /// </summary>
        public void Disconnect()
        {
            DisconnectInternal(true);
        }

        /// <summary>
        /// 읽기 스레드를 정지하고 시리얼 포트를 해제함. 수동 해제일 때는 자동 재연결도 함께 중단함.
        /// </summary>
        private void DisconnectInternal(bool isManual)
        {
            if (isManual)
            {
                _isManuallyDisconnected = true;
                StopConnect();
                StopAutoReconnect();
            }

            Thread threadToJoin;
            SerialPort portToDispose;
            bool wasConnected = false;

            lock (_connectionLock)
            {
                if (_serialPort == null && _readThread == null) return; // 이미 정리됨

                wasConnected = _serialPort != null;
                _isRunning = false;

                threadToJoin = _readThread;
                portToDispose = _serialPort;

                _readThread = null;
                _serialPort = null;
            }

            // 읽기 오류 발생 시 읽기 스레드가 스스로 이 메서드를 호출함.
            // (_isRunning이 이미 false이므로 루프는 다음 검사에서 정상 종료됨)
            JoinReadThread(threadToJoin);

            if (portToDispose != null)
            {
                ClosePortQuietly(portToDispose);
            }

            if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Disconnected");

            if (wasConnected)
            {
                _disconnectedSubject.OnNext(Unit.Default);

                if (!isManual && AutoReconnect && !string.IsNullOrEmpty(_lastExpectedHandshake) && _lastBaudRate > 0)
                {
                    TriggerAutoReconnect();
                }
            }
        }

        /// <summary>
        /// 백그라운드 스레드에서도 안전하게 자동 재연결 절차를 요청함.
        /// </summary>
        private void TriggerAutoReconnect()
        {
            DispatchAutoReconnectAsync().Forget();
        }

        /// <summary>
        /// 메인 스레드로 전환한 뒤 중복 실행을 걸러내고 자동 재연결 루프를 시작함.
        /// </summary>
        private async UniTaskVoid DispatchAutoReconnectAsync()
        {
            try
            {
                await UniTask.SwitchToMainThread();
                if (!this || !isActiveAndEnabled) return;
                // 읽기 오류로 끊긴 순간 수동 해제가 겹치면, 수동 해제가 이기도록 재연결을 시작하지 않음.
                if (_isManuallyDisconnected) return;
                if (_isReconnecting) return;
                StopAutoReconnect();

                _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
                AutoReconnectLoopAsync(_reconnectCts.Token).Forget();
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.ZLogWarning($"[ArduinoManager] Failed to dispatch auto-reconnect: {ex.Message}");
            }
        }

        /// <summary>
        /// 진행 중인 자동 재연결 루프를 취소하고 토큰 소스를 정리함.
        /// </summary>
        private void StopAutoReconnect()
        {
            // Disconnect는 어느 스레드에서도 부를 수 있어, 두 호출이 같은 토큰 소스를 함께 정리하지 않도록 원자적으로 꺼냄.
            CancellationTokenSource cts = Interlocked.Exchange(ref _reconnectCts, null);
            if (cts == null) return;

            cts.Cancel();
            cts.Dispose();
        }

        /// <summary>
        /// 연결에 성공하거나 취소될 때까지 간격을 두 배씩 늘려 가며(상한 AutoReconnectMaxIntervalMs) 재연결을 반복 시도함.
        /// 로그는 시작과 결과(몇 번 만에, 얼마나 걸렸는지)만 남김.
        /// </summary>
        private async UniTaskVoid AutoReconnectLoopAsync(CancellationToken cancellationToken)
        {
            _isReconnecting = true;
            int attemptCount = 0;
            // 첫 대기도 최소 간격을 지켜, 잘못된 설정(0 이하)으로 끊기자마자 쉬지 않고 스캔하지 않게 함.
            int delayMs = Math.Max(MinReconnectDelayMs, AutoReconnectIntervalMs);
            float startedAt = Time.realtimeSinceStartup;
            if (_logger != null) _logger.ZLogInformation($"[ArduinoManager] Auto-reconnect started.");

            try
            {
                while (!cancellationToken.IsCancellationRequested && !IsConnected)
                {
                    await UniTask.Delay(delayMs, DelayType.Realtime, cancellationToken: cancellationToken);
                    if (cancellationToken.IsCancellationRequested || IsConnected) break;

                    attemptCount++;
                    bool isEstablished;

                    // ConnectAsync와 같은 차례를 거쳐 두 스캔이 같은 포트를 동시에 열지 않게 함.
                    await _connectGate.WaitAsync(cancellationToken);
                    try
                    {
                        if (IsConnected) break;
                        (isEstablished, _) = await TryEstablishConnectionAsync(_lastBaudRate, _lastExpectedHandshake, cancellationToken);
                    }
                    finally
                    {
                        _connectGate.Release();
                    }

                    if (isEstablished)
                    {
                        if (!cancellationToken.IsCancellationRequested && _logger != null)
                        {
                            _logger.ZLogInformation($"[ArduinoManager] Auto-reconnect successful after {attemptCount} attempts ({Time.realtimeSinceStartup - startedAt:F0}s).");
                        }
                        break;
                    }

                    delayMs = NextReconnectDelayMs(delayMs, Math.Max(AutoReconnectMaxIntervalMs, AutoReconnectIntervalMs));
                }
            }
            catch (OperationCanceledException)
            {
                // 수동 해제나 파괴로 재연결을 멈춘 정상 취소
            }
            finally
            {
                _isReconnecting = false;
            }
        }

        /// <summary>
        /// 다음 재연결 대기 시간을 정함. 두 배로 늘리되 상한을 넘지 않고, 현재 간격이 0 이하면 1초부터 시작함.
        /// 상한이 1초보다 작으면 1초로 봐 시도마다 쉬지 않고 포트를 스캔하지 않게 함.
        /// </summary>
        internal static int NextReconnectDelayMs(int currentDelayMs, int maxDelayMs)
        {
            if (maxDelayMs < MinReconnectDelayMs) maxDelayMs = MinReconnectDelayMs;
            if (currentDelayMs < MinReconnectDelayMs) return MinReconnectDelayMs;
            if (currentDelayMs >= maxDelayMs / 2) return maxDelayMs;
            return currentDelayMs * 2;
        }

        /// <summary>
        /// 아두이노로 문자열 메시지를 전송함.
        /// </summary>
        public void Send(string msg)
        {
            // IsConnected와 같은 이유로 필드를 지역 변수에 고정한 뒤 검사·사용함.
            // 끊긴 동안 매 프레임 보내는 프로젝트가 있어, 연결되지 않은 경우는 자주 생기는 no-op으로 보고 로그를 남기지 않음.
            SerialPort port = _serialPort;
            if (port == null || !port.IsOpen) return;

            try
            {
                port.WriteLine(msg);
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogWarning($"[ArduinoManager] Send fail: {e.Message}");
            }
        }

        /// <summary>
        /// 시리얼 데이터 수신 메인 루프.
        /// </summary>
        private void ReadSerialLoop()
        {
            // DisconnectInternal()이 다른 스레드에서 _serialPort를 null로 만들 수 있으므로
            // 루프가 사용할 참조는 시작 시점에 지역 변수로 고정함.
            SerialPort port = _serialPort;

            while (IsSerialPortReady(port))
            {
                ProcessNextSerialMessage(port);
                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// 시리얼 포트가 읽기 가능한 상태인지 검증함.
        /// </summary>
        private bool IsSerialPortReady(SerialPort port)
        {
            return _isRunning && port != null && port.IsOpen;
        }

        /// <summary>
        /// 단일 메시지 수신 및 R3 스트림 발행 처리. 발행 구간만 마커로 감싸 수신 대기 시간이 섞이지 않게 함.
        /// </summary>
        private void ProcessNextSerialMessage(SerialPort port)
        {
            try
            {
                string data = port.ReadLine();

                if (!string.IsNullOrEmpty(data))
                {
                    using (DispatchMarker.Auto())
                    {
                        _messageSubject.OnNext(data);
                    }
                }
            }
            catch (TimeoutException)
            {
                // 정상적인 대기 시간 초과이므로 루프 유지를 위해 무시함.
            }
            catch (Exception e)
            {
                HandleReadException(e);
            }
        }

        /// <summary>
        /// 읽기 중 발생한 예외를 기록하고 연결을 해제함. 정상 종료 중이면 무시함.
        /// </summary>
        private void HandleReadException(Exception e)
        {
            try
            {
                if (!_isRunning)
                {
                    return;
                }

                if (_logger != null) _logger.ZLogWarning($"[ArduinoManager] Read error: {e.Message}");
                DisconnectInternal(false);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.ZLogError($"[ArduinoManager] Exception during read error handling: {ex.Message}");
            }
        }

        /// <summary>
        /// 유니티 생명주기 종료 시 시리얼 포트 연결과 R3 리소스를 해제해 메모리 누수를 막음.
        /// </summary>
        private void OnDestroy()
        {
            SingletonGuard<ArduinoManager>.Release(_isOriginal);
            if (!_isOriginal) return;

            _isDestroyed = true;
            DisconnectInternal(true);
            _messageSubject.Dispose();
            _connectedSubject.Dispose();
            _disconnectedSubject.Dispose();
        }

        /// <summary>
        /// 애플리케이션 종료 시 다음 실행에서 포트가 충돌하지 않도록 시리얼 포트 점유를 즉시 해제함.
        /// </summary>
        private void OnApplicationQuit()
        {
            StopAutoReconnect();
            DisconnectInternal(true);
        }
    }
}
