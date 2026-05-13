using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ThreadPriority = System.Threading.ThreadPriority;

namespace Wonjeong.Hardware
{
    public class ArduinoManager : MonoBehaviour
    {
        private static ArduinoManager _instance;
        public static ArduinoManager Instance
        {
            get
            {
                if (!_instance)
                {
                    _instance = FindFirstObjectByType<ArduinoManager>();
                    if (!_instance)
                    {
                        GameObject go = new GameObject("ArduinoManager");
                        _instance = go.AddComponent<ArduinoManager>();
                    }
                }
                return _instance;
            }
        }

        private SerialPort _serialPort;
        private Thread _readThread;
        private volatile bool _isRunning;
        private readonly ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();

        // 데이터 수신 이벤트
        public event Action<string> OnDataReceived;
        
        // 연결 상태
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        private void Awake()
        {
            if (!_instance) 
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }
        
        private void Update()
        {
            while (_messageQueue.TryDequeue(out string message))
            {
                OnDataReceived?.Invoke(message);
            }
        }
        
        public void Connect(int baudRate, string expectedHandshake)
        {
            if (IsConnected)
            {
                return;
            }

            Task.Run(() => TryHandshake(baudRate, expectedHandshake));
        }
        
        private void TryHandshake(int baudRate, string expectedHandshake)
        {
            string[] ports = SerialPort.GetPortNames();

            foreach (string port in ports)
            {
                if (TryConnectPort(port, baudRate, expectedHandshake))
                {
                    return;
                }
            }

            Debug.LogWarning("[ArduinoManager] Can't connect to Arduino.");
        }
        
        private bool TryConnectPort(string port, int baudRate, string expectedHandshake)
        {
            try
            {
                SerialPort testPort = new SerialPort(port, baudRate);
        
                // Prevents Arduino auto-reset on connection to ensure fast response within 500ms.
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
                // Fails gracefully to allow testing the next available port.
            }

            return false;
        }
        
        private void EstablishConnection(SerialPort validPort, string portName)
        {
            _serialPort = validPort;
            _isRunning = true;
    
            _readThread = new Thread(ReadSerialLoop);
            _readThread.Priority = ThreadPriority.BelowNormal; 
            _readThread.Start();

            Debug.Log($"[ArduinoManager] Connection success: {portName}");
        }
        
        public async Task RebootDeviceAsync()
        {
            if (!IsConnected) 
            {
                Debug.LogWarning("[ArduinoManager] Cannot reboot. Device is not connected.");
                return;
            }

            try
            {
                // 불완전한 시리얼 데이터 파싱 방지.
                _isRunning = false; 

                // DTR 핀 제어로 하드웨어 리셋 유도.
                _serialPort.DtrEnable = true;
                await Task.Delay(100);
                _serialPort.DtrEnable = false;

                // 부트로더 대기.
                await Task.Delay(2000);

                ClearMessageQueue();

                // 초기화 완료 후 읽기 루프 재시작.
                _isRunning = true;
                _readThread = new Thread(ReadSerialLoop);
                _readThread.Priority = ThreadPriority.BelowNormal;
                _readThread.Start();

                Debug.Log("[ArduinoManager] Device reboot complete. Clean state restored.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArduinoManager] Device reboot failed: {e.Message}");
                Disconnect();
            }
        }
        
        private void ClearMessageQueue()
        {
            while (_messageQueue.TryDequeue(out string discardedMessage)) 
            {
                // 이전 루프의 잔여 데이터 폐기.
            }
        }

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
            Debug.Log("[ArduinoManager] Disconnected");
        }

        public void Send(string msg)
        {
            if (!IsConnected) return;
            
            try
            {
                _serialPort.WriteLine(msg);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArduinoManager] Send fail: {e.Message}");
            }
        }

        private void ReadSerialLoop()
        {
            while (IsSerialPortReady())
            {
                ReadAndQueueData();
        
                // Yields execution to prevent CPU spikes if ReadLine timeouts frequently.
                Thread.Sleep(1); 
            }
        }
        
        private bool IsSerialPortReady()
        {
            return _isRunning && _serialPort != null && _serialPort.IsOpen;
        }
        
        private void ReadAndQueueData()
        {
            try
            {
                string data = _serialPort.ReadLine();
        
                if (string.IsNullOrEmpty(data))
                {
                    return;
                }
        
                _messageQueue.Enqueue(data);
            }
            catch (TimeoutException)
            {
                // Timeout is expected behavior for blocking reads. Keeps the loop active.
            }
            catch (Exception e)
            {
                HandleReadException(e);
            }
        }
        
        private void HandleReadException(Exception e)
        {
            // Ignores exceptions if the thread is already requested to stop.
            if (!_isRunning)
            {
                return;
            }

            Debug.LogWarning($"[ArduinoManager] Read error (Disconnected): {e.Message}");
            Disconnect();
        }

        private void OnApplicationQuit()
        {
            Disconnect();
        }
    }
}