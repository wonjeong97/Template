using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using Wonjeong.Data;
using Wonjeong.Utils;

namespace Wonjeong.Hardware
{
    public class ArduinoManager : MonoBehaviour
    {
        private static ArduinoManager _instance;
        public static ArduinoManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<ArduinoManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("ArduinoManager");
                        _instance = go.AddComponent<ArduinoManager>();
                    }
                }
                return _instance;
            }
        }

        [Header("Serial Settings (JSON에서 로드됨)")]
        public string portName = "COM3"; // 기본값
        public int baudRate = 9600;
        public bool autoConnect = true;

        private SerialPort _serialPort;
        private Thread _readThread;
        private volatile bool _isRunning = false;

        // 스레드 간 안전한 데이터 전달을 위한 큐
        private readonly ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();

        // 데이터 수신 이벤트
        public event Action<string> OnDataReceived;
        
        // 연결 상태
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        private void Awake()
        {
            if (_instance == null) 
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            // 1. JSON 설정 로드
            LoadSettings();

            // 2. 설정에 따라 자동 연결 시도
            if (autoConnect)
            {
                Connect();
            }
        }

        /// <summary> StreamingAssets/Settings.json에서 시리얼 설정을 불러와 적용 </summary>
        private void LoadSettings()
        {
            // JsonLoader를 통해 Settings 객체 전체를 불러옴
            Settings data = JsonLoader.Load<Settings>("Settings.json");

            if (data != null && data.serial != null)
            {
                this.portName = data.serial.portName;
                this.baudRate = data.serial.baudRate;
                this.autoConnect = data.serial.autoConnect;
                
                Debug.Log($"[ArduinoManager] JSON 설정 로드 완료: {portName} / {baudRate}");
            }
            else
            {
                Debug.LogWarning("[ArduinoManager] Settings.json에서 'serial' 설정을 찾을 수 없어 기본값을 사용합니다.");
            }
        }

        public void Connect()
        {
            if (IsConnected) return;

            try
            {
                _serialPort = new SerialPort(portName, baudRate);
                _serialPort.ReadTimeout = 500;
                _serialPort.WriteTimeout = 500;
                _serialPort.Open();

                _isRunning = true;
                
                _readThread = new Thread(ReadSerialLoop);
                _readThread.Start();

                Debug.Log($"[ArduinoManager] {portName} 연결 성공");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArduinoManager] 연결 실패 ({portName}): {e.Message}");
            }
        }

        public void Disconnect()
        {
            _isRunning = false;

            if (_readThread != null && _readThread.IsAlive)
            {
                _readThread.Join(600); // ReadTimeout(500ms)보다 약간 길게 설정
            }

            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }

            _serialPort = null;
            Debug.Log("[ArduinoManager] 연결 해제됨");
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
                Debug.LogWarning($"[ArduinoManager] 전송 실패: {e.Message}");
            }
        }

        private void ReadSerialLoop()
        {
            while (_isRunning && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    string data = _serialPort.ReadLine();
                    if (!string.IsNullOrEmpty(data))
                    {
                        _messageQueue.Enqueue(data);
                    }
                }
                catch (TimeoutException) { }
                catch (Exception e)
                {
                    if (_isRunning) Debug.LogWarning($"[ReadThread] Error: {e.Message}");
                }
            }
        }

        private void OnApplicationQuit()
        {
            Disconnect();
        }
        
        private void Update()
        {
            while (_messageQueue.TryDequeue(out string message))
            {
                OnDataReceived?.Invoke(message);
            }
        }
    }
}