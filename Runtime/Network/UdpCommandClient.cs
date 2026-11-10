using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using R3;
using ZLogger;

namespace HuliacDev.Network
{
    /// <summary>
    /// 랜으로 연결한 장치(UDP로 명령을 받는 아두이노 등)에 짧은 명령 문자열을 보내고, 장치가 같은 문자열을 돌려주는지로
    /// 받았는지 확인하는 클라이언트. 정한 시간 안에 돌아오지 않으면 정한 횟수까지 다시 보냄.
    /// <para>명령은 들어온 순서대로 하나씩 보내, 앞 명령을 다시 보내는 동안 뒤 명령이 먼저 나가 순서가 바뀌지 않게 함.
    /// 장치는 받은 명령을 보낸 쪽의 IP·포트로 그대로 돌려줘야 함(이 클라이언트의 포트는 실행마다 바뀜). 응답은 앞뒤 공백을
    /// 빼고 비교하므로 줄 끝 문자가 붙어 돌아와도 됨. 같은 명령을 연달아 보내면 앞 명령의 늦은 응답을 뒤 명령의 응답으로 볼 수
    /// 있음(돌려주는 문자열만으로는 구별할 수 없음).</para>
    /// <para>메인 스레드에서만 부름. 받기가 처음 보낸 호출의 SynchronizationContext로 이어지므로, 그래야 받은 패킷도
    /// 메인 스레드에서 발행됨.</para>
    /// </summary>
    public sealed class UdpCommandClient : IDisposable
    {
        /// <summary>장치가 명령을 돌려주기를 기다리는 기본 시간(ms).</summary>
        public const int DefaultReplyTimeoutMs = 3000;

        /// <summary>응답이 없을 때 보내는 기본 최대 횟수(첫 전송 포함).</summary>
        public const int DefaultMaxSendAttempts = 3;

        /// <summary>응답 대기 시간의 최소값(ms). 0이면 장치가 답하기도 전에 다시 보내 같은 명령이 여러 번 실행됨.</summary>
        private const int MinReplyTimeoutMs = 100;

        private readonly ILogger<UdpCommandClient> _logger;
        private readonly Subject<string> _messageSubject = new Subject<string>();
        private readonly Queue<PendingCommand> _sendQueue = new Queue<PendingCommand>();

        private IPEndPoint _target;

        // 처음 보낼 때 열어 Dispose할 때까지 씀. 장치가 이 소켓의 포트로 응답함.
        private UdpClient _client;

        // 지금 응답을 기다리는 명령(앞뒤 공백 제거)과 그 명령을 보낸 곳, 장치가 같은 문자열을 돌려주면 완료할 소스.
        private string _awaitedReply;
        private IPEndPoint _awaitedTarget;
        private UniTaskCompletionSource _replySource;

        private bool _isSending;
        private bool _isDisposed;

