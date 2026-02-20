/*==============================================================================================================================
* [메일 서버 설정 방법]
* * 1. Gmail 설정 (SMTP: smtp.gmail.com / Port: 587)
* - 구글 계정 관리 > 보안 > 2단계 인증 활성화.
* - [앱 비밀번호]를 생성하여 16자리 코드를 senderPassword에 입력.
* * 2. Microsoft 설정 (SMTP: smtp.office365.com / Port: 587)
* - MS 계정 보안 페이지 > 추가 보안 옵션 > 2단계 인증 활성화.
* - [새 앱 비밀번호 만들기]를 통해 생성된 코드를 senderPassword에 입력.
* - 일반 비밀번호 사용 시 로그인이 차단될 수 있으므로 반드시 앱 비밀번호 사용을 권장합니다.
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

    /// <summary>
    /// 런타임 로그를 수집하여 파일로 저장하고, 설정된 메일 서버(Google/MS)를 통해 전송하는 클래스입니다.
    /// </summary>
    public class LogSaver : MonoBehaviour
    {
        public enum SmtpProvider
        {
            Google,
            Microsoft
        }

        public static LogSaver Instance { get; private set; }

        [Header("Save Settings (PC)")]
        [SerializeField] private bool useCustomPath = false;
        [SerializeField] private string customPath = "C:/Logs";

        [Header("General Settings")]
        [SerializeField] private bool enableEmail = true;
        [SerializeField] private LogTriggerLevel triggerLevel = LogTriggerLevel.ErrorOrAbove;

        [Header("Email Settings")]
        [Tooltip("메일 서비스 제공자를 선택하세요.")]
        [SerializeField] private SmtpProvider smtpProvider = SmtpProvider.Google;

        [SerializeField] private string senderEmail = "your_email@example.com";
        [Tooltip("계정의 일반 비번이 아닌 '앱 비밀번호'를 입력해야 합니다.")]
        [SerializeField] private string senderPassword = "your_app_password";
        [SerializeField] private string recipientEmail = "target_email@example.com";
        [SerializeField] private int smtpPort = 587;

        private readonly StringBuilder _logBuffer = new StringBuilder();
        private string _currentLogPath;
        private string _logFolder;
        private string _productName;
        private bool _shouldSendEmail;

        // 선택된 제공자에 따라 SMTP 서버 주소를 동적으로 반환
        private string SmtpServer => smtpProvider == SmtpProvider.Google ? "smtp.gmail.com" : "smtp.office365.com";

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
            SetupLogPath();
            Application.logMessageReceived += HandleLog;
        }

        private void SetupLogPath()
        {
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
                Debug.LogError($"[LogSaver] 로그 폴더 생성 실패: {e.Message}");
                _currentLogPath = null;
            }
        }

        private void Start()
        {
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
                if (enableEmail)
                {
                    // 에디터/빌드 환경에 따른 발송 로직은 필요에 따라 전처리기로 조절 가능
                    TrySendSingleLog(_currentLogPath);
                }
            }
            _logBuffer.Clear(); // 메모리 관리: 로그 버퍼 비우기
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
                    mail.Subject = $"[{_productName}] Unity Log Report ({smtpProvider})";
                    mail.Body = $"발송 조건: {triggerLevel}\n제공자: {smtpProvider}\n로그 파일을 첨부합니다.";

                    using (Attachment attachment = new Attachment(filePath))
                    {
                        mail.Attachments.Add(attachment);
                        using (SmtpClient smtpClient = new SmtpClient(SmtpServer))
                        {
                            smtpClient.Port = smtpPort;
                            smtpClient.Credentials = new NetworkCredential(senderEmail, senderPassword) as ICredentialsByHost;
                            smtpClient.EnableSsl = true;
                            smtpClient.Timeout = 10000; // MS 서버의 느린 응답에 대비해 10초로 설정
                            smtpClient.Send(mail);
                        }
                    }
                }

                if (File.Exists(filePath)) File.Delete(filePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LogSaver] {smtpProvider} 전송 오류: {e.Message}");
                return false;
            }
        }
    }
}