using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Text;
using HuliacDev.Utils;
using Microsoft.Extensions.Logging;
using R3;
using ZLogger;

namespace HuliacDev.Network
{
    /// <summary>
    /// 랜으로 연결한 장치(TCP 서버로 동작하는 아두이노 등)에 접속을 유지하며 줄 단위로 받고 문자열을 보내는 클라이언트.
    /// 끊기면 간격을 늘려 가며 다시 접속하고, 받은 줄과 연결 변화는 메인 스레드에서 발행함.
    /// <para>소켓은 백그라운드 작업이 다뤄 메인 스레드를 막지 않음. 랜선이 빠진 것은 TCP keepalive로 몇 초 안에 알아챔.
    /// 끊긴 동안 보낸 문자열과 끊길 때 아직 보내지 못한 문자열은 버려, 다시 접속한 뒤 예전 명령으로 장치가 움직이지 않게 함.</para>
    /// </summary>
    public sealed class TcpLineClient : IDisposable
    {
        /// <summary>접속을 기다리는 기본 시간(ms).</summary>
        public const int DefaultConnectTimeoutMs = 3000;

        /// <summary>끊긴 뒤 다시 접속하기 전 첫 대기 시간의 기본값(ms).</summary>
        public const int DefaultReconnectIntervalMs = 1000;

        /// <summary>다시 접속하는 간격의 기본 상한(ms).</summary>
        public const int DefaultReconnectMaxIntervalMs = 30000;

        /// <summary>받는 줄 하나의 기본 최대 글자 수. 넘는 줄은 버림.</summary>
        public const int DefaultMaxLineLength = 256;

        /// <summary>접속 대기 시간의 최소값(ms).</summary>
        private const int MinConnectTimeoutMs = 500;

        /// <summary>다시 접속하는 간격의 최소값(ms). 잘못된 설정으로 쉬지 않고 접속을 시도하지 않게 함.</summary>
        private const int MinReconnectDelayMs = ReconnectBackoff.MinDelayMs;

        /// <summary>접속이 이 시간(ms)보다 짧게 유지되고 끊기면 실패처럼 간격을 늘림. 받자마자 끊는 장치에 1초마다 붙었다 끊기지 않게 함.</summary>
        private const int StableConnectionMs = 5000;

        /// <summary>접속하는 사이 Disconnect·Connect로 멈춘 경우의 결과. 실패가 아니므로 실패 로그를 남기지 않음.</summary>
        private const string StoppedResult = "stopped";

        /// <summary>한 번에 받는 바이트 수.</summary>
        private const int ReceiveBufferSize = 512;

        /// <summary>주고받는 것이 없을 때 연결 확인을 시작하기까지의 시간(ms). 랜선이 빠진 것을 쓰기 실패보다 먼저 알아챔.</summary>
        private const uint KeepAliveIdleMs = 2000;

        /// <summary>연결 확인에 답이 없을 때 다시 확인하는 간격(ms).</summary>
        private const uint KeepAliveIntervalMs = 1000;

        private readonly ILogger<TcpLineClient> _logger;
        private readonly Subject<string> _lineSubject = new Subject<string>();
        private readonly Subject<Unit> _connectedSubject = new Subject<Unit>();
        private readonly Subject<Unit> _disconnectedSubject = new Subject<Unit>();

        // 세대 확인과 IsConnected 변경을 묶어, 멈춘 루프가 확인 직후 늦게 IsConnected를 true로 되돌리지 않게 함.
        private readonly object _stateLock = new object();

        // 지금 돌고 있는 접속 루프를 멈추는 토큰 소스와, 멈춘 뒤 아직 끝나지 않았을 수 있는 루프까지 포함한 작업.
        // Connect·Disconnect·Dispose는 메인 스레드에서만 바꿈.
        private CancellationTokenSource _loopCts;
        private Task _connectionLoop;

        // 지금 루프가 보낼 문자열 큐. 루프마다 따로 둬, 멈춘 이전 루프가 새 접속의 문자열을 가져가거나 지우지 않게 함.
        private Outbox _outbox;