        /// <summary>
        /// 로거를 받아 보낼 곳이 정해지지 않은 상태로 만듦. VContainer에 Register로 등록해 주입받고, 쓰기 전에 SetTarget을 부름.
        /// </summary>
        public UdpCommandClient(ILogger<UdpCommandClient> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 대상 장치에서 온 패킷(앞뒤 공백 제거, ASCII). 명령에 대한 응답도 함께 발행됨. 메인 스레드에서 쓰면 메인 스레드에서 발행됨.
        /// </summary>
        public Observable<string> OnDataReceived => _messageSubject;

        /// <summary>장치가 명령을 돌려주기를 기다리는 시간(ms, 최소 0.1초). 명령마다 보낼 때의 값을 씀.</summary>
        public int ReplyTimeoutMs { get; set; } = DefaultReplyTimeoutMs;

        /// <summary>응답이 없을 때 보내는 최대 횟수(첫 전송 포함, 최소 1). 명령마다 보낼 때의 값을 씀.</summary>
        public int MaxSendAttempts { get; set; } = DefaultMaxSendAttempts;

        /// <summary>
        /// 명령을 보낼 장치의 주소를 정함. 이름 풀이를 기다리지 않도록 IP 주소만 받음. 주소가 IP가 아니거나 port가 1~65535 밖이면
        /// 바꾸지 않고 오류를 남긴 뒤 false. 이후 SendAsync로 넣는 명령부터 적용되고, 이미 넣은 명령은 넣을 때의 주소로 감.
        /// </summary>
        public bool SetTarget(string host, int port)
        {
            if (!IPAddress.TryParse(host, out IPAddress address))
            {
                if (_logger != null) _logger.ZLogError($"[UdpCommandClient] Target '{host}' is not an IP address; commands will not be sent.");
                return false;
            }

            if (port < 1 || port > 65535)
            {
                if (_logger != null) _logger.ZLogError($"[UdpCommandClient] Target port {port} is outside 1-65535; commands will not be sent.");
                return false;
            }

            _target = new IPEndPoint(address, port);
            return true;
        }

        /// <summary>
        /// 명령을 차례에 넣고, 장치가 같은 문자열을 돌려주면 true, 횟수를 다 써도 돌아오지 않으면 false로 끝나는 작업을 돌려줌.
        /// 결과가 필요 없으면 기다리지 않고 Forget해도 명령은 끝까지 보내짐. 명령은 ASCII 그대로 한 패킷으로 보냄(줄 끝 문자를
        /// 붙이지 않음). 보낼 곳이 정해지지 않았거나 Dispose한 뒤면 보내지 않고 false.
        /// </summary>
        public UniTask<bool> SendAsync(string command)
        {
            if (_isDisposed)
            {
                if (_logger != null) _logger.ZLogWarning($"[UdpCommandClient] SendAsync was called after Dispose; '{command}' was not sent.");
                return UniTask.FromResult(false);
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                if (_logger != null) _logger.ZLogWarning($"[UdpCommandClient] Empty command; not sent.");
                return UniTask.FromResult(false);
            }

            if (_target == null)
            {
                if (_logger != null) _logger.ZLogError($"[UdpCommandClient] No target set; '{command}' was not sent. Call SetTarget first.");
                return UniTask.FromResult(false);
            }

            UniTaskCompletionSource<bool> completion = new UniTaskCompletionSource<bool>();
            _sendQueue.Enqueue(new PendingCommand(command, _target, completion));
            if (!_isSending) ProcessQueueAsync().Forget();
            return completion.Task;
        }

        /// <summary>
        /// 차례를 기다리던 명령을 응답을 기다리지 않고 한 번씩 보낸 뒤 소켓을 닫음. 장치가 마지막 상태로 남지 않게 하려고
        /// 앱이 끝날 때 보낼 명령(종료 명령 등)은 Dispose 전에 SendAsync로 넣어 둠. 진행 중이던 명령과 차례를 기다리던 명령의
        /// 결과는 false.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            // 결과를 완료하면 호출자의 이어짐이 바로 실행될 수 있으므로, 먼저 끝난 것으로 표시해 그 안에서 다시 넣지 못하게 함.
            _isDisposed = true;
            PendingCommand[] waiting = _sendQueue.ToArray();
            _sendQueue.Clear();

            FlushOnDispose(waiting);

            UdpClient client = _client;
            _client = null;
            client?.Dispose();

            // 진행 중이던 명령의 대기를 끝내고(다시 보내지 않음) 차례를 기다리던 명령의 결과를 넣은 순서대로 완료함.
            _replySource?.TrySetCanceled();
            for (int i = 0; i < waiting.Length; i++)
            {
                waiting[i].Completion.TrySetResult(false);
            }

            _messageSubject.Dispose();
        }

        /// <summary>
        /// Dispose할 때 차례를 기다리던 명령을 응답을 기다리지 않고 한 번씩 보냄. 소켓이 없으면(한 번도 보내지 않았거나 받기
        /// 오류로 닫힌 뒤) 보내기만 할 소켓을 잠깐 엶.
        /// </summary>
        private void FlushOnDispose(PendingCommand[] waiting)
        {
            if (waiting.Length == 0) return;

            UdpClient client = _client;
            bool isTemporary = client == null;
            try
            {
                if (isTemporary) client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

                for (int i = 0; i < waiting.Length; i++)
                {
                    SendWith(client, waiting[i].Command, waiting[i].Target);
                }
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogWarning($"[UdpCommandClient] Could not open a socket to send the remaining commands on dispose: {e.Message}");
            }
            finally
            {
                if (isTemporary) client?.Dispose();
            }
        }

