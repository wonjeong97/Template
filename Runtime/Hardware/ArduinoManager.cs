using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
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
                try
                {
                    SerialPort testPort = new SerialPort(port, baudRate);
                    testPort.ReadTimeout = 5000;
                    testPort.WriteTimeout = 500;
                    testPort.Open();

                    string received = testPort.ReadLine().Trim();
            
                    if (received.Contains(expectedHandshake))
                    {
                        // 응답 전송 로직 삭제, 일치 시 바로 스레드 시작 및 연결 확정
                        _serialPort = testPort;
                        _isRunning = true;
                        _readThread = new Thread(ReadSerialLoop);
                        _readThread.Start();
                
                        Debug.Log($"[ArduinoManager] Connection success: {port}");
                        return;
                    }
            
                    testPort.Close();
                    testPort.Dispose();
                }
                catch (Exception)
                {
                    // 실패 시 다음 포트로 넘어가기 위해 예외 무시
                }
            }
    
            Debug.LogWarning("[ArduinoManager] Can't connect to Arduino");
        }
        
        public void RebootDevice()
        {
            if (!IsConnected) return;

            try
            {
                // DTR 신호를 순간적으로 껐다 켜서 보드 리셋을 유도합니다.
                _serialPort.DtrEnable = false;
                Thread.Sleep(100); 
                _serialPort.DtrEnable = true;
        
                Debug.Log("[ArduinoManager] Rebooting arduino.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArduinoManager] Device reboot failed: {e.Message}");
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
                catch (TimeoutException) 
                { 
                    // 대기 시간 초과 시 루프 유지
                }
                catch (Exception e)
                {
                    if (_isRunning)
                    {
                        Debug.LogWarning($"[ArduinoManager] Read error (Disconnected): {e.Message}");
                        
                        // USB가 강제로 뽑히는 등 치명적 통신 에러 발생 시 즉시 포트를 닫고 상태를 초기화합니다.
                        Disconnect(); 
                    }
                }
            }
        }

        private void OnApplicationQuit()
        {
            Disconnect();
        }
    }
}