        // 접속 루프의 세대 번호. Connect·Disconnect마다 바뀌어, 멈춘 이전 루프가 늦게 끝나며 IsConnected를 덮어쓰지 않게 함.
        private int _loopGeneration;

        private volatile bool _isConnected;

        // 끊긴 동안 Send를 버렸다고 이미 알렸는지. 주기적으로 보내는 앱에서 장치가 꺼진 동안 경고가 쌓이지 않게 끊긴 기간마다 한 번만 남김.
        // 끊길 때 되돌리므로, Send와 접속이 겹쳐도 경고가 빠지지 않고 많아야 한 번 더 남음.
        private volatile bool _isDropWarned;
        private bool _isDisposed;

        /// <summary>
        /// 로거를 받아 접속하지 않은 상태로 만듦. VContainer에 Register로 등록해 주입받음.
        /// </summary>
        public TcpLineClient(ILogger<TcpLineClient> logger)
        {
            _logger = logger;
        }

        /// <summary>받은 줄(줄 끝 문자 제외, 빈 줄 제외). 메인 스레드에서 발행됨.</summary>
        public Observable<string> OnDataReceived => _lineSubject.ObserveOnMainThread();

        /// <summary>접속됐을 때 메인 스레드에서 발행됨. 늦게 구독하면 지난 접속은 받지 못하므로 지금 상태는 IsConnected로 읽음.</summary>
        public Observable<Unit> OnConnected => _connectedSubject.ObserveOnMainThread();

        /// <summary>접속이 끊겼을 때 메인 스레드에서 발행됨(Disconnect로 끊은 경우 포함).</summary>
        public Observable<Unit> OnDisconnected => _disconnectedSubject.ObserveOnMainThread();

        /// <summary>지금 접속돼 있는지. 백그라운드에서 바로 바뀌므로 OnConnected·OnDisconnected보다 한 프레임 먼저 바뀔 수 있음.</summary>
        public bool IsConnected => _isConnected;

        /// <summary>접속을 기다리는 시간(ms, 최소 0.5초). Connect를 부를 때의 값을 씀.</summary>
        public int ConnectTimeoutMs { get; set; } = DefaultConnectTimeoutMs;

        /// <summary>끊긴 뒤 다시 접속하기 전 첫 대기 시간(ms, 최소 1초). 실패할 때마다 두 배로 늘어 ReconnectMaxIntervalMs에서 멈추고, 접속되면 이 값으로 돌아옴.</summary>
        public int ReconnectIntervalMs { get; set; } = DefaultReconnectIntervalMs;

        /// <summary>다시 접속하는 간격의 상한(ms). ReconnectIntervalMs보다 작으면 ReconnectIntervalMs를 씀.</summary>
        public int ReconnectMaxIntervalMs { get; set; } = DefaultReconnectMaxIntervalMs;

        /// <summary>받는 줄 하나의 최대 글자 수. 넘는 줄은 버리고 경고를 남김. Connect를 부를 때의 값을 씀.</summary>
        public int MaxLineLength { get; set; } = DefaultMaxLineLength;