        /// <summary>
        /// 차례에 쌓인 명령을 하나씩 응답을 확인하며 보내고, 명령마다 결과를 완료함.
        /// </summary>
        private async UniTaskVoid ProcessQueueAsync()
        {
            // 씬 수명과 묶지 않음. 씬을 떠나는 순간 보내는 명령도 끝까지 나가야 함.
            _isSending = true;
            try
            {
                while (_sendQueue.Count > 0 && !_isDisposed)
                {
                    PendingCommand pending = _sendQueue.Dequeue();
                    bool isAcknowledged = false;
                    try
                    {
                        isAcknowledged = await SendAndWaitReplyAsync(pending.Command, pending.Target);
                    }
                    catch (OperationCanceledException)
                    {
                        // Dispose로 대기를 끝내면 보통 SendAndWaitReplyAsync가 false를 돌려주지만, 취소가 예외로 올라와도 결과만 false로 둠.
                    }
                    catch (Exception e)
                    {
                        // 한 명령의 실패로 뒤 명령까지 멈추지 않게 결과만 실패로 두고 이어 감.
                        if (_logger != null) _logger.ZLogError($"[UdpCommandClient] Sending '{pending.Command}' failed: {e.Message}");
                    }

                    pending.Completion.TrySetResult(isAcknowledged);
                }
            }
            finally
            {
                _isSending = false;
            }
        }

        /// <summary>
        /// 명령을 보내고 장치가 같은 문자열을 돌려주기를 기다림. 정한 시간 안에 오지 않으면 최대 횟수까지 다시 보내고,
        /// 돌아오면 true. 뒤 명령이 기다리고 있으면 실제로 보낸 시도 한 번만 기다리고 넘어감.
        /// </summary>
        private async UniTask<bool> SendAndWaitReplyAsync(string command, IPEndPoint target)
        {
            // 응답 대기는 timeScale·프레임과 무관하게 실제 시간으로 잼.
            TimeSpan timeout = TimeSpan.FromMilliseconds(Math.Max(MinReplyTimeoutMs, ReplyTimeoutMs));
            int maxSendAttempts = Math.Max(1, MaxSendAttempts);

            try
            {
                for (int attempt = 1; attempt <= maxSendAttempts; attempt++)
                {
                    if (_isDisposed) return false;

                    // 앞 시도의 응답이 늦게 와도 같은 명령이므로 받은 것으로 침.
                    UniTaskCompletionSource replySource = new UniTaskCompletionSource();
                    _replySource = replySource;
                    _awaitedReply = command.Trim();
                    _awaitedTarget = target;

                    // 보내기에 실패해도(랜선이 잠깐 빠진 경우 등) 같은 간격으로 다시 보냄.
                    bool isSent = SendTo(command, target);

                    bool isTimeout = await replySource.Task.TimeoutWithoutException(timeout, DelayType.Realtime);
                    if (!isTimeout)
                    {
                        // 다시 보낸 뒤에 응답이 왔다면 장치가 같은 명령을 여러 번 받았을 수 있음. 동작이 겹치면 이 로그로 원인을 찾음.
                        if (attempt > 1 && _logger != null)
                        {
                            _logger.ZLogWarning($"[UdpCommandClient] '{command}' was acknowledged after a resend; the device may have received it up to {attempt} times.");
                        }

                        return true;
                    }

                    if (_isDisposed) return false;

                    // 장치가 답하지 않는 동안 뒤 명령이 횟수만큼 밀리지 않게, 뒤 명령이 있으면 더 다시 보내지 않음.
                    // 보내기 자체가 실패한 시도는 장치가 답할 기회가 없었으므로 넘어가지 않고 다시 보냄.
                    if (isSent && _sendQueue.Count > 0)
                    {
                        if (_logger != null) _logger.ZLogWarning($"[UdpCommandClient] No reply to '{command}' within {timeout.TotalMilliseconds}ms; moving on to the next command without resending.");
                        return false;
                    }

                    if (attempt < maxSendAttempts && _logger != null)
                    {
                        _logger.ZLogWarning($"[UdpCommandClient] No reply to '{command}' within {timeout.TotalMilliseconds}ms; resending ({attempt + 1}/{maxSendAttempts}).");
                    }
                }
            }
            finally
            {
                // 포기한 명령의 응답이 늦게 와도 다음 명령의 대기를 끝내지 않게 비움.
                _awaitedReply = null;
                _awaitedTarget = null;
                _replySource = null;
            }

            if (_logger != null) _logger.ZLogError($"[UdpCommandClient] No reply to '{command}' after {maxSendAttempts} attempts. Check the device power, cable and target address {target}.");
            return false;
        }

        /// <summary>
        /// 받기를 함께 도는 소켓으로 명령을 보냄. 보내지 못하면 false. UDP라 장치가 꺼져 있어도 보내기는 성공하며,
        /// 받았는지는 장치의 응답으로만 알 수 있음.
        /// </summary>
        private bool SendTo(string command, IPEndPoint target)
        {
            UdpClient client;
            try
            {
                client = GetOrOpenClient();
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogWarning($"[UdpCommandClient] Could not open a socket to send '{command}': {e.Message}");
                return false;
            }

            return client != null && SendWith(client, command, target);
        }

