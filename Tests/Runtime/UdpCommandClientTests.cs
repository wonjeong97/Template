using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Cysharp.Threading.Tasks;
using HuliacDev.Network;
using NUnit.Framework;
using R3;
using UnityEngine.TestTools;

namespace HuliacDev.Tests
{
    /// <summary>
    /// UdpCommandClient 검증. 루프백 주소에 가짜 장치를 띄워, 받은 명령을 그대로 돌려주거나 답하지 않거나 다른 문자열로 답하게 함.
    /// 응답이 오는 테스트는 프레임이 한 번 길어져도 시간 초과로 보지 않게 대기를 넉넉히(1초) 두고, 응답이 없는 테스트만 짧게 둠.
    /// </summary>
    public class UdpCommandClientTests
    {
        private readonly List<string> _deviceReceived = new List<string>();
        private UdpClient _device;
        private UdpCommandClient _client;
        private IPEndPoint _clientEndPoint;

        // 돌려주지 않고 이 문자열로 답함(null이면 받은 명령을 그대로 돌려줌).
        private string _replyOverride;
        private int _devicePort;

        // 처음 이 횟수만큼 받은 명령에는 답하지 않음.
        private int _ignoreFirstCount;
        private int _echoDelayMs;
        private bool _isEchoing;

        /// <summary>
        /// 가짜 장치를 열고, 그 주소로 보내는 클라이언트를 만듦.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _deviceReceived.Clear();
            _isEchoing = true;
            _replyOverride = null;
            _ignoreFirstCount = 0;
            _echoDelayMs = 0;
            _clientEndPoint = null;
            _device = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _devicePort = ((IPEndPoint)_device.Client.LocalEndPoint).Port;
            RunDeviceAsync(_device).Forget();

            _client = new UdpCommandClient(null);
            _client.ReplyTimeoutMs = 1000;
            _client.MaxSendAttempts = 3;
            Assert.IsTrue(_client.SetTarget("127.0.0.1", _devicePort));
        }