        /// <summary>
        /// 장치에 접속을 시작하고, Disconnect나 Dispose를 부를 때까지 끊기면 다시 접속함. 접속을 기다리지 않고 바로 돌아옴.
        /// 장치가 아직 꺼져 있어도 앱 시작을 막지 않음. host가 비었거나 port가 1~65535 밖이면 시작하지 않고 false.
        /// 이미 접속 중이면 이전 접속을 끊고 새 주소로 시작함.
        /// </summary>
        public bool Connect(string host, int port)
        {
            if (_isDisposed)
            {
                if (_logger != null) _logger.ZLogWarning($"[TcpLineClient] Connect was called after Dispose; ignored.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535)
            {
                if (_logger != null) _logger.ZLogError($"[TcpLineClient] Invalid address '{host}:{port}'; not connecting.");
                return false;
            }

            Task previousLoop = StopLoop();

            int generation;
            lock (_stateLock)
            {
                generation = ++_loopGeneration;
            }

            Outbox outbox = new Outbox();
            ConnectionOptions options = new ConnectionOptions(generation, host, port, outbox,
                Math.Max(MinConnectTimeoutMs, ConnectTimeoutMs),
                Math.Max(MinReconnectDelayMs, ReconnectIntervalMs),
                Math.Max(Math.Max(MinReconnectDelayMs, ReconnectIntervalMs), ReconnectMaxIntervalMs),
                MaxLineLength);

            CancellationTokenSource loopCts = new CancellationTokenSource();
            _loopCts = loopCts;
            _outbox = outbox;
            if (_logger != null) _logger.ZLogInformation($"[TcpLineClient] Connecting to {host}:{port}.");

            // 루프 안의 대기·소켓 작업이 PlayerLoop(메인 스레드)로 돌아오지 않게 UniTask가 아닌 Task로 스레드 풀에서 돌림.
            Task loop = Task.Run(() => RunConnectionLoopAsync(options, loopCts.Token));

            // 멈춘 이전 루프도 끝날 때까지 Subject를 쓰므로 Dispose가 둘 다 기다리게 묶음.
            _connectionLoop = previousLoop == null || previousLoop.IsCompleted ? loop : Task.WhenAll(previousLoop, loop);
            return true;
        }

        /// <summary>
        /// 접속을 끊고 다시 접속하지 않음. 접속돼 있었으면 OnDisconnected가 발행됨. 다시 쓰려면 Connect를 부름.
        /// </summary>
        public void Disconnect()
        {
            StopLoop();
        }

        /// <summary>
        /// 문자열을 ASCII로 그대로 보냄(줄 끝 문자를 붙이지 않음). 장치가 줄 단위로 받으면 "\n"을 붙여 넘김.
        /// 접속돼 있지 않으면 보내지 않고 false. true여도 보내는 도중 끊기면 장치에 닿지 않을 수 있음.
        /// </summary>
        public bool Send(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                if (_logger != null) _logger.ZLogWarning($"[TcpLineClient] Empty message; not sent.");
                return false;
            }

            Outbox outbox = _outbox;
            if (_isDisposed || !_isConnected || outbox == null)
            {
                if (!_isDropWarned)
                {
                    _isDropWarned = true;
                    if (_logger != null) _logger.ZLogWarning($"[TcpLineClient] Not connected; dropped '{message}'. Messages are dropped silently until connected.");
                }

                return false;
            }

            outbox.Enqueue(message);
            return true;
        }

        /// <summary>
        /// 접속을 끊고, 루프가 끝난 뒤 내부 자원을 해제함. 장치에 마지막 명령을 보내야 하면 Dispose 전에 Send하고 잠시 기다림.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Task loop = StopLoop();
            if (loop == null)
            {
                ReleaseResources();
                return;
            }

            // 루프가 아직 Subject를 쓰는 동안 해제하면 예외가 나므로 끝난 뒤에 해제함(보낼 큐는 루프가 끝날 때 스스로 해제함).
            loop.ContinueWith(_ => ReleaseResources(), TaskScheduler.Default);
        }

        /// <summary>
        /// 돌고 있는 접속 루프를 멈추고, 이미 멈춘 루프까지 포함해 끝나기를 기다릴 작업을 돌려줌(돈 적이 없으면 null).
        /// 루프는 토큰 취소로 소켓을 닫고 곧 끝남.
        /// </summary>
        private Task StopLoop()
        {
            CancellationTokenSource loopCts = _loopCts;
            Task loop = _connectionLoop;
            _loopCts = null;
            _outbox = null;
            if (loopCts == null) return loop;

            // 멈춘 루프는 IsConnected를 바꾸거나 이벤트를 내지 못하므로(세대가 달라짐) 여기서 끊긴 것으로 두고 알림.
            lock (_stateLock)
            {
                _loopGeneration++;
                if (_isConnected)
                {
                    _isConnected = false;
                    _isDropWarned = false;
                    _disconnectedSubject.OnNext(Unit.Default);
                }
            }

            loopCts.Cancel();

            // 루프가 이 토큰을 다 쓴 뒤에 해제함.
            loop.ContinueWith(_ => loopCts.Dispose(), TaskScheduler.Default);
            return loop;
        }

