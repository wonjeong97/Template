/*==============================================================================================================================
* Gmail 설정 방법 (필수)
* Gmail을 보내는 메일로 사용하려면 다음 설정이 필요합니다.
* * 1. 구글 계정 관리 > 보안 탭으로 이동.
* * 2. 2단계 인증이 켜져 있어야 합니다.
* * 3. 2단계 인증 설정 하단에 [앱 비밀번호] 항목을 찾아 클릭합니다. (검색창에 '앱 비밀번호' 검색 가능)
* * 4. 앱 이름에 'UnityLog' 등으로 입력하고 생성하기를 누릅니다.
* * 생성된 16자리 비밀번호를 복사해서 위 코드의 senderPassword 변수에 붙여넣으세요. (기존 구글 로그인 비번은 작동하지 않습니다)
*==============================================================================================================================*/

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
                
                string fileName = $"Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                _currentLogPath = Path.Combine(_logFolder, fileName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LogSaver] 로그 폴더 생성 실패({_logFolder}): {e.Message}");
                // 파일 저장은 불가능하지만 로그 수집은 계속
                _currentLogPath = null; // 파일 저장 비활성화 표시
            }

            Application.logMessageReceived += HandleLog;
        }

        private void Start()
        {
            // 에디터가 아닐 때만 미전송 로그 발송 시도
#if !UNITY_EDITOR
            if (enableEmail)
            {
                Task.Run(async () => await TrySendPendingLogsAsync());
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
            if (_logBuffer.Length > 0 && !string.IsNullOrEmpty(_currentLogPath))
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