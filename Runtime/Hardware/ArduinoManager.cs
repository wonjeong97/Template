using System;
using System.IO.Ports;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using R3;
using UnityEngine;
using VContainer;
using ZLogger;

namespace Wonjeong.Hardware
{
    public class ArduinoManager : MonoBehaviour
    {
        private SerialPort _serialPort;
        private Thread _readThread;
        private volatile bool _isRunning;

        private ILogger<ArduinoManager> _logger;

        private readonly Subject<string> _messageSubject = new Subject<string>();
        
        /// <summary>
        /// 외부에서 구독할 수 있는 아두이노 수신 데이터 스트림.
        /// 백그라운드 스레드 데이터를 유니티 메인 스레드로 안전하게 전달함.
        /// 사용 예: arduinoManager.OnDataReceived.Subscribe(msg => Debug.Log(msg));
        /// </summary>
        public Observable<string> OnDataReceived => _messageSubject.ObserveOnMainThread();
        
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        /// <summary>
        /// VContainer 의존성 주입.
        /// ZLogger 할당.
        /// </summary>
        [Inject]
        public void Construct(ILogger<ArduinoManager> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// 아두이노 장치와 시리얼 통신 연결을 비동기로 시도함.
        /// 하드웨어 인식 지연 및 부팅 시간을 고려하여 1초 간격으로 최대 10회 재시도함.
        /// </summary>
        public async UniTask ConnectAsync(int baudRate, string expectedHandshake)
        {
            if (IsConnected) return;

            int maxRetries = 10;
            int retryDelayMs = 1000;

            for (int i = 0; i < maxRetries; i++)
            {
                bool isSuccess = await UniTask.RunOnThreadPool(() => TryHandshake(baudRate, expectedHandshake));
                
                if (isSuccess)
                {
                    return;
                }

                if (i < maxRetries - 1)
                {
                    if(_logger != null) _logger.ZLogInformation($"[ArduinoManager] Connection retry in {retryDelayMs}ms... ({i + 1}/{maxRetries})");
                    await UniTask.Delay(retryDelayMs, cancellationToken: this.GetCancellationTokenOnDestroy());
                }
            }

            if(_logger != null) _logger.ZLogWarning($"[ArduinoManager] Failed to connect to Arduino after {maxRetries} attempts.");
        }
        
        /// <summary>
        /// 사용 가능한 모든 시리얼 포트를 순회하며 핸드셰이크 응답을 검증함.
        /// 장치가 연결된 올바른 포트를 동적으로 식별하기 위해 수행됨.
        /// </summary>
        private bool TryHandshake(int baudRate, string expectedHandshake)
        {
            string[] ports = SerialPort.GetPortNames();

            foreach (string port in ports)
            {
                if (TryConnectPort(port, baudRate, expectedHandshake))
                {
                    return true;
                }
            }

            return false;
        }
        
        /// <summary>
        /// 지정된 단일 포트에 연결을 시도하고 기대 응답 문자열을 대기함.
        /// DTR 제어를 비활성화하여 보드 자동 초기화를 방지하고 빠른 검증을 수행함.
        /// </summary>
        private bool TryConnectPort(string port, int baudRate, string expectedHandshake)
        {
            try
            {
                SerialPort testPort = new SerialPort(port, baudRate);
        
                testPort.DtrEnable = false; 
                testPort.ReadTimeout = 500;
                testPort.WriteTimeout = 500;
                testPort.Open();

                string received = testPort.ReadLine().Trim();

                if (received.Contains(expectedHandshake))
                {
                    EstablishConnection(testPort, port);
                    return true;
                }

                testPort.Close();
                testPort.Dispose();
            }
            catch (Exception)
            {
                // 다음 포트 연결 테스트를 진행하기 위해 현재 예외를 무시함.
            }

            return false;
        }
        
        /// <summary>
        /// 검증 완료된 시리얼 포트를 인스턴스에 할당하고 수신 스레드를 가동함.
        /// 메인 스레드 점유율을 낮추기 위해 우선순위를 BelowNormal로 설정함.
        /// </summary>
        private void EstablishConnection(SerialPort validPort, string portName)
        {
            _serialPort = validPort;
            _isRunning = true;
    
            _readThread = new Thread(ReadSerialLoop)
            {
                Priority = System.Threading.ThreadPriority.BelowNormal
            };
            _readThread.Start();

            if(_logger != null) _logger.ZLogInformation($"[ArduinoManager] Connection success: {portName}");
        }
        
        /// <summary>
        /// DTR 핀을 제어하여 아두이노를 하드웨어 리셋함.
        /// 비동기 대기 중 오브젝트가 파괴되면 작업을 안전하게 취소함.
        /// </summary>
        public async UniTask RebootDeviceAsync()
        {
            if (!IsConnected) 
            {
                if(_logger != null) _logger.ZLogWarning($"[ArduinoManager] Cannot reboot. Device is not connected.");
                return;
            }

            try
            {
                _isRunning = false; 

                _serialPort.DtrEnable = true;
                await UniTask.Delay(100, cancellationToken: this.GetCancellationTokenOnDestroy());
                _serialPort.DtrEnable = false;

                // 부트로더 대기
                await UniTask.Delay(2000, cancellationToken: this.GetCancellationTokenOnDestroy());

                _isRunning = true;
                _readThread = new Thread(ReadSerialLoop)
                {
                    Priority = System.Threading.ThreadPriority.BelowNormal
                };
                _readThread.Start();

                if(_logger != null) _logger.ZLogInformation($"[ArduinoManager] Device reboot complete. Clean state restored.");
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴로 인한 정상적인 취소이므로 경고를 띄우지 않음.
                if(_logger != null) _logger.ZLogInformation($"[ArduinoManager] Reboot canceled due to object destruction.");
            }
            catch (Exception e)
            {
                if(_logger != null) _logger.ZLogWarning($"[ArduinoManager] Device reboot failed: {e.Message}");
                Disconnect();
            }
        }

        /// <summary>
        /// 현재 열려있는 시리얼 포트를 닫고 통신 스레드를 종료함.
        /// </summary>
        public void Disconnect()
        {
            _isRunning = false;

            if (_readThread != null && _readThread.IsAlive)
            {
                _readThread.Join(600);
            }

            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }

            _serialPort = null;
            if(_logger != null) _logger.ZLogInformation($"[ArduinoManager] Disconnected");
        }