        /// <summary>
        /// 클라이언트와 가짜 장치를 닫음.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            _device.Dispose();
        }

        /// <summary>
        /// 가짜 장치. 받은 명령을 기록하고, 정한 방식대로 보낸 쪽 포트로 답함. 메인 스레드에서 시작해 메인 스레드에서 이어짐.
        /// </summary>
        private async UniTaskVoid RunDeviceAsync(UdpClient device)
        {
            while (true)
            {
                UdpReceiveResult result;
                try
                {
                    result = await device.ReceiveAsync();
                }
                catch (Exception)
                {
                    return;
                }

                _clientEndPoint = result.RemoteEndPoint;
                _deviceReceived.Add(Encoding.ASCII.GetString(result.Buffer));
                if (!_isEchoing || _deviceReceived.Count <= _ignoreFirstCount) continue;

                byte[] reply = _replyOverride == null ? result.Buffer : Encoding.ASCII.GetBytes(_replyOverride);
                if (_echoDelayMs > 0)
                {
                    ReplyLaterAsync(device, reply, result.RemoteEndPoint, _echoDelayMs).Forget();
                }
                else
                {
                    device.Send(reply, reply.Length, result.RemoteEndPoint);
                }
            }
        }

        /// <summary>
        /// 가짜 장치가 정한 시간 뒤에 답함. 그동안 장치는 다른 명령을 계속 받음.
        /// </summary>
        private async UniTaskVoid ReplyLaterAsync(UdpClient device, byte[] reply, IPEndPoint remote, int delayMs)
        {
            await UniTask.Delay(delayMs, DelayType.Realtime);
            _deviceReceived.Add("(replied)");
            try
            {
                device.Send(reply, reply.Length, remote);
            }
            catch (ObjectDisposedException)
            {
                // 테스트가 먼저 끝나 장치가 닫힌 경우.
            }
        }

        /// <summary>
        /// 가짜 장치가 count개 이상을 받을 때까지 기다림.
        /// </summary>
        private UniTask WaitDeviceReceivedAsync(int count)
        {
            return UniTask.WaitUntil(() => _deviceReceived.Count >= count).AwaitWithRealtimeTimeout(3f, "가짜 장치가 기대한 수만큼 받지 못함");
        }

        /// <summary>
        /// 장치가 명령을 돌려주면 한 번만 보내고 true로 끝나야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 장치가_명령을_돌려주면_한_번만_보내고_true로_끝난다() => UniTask.ToCoroutine(async () =>
        {
            bool isAcknowledged = await _client.SendAsync("1_enter").AwaitWithRealtimeTimeout();

            Assert.IsTrue(isAcknowledged);
            CollectionAssert.AreEqual(new[] { "1_enter" }, _deviceReceived);
        });

        /// <summary>
        /// 장치가 답하지 않으면 정한 횟수만큼 보내고 false로 끝나야 함. 끝없이 보내거나 한 번만 보내면 안 됨.
        /// </summary>
        [UnityTest]
        public IEnumerator 응답이_없으면_정한_횟수만큼_보내고_false로_끝난다() => UniTask.ToCoroutine(async () =>
        {
            _isEchoing = false;
            _client.ReplyTimeoutMs = 100;

            bool isAcknowledged = await _client.SendAsync("1_exit").AwaitWithRealtimeTimeout();
            await WaitDeviceReceivedAsync(3);

            Assert.IsFalse(isAcknowledged);
            Assert.AreEqual(3, _deviceReceived.Count, "보낸 횟수가 MaxSendAttempts와 다름");
        });

        /// <summary>
        /// 첫 전송의 응답이 없다가 다시 보낸 것에 응답이 오면 true로 끝나고 더 보내지 않아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 다시_보낸_것에_응답이_오면_true로_끝나고_더_보내지_않는다() => UniTask.ToCoroutine(async () =>
        {
            _ignoreFirstCount = 1;
            _client.ReplyTimeoutMs = 300;

            bool isAcknowledged = await _client.SendAsync("2_success").AwaitWithRealtimeTimeout();
            await UniTask.Delay(400, DelayType.Realtime);

            Assert.IsTrue(isAcknowledged, "다시 보낸 것의 응답을 받지 못함");
            Assert.AreEqual(2, _deviceReceived.Count, "응답을 받은 뒤에도 더 보냄");
        });

        /// <summary>
        /// 장치가 기다리는 명령과 다른 문자열(버튼 입력 등)을 보내도 응답으로 보지 않아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 다른_문자열은_응답으로_보지_않는다() => UniTask.ToCoroutine(async () =>
        {
            _replyOverride = "BTN1";
            _client.ReplyTimeoutMs = 100;
            _client.MaxSendAttempts = 2;

            bool isAcknowledged = await _client.SendAsync("1_enter").AwaitWithRealtimeTimeout();

            Assert.IsFalse(isAcknowledged, "다른 문자열을 응답으로 봄");
            Assert.AreEqual(2, _deviceReceived.Count);
        });

        /// <summary>
        /// 줄 끝 문자를 붙여 보낸 명령도, 장치가 그대로 돌려주면 응답으로 봐야 함. 받은 쪽만 공백을 빼고 비교하면 영영 맞지 않음.
        /// </summary>
        [UnityTest]
        public IEnumerator 줄_끝_문자가_붙은_명령도_응답을_알아본다() => UniTask.ToCoroutine(async () =>
        {
            bool isAcknowledged = await _client.SendAsync("PLAY\r\n").AwaitWithRealtimeTimeout();

            Assert.IsTrue(isAcknowledged);
            Assert.AreEqual(1, _deviceReceived.Count, "응답을 받고도 다시 보냄");
        });

        /// <summary>
        /// 앞 명령의 응답이 오기 전에는 뒤 명령을 보내지 않아야 함. 순서가 바뀌면 장치 연출이 뒤섞임.
        /// </summary>
        [UnityTest]
        public IEnumerator 앞_명령의_응답이_오기_전에는_뒤_명령을_보내지_않는다() => UniTask.ToCoroutine(async () =>
        {
            _echoDelayMs = 200;

            UniTask<bool> first = _client.SendAsync("A");
            UniTask<bool> second = _client.SendAsync("B");
            UniTask<bool> third = _client.SendAsync("C");
            bool[] results = await UniTask.WhenAll(new[] { first, second, third }).AwaitWithRealtimeTimeout();

            CollectionAssert.AreEqual(new[] { true, true, true }, results);
            CollectionAssert.AreEqual(new[] { "A", "(replied)", "B", "(replied)", "C", "(replied)" }, _deviceReceived,
                "앞 명령의 응답 전에 뒤 명령이 나감");
        });

        /// <summary>
        /// 응답이 없는 명령 뒤에 다음 명령이 기다리면 다시 보내지 않고 넘어가야 함. 장치가 꺼진 동안 뒤 명령이 횟수만큼 밀리면 안 됨.
        /// </summary>
        [UnityTest]
        public IEnumerator 뒤_명령이_기다리면_다시_보내지_않고_넘어간다() => UniTask.ToCoroutine(async () =>
        {
            _isEchoing = false;
            _client.ReplyTimeoutMs = 100;

            UniTask<bool> first = _client.SendAsync("A");
            UniTask<bool> second = _client.SendAsync("B");
            bool[] results = await UniTask.WhenAll(new[] { first, second }).AwaitWithRealtimeTimeout();
            await WaitDeviceReceivedAsync(4);

            CollectionAssert.AreEqual(new[] { false, false }, results);
            CollectionAssert.AreEqual(new[] { "A", "B", "B", "B" }, _deviceReceived, "앞 명령을 다시 보냈거나 마지막 명령을 덜 보냄");
        });

        /// <summary>
        /// 장치가 먼저 보낸 패킷(버튼 입력 등)도 OnDataReceived로 받아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 장치가_보낸_패킷을_OnDataReceived로_받는다() => UniTask.ToCoroutine(async () =>
        {
            List<string> messages = new List<string>();
            using (_client.OnDataReceived.Subscribe(messages.Add))
            {
                // 클라이언트 소켓은 처음 보낼 때 열리므로 명령을 하나 보내 장치가 주소를 알게 함.
                await _client.SendAsync("hello").AwaitWithRealtimeTimeout();

                byte[] bytes = Encoding.ASCII.GetBytes("BTN1\r\n");
                _device.Send(bytes, bytes.Length, _clientEndPoint);
                await UniTask.WaitUntil(() => messages.Contains("BTN1")).AwaitWithRealtimeTimeout(3f, "장치가 보낸 패킷이 발행되지 않음");
            }

            CollectionAssert.AreEqual(new[] { "hello", "BTN1" }, messages, "응답과 장치가 보낸 패킷이 순서대로 발행되지 않음");
        });

        /// <summary>
        /// 주소가 IP가 아니거나 포트가 범위 밖이면 false이고, 이전에 정한 대상은 그대로 남아야 함. 이름 풀이를 기다리면 명령이 늦게 나감.
        /// </summary>
        [UnityTest]
        public IEnumerator 잘못된_주소는_거절하고_이전_대상을_유지한다() => UniTask.ToCoroutine(async () =>
        {
            Assert.IsFalse(_client.SetTarget("device.local", 9000));
            Assert.IsFalse(_client.SetTarget("127.0.0.1", 0));
            Assert.IsFalse(_client.SetTarget("127.0.0.1", 65536));

            bool isAcknowledged = await _client.SendAsync("ping").AwaitWithRealtimeTimeout();

            Assert.IsTrue(isAcknowledged, "잘못된 주소로 대상이 바뀜");
        });

        /// <summary>
        /// Dispose하면 진행 중이던 명령의 대기를 바로 끝내고, 차례를 기다리던 명령을 한 번씩 보낸 뒤 결과는 false로 끝나야 함.
        /// 앱이 끝날 때 종료 명령이 빠지면 장치가 마지막 상태로 남음.
        /// </summary>
        [UnityTest]
        public IEnumerator Dispose하면_기다리던_명령을_한_번씩_보낸다() => UniTask.ToCoroutine(async () =>
        {
            _isEchoing = false;
            _client.ReplyTimeoutMs = 2000;

            UniTask<bool> inFlight = _client.SendAsync("A");
            UniTask<bool> waiting = _client.SendAsync("exit");
            _client.Dispose();

            // 응답 대기(2초)보다 짧게 기다려, 진행 중이던 명령이 대기 시간을 다 채우지 않고 끝나는지 확인함.
            bool[] results = await UniTask.WhenAll(new[] { inFlight, waiting }).AwaitWithRealtimeTimeout(0.5f);
            await WaitDeviceReceivedAsync(2);

            CollectionAssert.AreEqual(new[] { false, false }, results);
            CollectionAssert.AreEqual(new[] { "A", "exit" }, _deviceReceived);
        });
    }
}