        /// <summary>
        /// Subject를 해제함. 루프가 끝난 뒤에만 부름.
        /// </summary>
        private void ReleaseResources()
        {
            _lineSubject.Dispose();
            _connectedSubject.Dispose();
            _disconnectedSubject.Dispose();
        }

        /// <summary>
        /// 멈출 때까지 접속을 맺고, 끊기거나 맺지 못하면 간격을 늘려 가며 다시 맺음. 실패 이유는 바뀔 때만 로그로 남김.
        /// </summary>
        private async Task RunConnectionLoopAsync(ConnectionOptions options, CancellationToken token)
        {
            byte[] buffer = new byte[ReceiveBufferSize];
            LineBuffer lines = new LineBuffer(options.MaxLineLength);
            List<string> received = new List<string>();
            System.Diagnostics.Stopwatch sinceLost = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch connectedFor = new System.Diagnostics.Stopwatch();

            try
            {
                await ServeUntilStoppedAsync(options, buffer, lines, received, sinceLost, connectedFor, token);
            }
            finally
            {
                // Send는 StopLoop가 이 큐를 떼어 낸 뒤에는 넣지 않으므로, 루프가 끝나면 해제해도 됨.
                options.Outbox.Dispose();
            }
        }

        /// <summary>
        /// 멈출 때까지 접속을 맺고 다시 맺기를 되풀이함. RunConnectionLoopAsync가 끝난 뒤 이 루프의 큐를 해제하도록 나눔.
        /// </summary>
        private async Task ServeUntilStoppedAsync(ConnectionOptions options, byte[] buffer, LineBuffer lines, List<string> received,
            System.Diagnostics.Stopwatch sinceLost, System.Diagnostics.Stopwatch connectedFor, CancellationToken token)
        {
            string lastFailure = null;
            int delayMs = options.ReconnectIntervalMs;
            int failedAttempts = 0;

            while (!token.IsCancellationRequested)
            {
                string failure;
                try
                {
                    failure = await ConnectAndServeAsync(options, buffer, lines, received, failedAttempts, sinceLost, connectedFor, token);
                }
                catch (Exception e)
                {
                    // 소켓을 만들지 못하는 등 한 번의 실패로 다시 접속하기 자체가 멈추면 안 됨.
                    failure = ZString.Concat("unexpected error: ", e.GetType().Name, ": ", e.Message);
                }

                // 멈춘 경우는 실패가 아니므로 로그 없이 끝냄(세대가 바뀐 뒤 토큰 취소가 아직 닿지 않았을 수 있음).
                if (token.IsCancellationRequested || ReferenceEquals(failure, StoppedResult)) return;

                // 접속했다가 곧바로 끊긴 경우는 실패처럼 간격을 늘림.
                bool isStable = failure == null && connectedFor.ElapsedMilliseconds >= StableConnectionMs;
                if (failure == null)
                {
                    // 접속했다가 끊긴 경우. 오래 유지됐으면 처음 간격부터 다시 시도하고, 끊긴 시점부터 걸린 시간을 잼.
                    if (isStable) delayMs = options.ReconnectIntervalMs;
                    failedAttempts = 0;
                    lastFailure = null;
                    sinceLost.Restart();
                }
                else
                {
                    failedAttempts++;
                    if (failure != lastFailure)
                    {
                        if (_logger != null) _logger.ZLogWarning($"[TcpLineClient] Could not connect to {options.Host}:{options.Port}: {failure}. Retrying with backoff.");
                        lastFailure = failure;
                    }
                }

                try
                {
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!isStable) delayMs = ReconnectBackoff.NextDelayMs(delayMs, options.ReconnectMaxIntervalMs);
            }
        }