        /// <summary>
        /// 아두이노로 문자열 메시지를 전송함.
        /// </summary>
        public void Send(string msg)
        {
            if (!IsConnected) return;
            
            try
            {
                _serialPort.WriteLine(msg);
            }
            catch (Exception e)
            {
                if(_logger != null) _logger.ZLogWarning($"[ArduinoManager] Send fail: {e.Message}");
            }
        }

        /// <summary>
        /// 시리얼 데이터 수신 메인 루프.
        /// </summary>
        private void ReadSerialLoop()
        {
            while (IsSerialPortReady())
            {
                ProcessNextSerialMessage();
                Thread.Sleep(1); 
            }
        }

        /// <summary>
        /// 시리얼 포트가 읽기 가능한 상태인지 검증함.
        /// </summary>
        private bool IsSerialPortReady()
        {
            return _isRunning && _serialPort != null && _serialPort.IsOpen;
        }

        /// <summary>
        /// 단일 메시지 수신 및 R3 스트림 발행 처리.
        /// </summary>
        private void ProcessNextSerialMessage()
        {
            try
            {
                string data = _serialPort.ReadLine();
        
                if (!string.IsNullOrEmpty(data))
                {
                    _messageSubject.OnNext(data);
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
        /// 읽기 에러 발생 시 예외 처리 로직.
        /// </summary>
        private void HandleReadException(Exception e)
        {
            if (!_isRunning)
            {
                return;
            }

            if(_logger != null) _logger.ZLogWarning($"[ArduinoManager] Read error: {e.Message}");
            Disconnect();
        }

        /// <summary>
        /// 유니티 생명주기 종료 시 시리얼 포트 연결 및 R3 리소스를 안전하게 해제함.
        /// 메모리 누수 방지 목적.
        /// </summary>
        private void OnDestroy()
        {
            Disconnect();
            _messageSubject?.Dispose();
        }

        /// <summary>
        /// 애플리케이션 강제 종료 시 시리얼 포트 점유를 즉시 해제함.
        /// 다음 실행 시 포트 충돌을 방지하기 위함.
        /// </summary>
        private void OnApplicationQuit()
        {
            Disconnect();
        }
    }
}