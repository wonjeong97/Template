using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Wonjeong.Utils
{
    public enum LogTriggerLevel
    {
        Everything,      // 모든 로그
        WarningOrAbove,  // 경고, 에러, 예외
        ErrorOrAbove     // 에러, 예외 (기본값)
    }

    public class LogSaver : MonoBehaviour
    {
        public static LogSaver Instance { get; private set; }

        [Header("Save Settings (PC)")]
        [SerializeField] private bool useCustomPath = false;
        [SerializeField] private string customPath = "C:/Logs";

        [Header("General Settings")]
        [SerializeField] private bool enableEmail = true;
        [SerializeField] private LogTriggerLevel triggerLevel = LogTriggerLevel.ErrorOrAbove;

        [Header("Email Settings")]
        [SerializeField] private string senderEmail = "your_email@gmail.com";
        [SerializeField] private string senderPassword = "your_app_password";
        [SerializeField] private string recipientEmail = "target_email@example.com";
        [SerializeField] private string smtpServer = "smtp.gmail.com";
        [SerializeField] private int smtpPort = 587;

        private readonly StringBuilder _logBuffer = new StringBuilder();
        private string _currentLogPath;
        private string _logFolder;
        private string _productName;
        private bool _shouldSendEmail; 

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }
            
            _productName = Application.productName;

            if (useCustomPath && !string.IsNullOrEmpty(customPath))
            {
                _logFolder = customPath;
            }
            else
            {
                string basePath = Path.GetDirectoryName(Application.dataPath);
                _logFolder = Path.Combine(basePath, "Logs");
            }

            try
            {
                if (!Directory.Exists(_logFolder)) Directory.CreateDirectory(_logFolder);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LogSaver] 로그 폴더 생성 실패({_logFolder}): {e.Message}");
                return;
            }
            
            string fileName = $"Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            _currentLogPath = Path.Combine(_logFolder, fileName);

            Application.logMessageReceived += HandleLog;
        }

        private void Start()
        {
            // 에디터가 아닐 때만 미전송 로그 발송 시도
#if !UNITY_EDITOR
            if (enableEmail)
            {
                TrySendPendingLogsAsync().ConfigureAwait(false);
            }
#endif
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string logString, string stackTrace, LogType type)
        {
            bool isTrigger = false;
            switch (triggerLevel)
            {
                case LogTriggerLevel.Everything: isTrigger = true; break;
                case LogTriggerLevel.WarningOrAbove: if (type != LogType.Log) isTrigger = true; break;
                case LogTriggerLevel.ErrorOrAbove:
                    if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                        isTrigger = true;
                    break;
            }

            if (isTrigger) _shouldSendEmail = true;

            _logBuffer.AppendLine($"[{DateTime.Now:HH:mm:ss}] [{type}] {logString}");
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _logBuffer.AppendLine($"Stack Trace: {stackTrace}");
            }
        }

        private void OnApplicationQuit()
        {
            if (_shouldSendEmail)
            {
                SaveLogToFile();

                // 에디터가 아닐 때만 메일 전송 시도
#if !UNITY_EDITOR
                if (enableEmail)
                {
                    TrySendSingleLog(_currentLogPath); 
                }
#endif
            }
            _logBuffer.Clear(); // 메모리 정리
        }

        private void SaveLogToFile()
        {
            if (_logBuffer.Length > 0)
            {
                try 
                {
                    File.WriteAllText(_currentLogPath, _logBuffer.ToString());
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LogSaver] 파일 저장 실패: {e.Message}");
                }
            }
        }

        private async Task TrySendPendingLogsAsync()
        {
            if (!Directory.Exists(_logFolder)) return;

            string[] files = Directory.GetFiles(_logFolder, "*.txt");
            if (files.Length == 0) return;

            foreach (string filePath in files)
            {
                if (filePath == _currentLogPath) continue;
                bool success = await Task.Run(() => TrySendSingleLog(filePath));
                if (!success) break; 
            }
        }

        private bool TrySendSingleLog(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                using (MailMessage mail = new MailMessage())
                {
                    mail.From = new MailAddress(senderEmail);
                    mail.To.Add(recipientEmail);
                    mail.Subject = $"[{_productName}] Unity Log Report"; 
                    mail.Body = $"발송 조건: {triggerLevel}\n로그 파일을 첨부합니다.";

                    using (Attachment attachment = new Attachment(filePath))
                    {
                        mail.Attachments.Add(attachment);
                        using (SmtpClient smtpClient = new SmtpClient(smtpServer))
                        {
                            smtpClient.Port = smtpPort;
                            smtpClient.Credentials = new NetworkCredential(senderEmail, senderPassword) as ICredentialsByHost;
                            smtpClient.EnableSsl = true;
                            smtpClient.Timeout = 5000;
                            ServicePointManager.ServerCertificateValidationCallback = (s, certificate, chain, sslPolicyErrors) => true;
                            smtpClient.Send(mail);
                        }
                    } 
                }

                if (File.Exists(filePath)) File.Delete(filePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LogSaver] 처리 중 오류 발생: {e.Message}");
                return false;
            }
        }
    }
}