        /// <summary>
        /// 접속 하나를 맺고 끊길 때까지 주고받음. 접속했다가 끊겼으면 null, 맺지 못했으면 그 이유를 돌려줌.
        /// </summary>
        private async Task<string> ConnectAndServeAsync(ConnectionOptions options, byte[] buffer, LineBuffer lines, List<string> received,
            int failedAttempts, System.Diagnostics.Stopwatch sinceLost, System.Diagnostics.Stopwatch connectedFor, CancellationToken token)
        {
            bool isConnected = false;
            string disconnectReason = "closed by the device";
            using (TcpClient tcp = new TcpClient())
            using (CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (CancellationTokenRegistration closeOnCancel = connection.Token.Register(tcp.Close))
            {
                try
                {
                    if (!await ConnectWithTimeoutAsync(tcp, options, connection.Token))
                    {
                        return ZString.Concat("no answer within ", options.ConnectTimeoutMs, "ms");
                    }

                    ConfigureSocket(tcp);
                    // 끊긴 접속으로 보내려던 문자열을 버림. 다시 접속한 뒤 예전 명령이 나가 장치가 움직이면 안 됨.
                    options.Outbox.Clear();
                    lines.Clear();

                    // 접속하는 사이 Disconnect·Connect로 멈췄으면 접속했다고 알리지 않고 닫음.
                    if (!TryMarkConnected(options)) return StoppedResult;

                    isConnected = true;
                    connectedFor.Restart();
                    LogConnected(options, failedAttempts, sinceLost);

                    NetworkStream stream = tcp.GetStream();
                    Task writer = WriteLoopAsync(stream, options.Outbox, connection);
                    try
                    {
                        await ReadLoopAsync(options, stream, buffer, lines, received, connection.Token);
                    }
                    finally
                    {
                        // 받기가 예외로 끝나도 쓰기 작업을 끝냄. 남아 있으면 다시 접속한 뒤 첫 문자열을 가져가 닫힌 소켓에 쓰고 잃음.
                        connection.Cancel();
                        await writer;
                    }

                    return null;
                }
                catch (Exception e) when (IsConnectionFailure(e))
                {
                    // 접속 중 끊김은 OnDisconnected로 알리므로 맺지 못한 경우만 이유를 돌려줌.
                    if (!isConnected) return DescribeFailure(e);

                    disconnectReason = DescribeFailure(e);
                    return null;
                }
                finally
                {
                    if (isConnected)
                    {
                        MarkDisconnectedIfCurrent(options);
                        if (token.IsCancellationRequested)
                        {
                            if (_logger != null) _logger.ZLogInformation($"[TcpLineClient] Disconnected from {options.Host}:{options.Port}.");
                        }
                        else if (_logger != null)
                        {
                            _logger.ZLogWarning($"[TcpLineClient] Connection to {options.Host}:{options.Port} lost ({disconnectReason}). Reconnecting.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 이 설정으로 돈 루프가 지금 루프면 접속된 것으로 두고 OnConnected를 발행한 뒤 true. 이미 멈춘 루프면 아무것도 하지 않고 false.
        /// 세대 확인·상태 변경·발행을 한 잠금 안에서 해 StopLoop의 끊김 발행과 순서가 뒤바뀌지 않게 함.
        /// </summary>
        private bool TryMarkConnected(ConnectionOptions options)
        {
            lock (_stateLock)
            {
                if (options.Generation != _loopGeneration) return false;

                _isConnected = true;
                _connectedSubject.OnNext(Unit.Default);
                return true;
            }
        }

        /// <summary>
        /// 이 설정으로 돈 루프가 지금 루프이고 접속돼 있으면 끊긴 것으로 두고 OnDisconnected를 발행함.
        /// 멈춘 이전 루프의 끊김은 StopLoop가 이미 알렸으므로 다시 알리지 않음.
        /// </summary>
        private void MarkDisconnectedIfCurrent(ConnectionOptions options)
        {
            lock (_stateLock)
            {
                if (options.Generation != _loopGeneration || !_isConnected) return;

                _isConnected = false;
                _isDropWarned = false;
                _disconnectedSubject.OnNext(Unit.Default);
            }
        }

        /// <summary>
        /// 이 설정으로 돈 루프가 지금 루프일 때만 받은 줄을 발행함. 멈춘 이전 루프가 마지막으로 받은 줄이 새 접속의 줄처럼 나가지 않게 함.
        /// </summary>
        private void PublishLineIfCurrent(ConnectionOptions options, string line)
        {
            lock (_stateLock)
            {
                if (options.Generation == _loopGeneration) _lineSubject.OnNext(line);
            }
        }

        /// <summary>
        /// 접속 성공을 남김. 끊긴 뒤 다시 접속했으면 몇 번 만에, 얼마나 걸려 접속했는지 함께 남김.
        /// </summary>
        private void LogConnected(ConnectionOptions options, int failedAttempts, System.Diagnostics.Stopwatch sinceLost)
        {
            if (_logger == null) return;

            if (sinceLost.IsRunning)
            {
                _logger.ZLogInformation($"[TcpLineClient] Reconnected to {options.Host}:{options.Port} after {failedAttempts + 1} attempts ({sinceLost.Elapsed.TotalSeconds:F0}s).");
            }
            else
            {
                _logger.ZLogInformation($"[TcpLineClient] Connected to {options.Host}:{options.Port}.");
            }
        }

        /// <summary>
        /// 정한 시간 안에 접속하면 true. 시간을 넘기면 false이고, 남은 시도는 소켓을 닫을 때 끝남.
        /// </summary>
        private static async Task<bool> ConnectWithTimeoutAsync(TcpClient tcp, ConnectionOptions options, CancellationToken token)
        {
            Task connect = tcp.ConnectAsync(options.Host, options.Port);
            Task finished = await Task.WhenAny(connect, Task.Delay(options.ConnectTimeoutMs, token));
            if (finished == connect)
            {
                await connect;
                return true;
            }

            // 소켓을 닫으면 connect가 예외로 끝나므로, 관찰되지 않은 예외로 남지 않게 읽어 둠.
            _ = connect.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            token.ThrowIfCancellationRequested();
            return false;
        }

        /// <summary>
        /// 작은 명령이 바로 나가게 하고, 주고받는 것이 없어도 끊김을 알 수 있게 keepalive를 켬.
        /// </summary>
        private void ConfigureSocket(TcpClient tcp)
        {
            tcp.NoDelay = true;
            try
            {
                byte[] keepAlive = new byte[12];
                BitConverter.GetBytes(1u).CopyTo(keepAlive, 0);
                BitConverter.GetBytes(KeepAliveIdleMs).CopyTo(keepAlive, 4);
                BitConverter.GetBytes(KeepAliveIntervalMs).CopyTo(keepAlive, 8);
                tcp.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);
            }
            catch (Exception e) when (e is SocketException || e is NotSupportedException || e is PlatformNotSupportedException)
            {
                if (_logger != null) _logger.ZLogWarning($"[TcpLineClient] Could not set TCP keepalive ({e.GetType().Name}); a pulled cable is noticed only when a send fails.");
            }
        }

        /// <summary>
        /// 메인 스레드가 넣은 문자열을 차례로 보냄. 보내지 못하면 접속을 끊어 다시 접속하게 함.
        /// </summary>
        private async Task WriteLoopAsync(NetworkStream stream, Outbox outbox, CancellationTokenSource connection)
        {
            CancellationToken token = connection.Token;

            // 접속마다 하나를 재사용함. 이 루프 안에서만 쓰고 WriteAsync를 기다린 뒤 다음 문자열을 채우므로 겹쳐 쓰지 않음.
            byte[] buffer = new byte[64];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string message = await outbox.DequeueAsync(token);
                    if (message == null) continue;

                    // ASCII는 글자 하나가 1바이트라(ASCII 밖 글자는 '?') 바이트 수가 글자 수와 같음.
                    if (message.Length > buffer.Length) buffer = new byte[message.Length];
                    int count = Encoding.ASCII.GetBytes(message, 0, message.Length, buffer, 0);
                    await stream.WriteAsync(buffer, 0, count, token);
                }
            }
            catch (Exception e) when (IsConnectionFailure(e))
            {
                // 끊긴 이유는 받기 쪽이 끝난 방식으로 정해지므로, 쓰기가 먼저 실패한 원인은 따로 남김.
                if (!token.IsCancellationRequested && _logger != null)
                {
                    _logger.ZLogWarning($"[TcpLineClient] Send failed ({DescribeFailure(e)}); reconnecting.");
                }

                connection.Cancel();
            }
        }

        /// <summary>
        /// 받은 바이트를 줄로 나눠 발행함. 장치가 접속을 닫거나 소켓이 닫히면 끝남.
        /// </summary>
        private async Task ReadLoopAsync(ConnectionOptions options, NetworkStream stream, byte[] buffer, LineBuffer lines, List<string> received,
            CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (count <= 0) return;

                received.Clear();
                int discarded = lines.Append(buffer, count, received);
                if (discarded > 0 && _logger != null)
                {
                    _logger.ZLogWarning($"[TcpLineClient] Discarded {discarded} line(s) longer than {options.MaxLineLength} characters.");
                }

                for (int i = 0; i < received.Count; i++)
                {
                    PublishLineIfCurrent(options, received[i]);
                }
            }
        }

        /// <summary>
        /// 현장 로그에서 원인을 구분하게 소켓 오류 코드를 꺼냄(접속 거부, 경로 없음, 시간 초과 등).
        /// </summary>
        private static string DescribeFailure(Exception e)
        {
            if (e is SocketException socketException) return socketException.SocketErrorCode.ToString();
            if (e.InnerException is SocketException inner) return inner.SocketErrorCode.ToString();
            return e is OperationCanceledException ? "stopped or send failed" : e.GetType().Name;
        }

        /// <summary>
        /// 소켓을 쓰다 끊기거나 닫혀서 나는 예외인지. 이런 예외는 다시 접속하는 것으로 처리함.
        /// </summary>
        private static bool IsConnectionFailure(Exception e)
        {
            return e is SocketException || e is IOException || e is ObjectDisposedException ||
                   e is OperationCanceledException || e is InvalidOperationException;
        }

        /// <summary>
        /// 접속 루프 하나가 보낼 문자열 큐. 메인 스레드의 Send가 넣고 그 루프의 쓰기 작업만 꺼냄.
        /// </summary>
        private sealed class Outbox : IDisposable
        {
            private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
            private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

            /// <summary>
            /// 보낼 문자열을 넣고 쓰기 작업을 깨움.
            /// </summary>
            public void Enqueue(string message)
            {
                _queue.Enqueue(message);
                _signal.Release();
            }

            /// <summary>
            /// 보낼 문자열이 들어올 때까지 기다렸다 꺼냄. 깨웠는데 Clear로 비워졌으면 null.
            /// </summary>
            public async Task<string> DequeueAsync(CancellationToken token)
            {
                await _signal.WaitAsync(token);
                return _queue.TryDequeue(out string message) ? message : null;
            }

            /// <summary>
            /// 쌓인 문자열을 버림. 새 접속을 시작할 때 부름.
            /// </summary>
            public void Clear()
            {
                while (_queue.TryDequeue(out string _))
                {
                }
            }

            /// <summary>
            /// 신호용 세마포어를 해제함. 루프가 끝난 뒤에만 부름.
            /// </summary>
            public void Dispose()
            {
                _signal.Dispose();
            }
        }

        /// <summary>
        /// Connect를 부를 때 정한 접속 설정. 루프가 도는 동안 속성이 바뀌어도 흔들리지 않게 값을 복사해 둠.
        /// </summary>
        private readonly struct ConnectionOptions
        {
            public readonly string Host;
            public readonly Outbox Outbox;
            public readonly int Generation;
            public readonly int Port;
            public readonly int ConnectTimeoutMs;
            public readonly int ReconnectIntervalMs;
            public readonly int ReconnectMaxIntervalMs;
            public readonly int MaxLineLength;

            /// <summary>
            /// 접속 설정 값을 담음.
            /// </summary>
            public ConnectionOptions(int generation, string host, int port, Outbox outbox, int connectTimeoutMs, int reconnectIntervalMs, int reconnectMaxIntervalMs, int maxLineLength)
            {
                Host = host;
                Outbox = outbox;
                Generation = generation;
                Port = port;
                ConnectTimeoutMs = connectTimeoutMs;
                ReconnectIntervalMs = reconnectIntervalMs;
                ReconnectMaxIntervalMs = reconnectMaxIntervalMs;
                MaxLineLength = maxLineLength;
            }
        }
    }
}