        /// <summary>
        /// 주어진 소켓으로 명령을 ASCII 그대로 한 패킷으로 보내고 로그를 남김. 보내지 못하면 경고를 남기고 false.
        /// </summary>
        private bool SendWith(UdpClient client, string command, IPEndPoint target)
        {
            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(command);
                client.Send(bytes, bytes.Length, target);
                if (_logger != null) _logger.ZLogInformation($"[UdpCommandClient] Sent '{command}' to {target}.");
                return true;
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogWarning($"[UdpCommandClient] Could not send '{command}' to {target}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 명령·응답에 쓸 소켓을 처음 한 번 열고 받기를 시작함. Dispose한 뒤에는 열지 않음.
        /// </summary>
        private UdpClient GetOrOpenClient()
        {
            if (_isDisposed) return null;
            if (_client != null) return _client;

            // 빈 포트를 하나 받아 Dispose할 때까지 씀. 포트는 실행마다 바뀌므로 장치는 받은 패킷의 포트로 응답해야 함.
            _client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            ReceiveAsync(_client).Forget();
            return _client;
        }

        /// <summary>
        /// 소켓이 닫힐 때까지 대상 장치의 패킷을 받아 발행하고, 기다리던 명령의 응답이면 대기를 끝냄.
        /// 메인 스레드에서 시작하므로 받은 뒤의 처리도 메인 스레드에서 이어짐.
        /// </summary>
        private async UniTaskVoid ReceiveAsync(UdpClient client)
        {
            while (client == _client)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync();
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Windows는 보낸 패킷이 ICMP로 거부되면(장치 프로그램이 멈춤 등) 다음 받기에서 이 예외를 냄. 소켓은 계속 쓸 수 있음.
                    continue;
                }
                catch (Exception e)
                {
                    // Dispose로 소켓을 닫으면 기다리던 받기가 예외로 끝남. 정상 종료.
                    if (client != _client) return;

                    // 응답을 못 받는 소켓을 두면 이후 모든 명령이 응답 없음으로 여러 번 나가므로, 닫고 다음 전송 때 새로 엶.
                    if (_logger != null) _logger.ZLogWarning($"[UdpCommandClient] Receiving failed; the socket will be reopened on the next send: {e.Message}");
                    _client = null;
                    client.Dispose();
                    return;
                }

                if (_isDisposed || client != _client) return;

                string message = Encoding.ASCII.GetString(result.Buffer).Trim();
                if (IsAwaitedReply(message, result.RemoteEndPoint))
                {
                    if (_logger != null) _logger.ZLogInformation($"[UdpCommandClient] '{message}' acknowledged by {result.RemoteEndPoint}.");
                    _replySource.TrySetResult();
                }
                else if (!IsFromTarget(result.RemoteEndPoint))
                {
                    // 다른 장치·프로그램이 이 포트로 보낸 패킷은 무시함.
                    continue;
                }

                // 응답을 완료하면 호출자의 이어짐이 바로 실행돼 그 안에서 Dispose될 수 있음.
                if (_isDisposed) return;
                _messageSubject.OnNext(message);
            }
        }

        /// <summary>
        /// 받은 패킷이 지금 기다리는 명령을 보낸 장치에서 온 같은 문자열인지.
        /// </summary>
        private bool IsAwaitedReply(string message, IPEndPoint remote)
        {
            return _replySource != null && _awaitedTarget != null && remote != null
                   && remote.Address.Equals(_awaitedTarget.Address) && message == _awaitedReply;
        }

        /// <summary>
        /// 받은 패킷이 지금 대상 장치의 IP에서 왔는지.
        /// </summary>
        private bool IsFromTarget(IPEndPoint remote)
        {
            return _target != null && remote != null && remote.Address.Equals(_target.Address);
        }

        /// <summary>
        /// 차례를 기다리는 명령과 보낼 곳, 그 결과를 완료할 소스.
        /// </summary>
        private readonly struct PendingCommand
        {
            public readonly string Command;
            public readonly IPEndPoint Target;
            public readonly UniTaskCompletionSource<bool> Completion;

            /// <summary>
            /// 명령과 보낼 곳, 결과 소스를 담음.
            /// </summary>
            public PendingCommand(string command, IPEndPoint target, UniTaskCompletionSource<bool> completion)
            {
                Command = command;
                Target = target;
                Completion = completion;
            }
        }
    }
}
