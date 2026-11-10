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
    /// TcpLineClient 검증. 루프백 주소에 TcpListener로 가짜 장치(TCP 서버)를 띄움.
    /// </summary>
    public class TcpLineClientTests
    {
        private readonly List<string> _lines = new List<string>();
        private TcpListener _listener;
        private TcpClient _server;
        private TcpLineClient _client;
        private IDisposable _subscriptions;
        private int _port;
        private int _connectedCount;
        private int _disconnectedCount;

        /// <summary>
        /// 가짜 장치를 열고, 받은 줄과 연결 변화를 기록하는 클라이언트를 만듦.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _lines.Clear();
            _connectedCount = 0;
            _disconnectedCount = 0;
            _server = null;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _client = new TcpLineClient(null);
            _subscriptions = Disposable.Combine(
                _client.OnDataReceived.Subscribe(_lines.Add),
                _client.OnConnected.Subscribe(_ => _connectedCount++),
                _client.OnDisconnected.Subscribe(_ => _disconnectedCount++));
        }

        /// <summary>
        /// 구독을 풀고 클라이언트와 가짜 장치를 닫음.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _subscriptions.Dispose();
            _client.Dispose();
            if (_server != null) _server.Close();
            _listener.Stop();
        }

        /// <summary>
        /// 가짜 장치가 클라이언트의 접속을 받을 때까지 기다림.
        /// </summary>
        private async UniTask<TcpClient> AcceptAsync()
        {
            return await _listener.AcceptTcpClientAsync().AsUniTask().AwaitWithRealtimeTimeout(3f);
        }

        /// <summary>
        /// 접속하고 가짜 장치가 받은 뒤, 메인 스레드에 접속 이벤트가 올 때까지 기다림.
        /// </summary>
        private async UniTask ConnectAsync()
        {
            Assert.IsTrue(_client.Connect("127.0.0.1", _port));
            _server = await AcceptAsync();
            await UniTask.WaitUntil(() => _connectedCount == 1).AwaitWithRealtimeTimeout(3f, "OnConnected가 발행되지 않음");
        }

        /// <summary>
        /// 가짜 장치가 문자열을 보냄.
        /// </summary>
        private void ServerWrite(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            _server.GetStream().Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// 접속하면 IsConnected가 true가 되고 OnConnected가 한 번 발행돼야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 접속하면_IsConnected와_OnConnected가_바뀐다() => UniTask.ToCoroutine(async () =>
        {
            await ConnectAsync();

            Assert.IsTrue(_client.IsConnected);
            Assert.AreEqual(1, _connectedCount);
        });

        /// <summary>
        /// 장치가 줄을 나눠 보내거나 여러 줄을 한 번에 보내도 줄 단위로 메인 스레드에서 받아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 나뉘어_온_줄도_줄_단위로_받는다() => UniTask.ToCoroutine(async () =>
        {
            await ConnectAsync();

            ServerWrite("S");
            await UniTask.Delay(50, DelayType.Realtime);
            ServerWrite("1\r\nREADY\r\n");
            await UniTask.WaitUntil(() => _lines.Count >= 2).AwaitWithRealtimeTimeout(3f, "줄을 받지 못함");

            CollectionAssert.AreEqual(new[] { "S1", "READY" }, _lines);
        });

        /// <summary>
        /// Send한 문자열이 줄 끝 문자를 붙이지 않고 그대로 장치에 닿아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator Send한_문자열이_그대로_장치에_닿는다() => UniTask.ToCoroutine(async () =>
        {
            await ConnectAsync();

            Assert.IsTrue(_client.Send("h"));
            Assert.IsTrue(_client.Send("S2\n"));

            byte[] buffer = new byte[16];
            StringBuilder received = new StringBuilder();
            NetworkStream stream = _server.GetStream();
            while (received.Length < 4)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length).AsUniTask().AwaitWithRealtimeTimeout(3f);
                if (count <= 0) break;
                received.Append(Encoding.ASCII.GetString(buffer, 0, count));
            }

            Assert.AreEqual("hS2\n", received.ToString());
        });

        /// <summary>
        /// 장치가 접속을 끊으면 OnDisconnected가 발행되고, 장치가 다시 받을 수 있으면 사람 손 없이 다시 접속해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 장치가_끊으면_알리고_다시_접속한다() => UniTask.ToCoroutine(async () =>
        {
            await ConnectAsync();

            _server.Close();
            await UniTask.WaitUntil(() => _disconnectedCount == 1).AwaitWithRealtimeTimeout(3f, "OnDisconnected가 발행되지 않음");
            Assert.IsFalse(_client.IsConnected);

            _server = await AcceptAsync();
            await UniTask.WaitUntil(() => _connectedCount == 2).AwaitWithRealtimeTimeout(3f, "다시 접속하지 않음");
            Assert.IsTrue(_client.IsConnected);
        });

        /// <summary>
        /// Disconnect하면 끊김을 알리고, 장치가 받을 수 있어도 다시 접속하지 않아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator Disconnect하면_다시_접속하지_않는다() => UniTask.ToCoroutine(async () =>
        {
            await ConnectAsync();

            _client.Disconnect();
            await UniTask.WaitUntil(() => _disconnectedCount == 1).AwaitWithRealtimeTimeout(3f, "OnDisconnected가 발행되지 않음");

            // 다시 접속하는 첫 간격(1초)보다 넉넉히 기다려도 새 접속이 없어야 함.
            await UniTask.Delay(1500, DelayType.Realtime);
            Assert.IsFalse(_listener.Pending(), "Disconnect 뒤에 다시 접속함");
            Assert.IsFalse(_client.IsConnected);
            Assert.AreEqual(1, _connectedCount);
        });

        /// <summary>
        /// 접속 중에 다른 주소로 Connect하면 이전 접속을 닫고 새로 접속해야 하며, 이벤트는 끊김 다음 접속 순서로 한 번씩만 와야 함.
        /// 멈춘 이전 루프가 늦게 끊김을 알리면 실제로는 접속 중인데 끊긴 것으로 보임.
        /// </summary>
        [UnityTest]
        public IEnumerator 접속_중에_다른_주소로_Connect하면_이전_접속을_닫고_새로_접속한다() => UniTask.ToCoroutine(async () =>
        {
            await ConnectAsync();

            List<string> events = new List<string>();
            TcpListener other = new TcpListener(IPAddress.Loopback, 0);
            other.Start();
            TcpClient otherServer = null;
            try
            {
                using (Disposable.Combine(
                           _client.OnConnected.Subscribe(_ => events.Add("connected")),
                           _client.OnDisconnected.Subscribe(_ => events.Add("disconnected"))))
                {
                    Assert.IsTrue(_client.Connect("127.0.0.1", ((IPEndPoint)other.LocalEndpoint).Port));
                    otherServer = await other.AcceptTcpClientAsync().AsUniTask().AwaitWithRealtimeTimeout(3f);
                    await UniTask.WaitUntil(() => events.Count >= 2).AwaitWithRealtimeTimeout(3f, "끊김·접속 이벤트가 오지 않음");
                    await UniTask.Delay(200, DelayType.Realtime);

                    CollectionAssert.AreEqual(new[] { "disconnected", "connected" }, events, "이벤트 순서가 틀리거나 이전 루프가 더 발행함");
                    Assert.IsTrue(_client.IsConnected);
                }

                Assert.IsTrue(await IsClosedByClientAsync(_server), "이전 접속을 닫지 않음");

                // 바꾼 뒤 보낸 문자열은 멈춘 이전 루프가 가져가지 않고 새 장치에 닿아야 함.
                Assert.IsTrue(_client.Send("X\n"));
                byte[] buffer = new byte[8];
                int count = await otherServer.GetStream().ReadAsync(buffer, 0, buffer.Length).AsUniTask().AwaitWithRealtimeTimeout(3f);
                Assert.AreEqual("X\n", Encoding.ASCII.GetString(buffer, 0, count), "바꾼 뒤 보낸 문자열이 새 장치에 닿지 않음");
            }
            finally
            {
                if (otherServer != null) otherServer.Close();
                other.Stop();
            }
        });

        /// <summary>
        /// 접속을 받자마자 끊는 장치에는 실패처럼 간격을 늘려야 함. 늘리지 않으면 1초마다 붙었다 끊기며 로그와 이벤트가 쌓임.
        /// </summary>
        [UnityTest]
        public IEnumerator 받자마자_끊는_장치에는_간격을_늘려_다시_접속한다() => UniTask.ToCoroutine(async () =>
        {
            Assert.IsTrue(_client.Connect("127.0.0.1", _port));

            // 간격을 늘리면 0·1·3초에, 늘리지 않으면 0~4초에 매초 접속함.
            int acceptedCount = 0;
            System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 4500)
            {
                if (_listener.Pending())
                {
                    TcpClient accepted = _listener.AcceptTcpClient();
                    accepted.Close();
                    acceptedCount++;
                }

                await UniTask.Delay(20, DelayType.Realtime);
            }

            Assert.LessOrEqual(acceptedCount, 3, "받자마자 끊겨도 처음 간격으로 다시 접속함");
            Assert.GreaterOrEqual(acceptedCount, 2, "다시 접속하지 않음");
        });

        /// <summary>
        /// 클라이언트가 접속을 닫았는지 가짜 장치 쪽에서 확인함. 읽기가 0을 돌려주거나 연결 재설정 예외가 나면 닫힌 것.
        /// </summary>
        private static async UniTask<bool> IsClosedByClientAsync(TcpClient server)
        {
            try
            {
                byte[] buffer = new byte[1];
                int count = await server.GetStream().ReadAsync(buffer, 0, 1).AsUniTask().AwaitWithRealtimeTimeout(3f);
                return count == 0;
            }
            catch (System.IO.IOException)
            {
                return true;
            }
        }

        /// <summary>
        /// 접속돼 있지 않을 때 Send는 보내지 않고 false여야 함. 쌓아 두면 다시 접속한 뒤 예전 명령으로 장치가 움직임.
        /// </summary>
        [Test]
        public void 접속돼_있지_않으면_Send는_false다()
        {
            Assert.IsFalse(_client.Send("S1\n"));
        }

        /// <summary>
        /// 주소가 비었거나 포트가 범위 밖이면 접속을 시작하지 않고 false여야 함.
        /// </summary>
        [Test]
        public void 잘못된_주소면_접속하지_않는다()
        {
            Assert.IsFalse(_client.Connect("", 9000));
            Assert.IsFalse(_client.Connect("127.0.0.1", 0));
            Assert.IsFalse(_client.Connect("127.0.0.1", 70000));
        }
    }
}
