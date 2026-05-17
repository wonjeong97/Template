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
        /// </summary>
        public async UniTask ConnectAsync(int baudRate, string expectedHandshake)
        {
            if (IsConnected) return;

            int maxRetries = 10;
            
            // 재시도 루프 처리를 전담하는 별도 메서드 호출 (복잡도 분리)
            bool isSuccess = await ExecuteConnectionRetriesAsync(baudRate, expectedHandshake, maxRetries, 1000);

            if (!isSuccess && _logger != null)
            {
                _logger.ZLogWarning($"[ArduinoManager] Failed to connect to Arduino after {maxRetries} attempts.");
            }
        }

        /// <summary>
        /// 지정된 횟수만큼 연결 검증 및 대기를 반복함.
        /// </summary>
        private async UniTask<bool> ExecuteConnectionRetriesAsync(int baudRate, string expectedHandshake, int maxRetries, int delayMs)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                if (await TryEstablishConnectionAsync(baudRate, expectedHandshake))
                {
                    return true;
                }

                // 마지막 시도가 아닐 경우에만 대기 및 로그 출력
                if (i < maxRetries - 1)
                {
                    LogRetryAttempt(i + 1, maxRetries, delayMs);
                    await UniTask.Delay(delayMs, cancellationToken: this.GetCancellationTokenOnDestroy());
                }
            }

            return false;
        }

        /// <summary>
        /// 널 체크 및 재시도 로그 출력을 전담함.
        /// </summary>
        private void LogRetryAttempt(int currentAttempt, int maxRetries, int delayMs)
        {
            if (_logger != null)
            {
                _logger.ZLogInformation($"[ArduinoManager] Connection retry in {delayMs}ms... ({currentAttempt}/{maxRetries})");
            }
        }

        /// <summary>
        /// 백그라운드 스레드에서 단일 포트 검증을 수행하고, 
        /// 메인 스레드 복귀 후 안전하게 연결을 할당함.
        /// </summary>
        private async UniTask<bool> TryEstablishConnectionAsync(int baudRate, string expectedHandshake)
        {
            SerialPort validPort = await UniTask.RunOnThreadPool(() => TryHandshake(baudRate, expectedHandshake));
            
            if (validPort == null) 
            {
                return false;
            }

            // 메인 스레드 복귀 후 객체 파괴(취소) 여부 검사
            if (this.GetCancellationTokenOnDestroy().IsCancellationRequested)
            {
                validPort.Close();
                validPort.Dispose();
                return true; // 이미 파괴 중이므로 추가 재시도 루프를 막기 위해 true 반환
            }

            EstablishConnection(validPort, validPort.PortName);
            return true;
        }
        
        /// <summary>
        /// 사용 가능한 모든 시리얼 포트를 순회하며 핸드셰이크 응답을 검증함.
        /// 검증에 성공한 SerialPort 객체를 반환함.
        /// </summary>
        private SerialPort TryHandshake(int baudRate, string expectedHandshake)
        {
            string[] ports = SerialPort.GetPortNames();

            foreach (string port in ports)
            {
                SerialPort validPort = TryConnectPort(port, baudRate, expectedHandshake);
                if (validPort != null)
                {
                    return validPort;
                }
            }

            return null;
        }
        
        /// <summary>
        /// 지정된 단일 포트에 연결을 시도하고 기대 응답 문자열을 대기함.
        /// 백그라운드에서 실행되므로 내부 상태를 변경하지 않고 포트 객체만 반환함.
        /// </summary>
        private SerialPort TryConnectPort(string port, int baudRate, string expectedHandshake)
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
                    // 성공 시 포트를 닫지 않고 그대로 반환하여 메인 스레드에 소유권을 넘김
                    return testPort;
                }

                testPort.Close();
                testPort.Dispose();
            }
            catch (Exception)
            {
                // 다음 포트 연결 테스트를 진행하기 위해 현재 예외를 무시함.
            }

            return null;
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