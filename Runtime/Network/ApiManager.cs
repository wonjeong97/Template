using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.Networking;
using VContainer;
using Wonjeong.Data;
using ZLogger;

namespace Wonjeong.Network
{
    /// <summary>
    /// 프로그램 시작 시 서버에 시작 로그를 1회 전송하는 매니저. 날짜/시간은 서버에서
    /// 수신 시점에 자동으로 기록되므로 클라이언트는 상태 메시지만 전송함.
    /// Settings.json의 apiUrl은 idx_content_device, uid 등 콘텐츠별 쿼리 파라미터가
    /// 이미 포함된 형태(message= 까지)로 서버에서 발급되므로, 여기서는 message 값만
    /// 이어붙여 GET 요청을 보냄. 전시/키오스크 환경에서 네트워크 장애로 앱 실행이
    /// 막히면 안 되므로 실패해도 로그만 남기고 재시도 없이 진행함.
    /// </summary>
    public class ApiManager : MonoBehaviour
    {
        /// <summary>
        /// 오늘 시작 로그를 이미 성공적으로 전송했는지 판별하기 위해 마지막 전송 날짜를
        /// 기기에 저장해두는 PlayerPrefs 키. 같은 날 재실행되면 "재시작"으로 구분함.
        /// </summary>
        private const string LastStartupLogDateKey = "ApiManager_LastStartupLogDate";

        private ILogger<ApiManager> _logger;
        private AppSettingsProvider _settingsProvider;

        [Inject]
        public void Construct(ILogger<ApiManager> logger, AppSettingsProvider settingsProvider)
        {
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        private void Start()
        {
            SendStartupLogAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid SendStartupLogAsync(CancellationToken cancellationToken)
        {
            try
            {
                Settings settings = await _settingsProvider.GetAsync(cancellationToken);

                if (settings == null || string.IsNullOrEmpty(settings.apiUrl))
                {
                    if (_logger != null) _logger.ZLogInformation($"[ApiManager] apiUrl이 설정되지 않아 시작 로그를 전송하지 않음.");
                    return;
                }

                string today = DateTime.Now.ToString("yyyy-MM-dd");
                bool alreadyLoggedToday = PlayerPrefs.GetString(LastStartupLogDateKey, string.Empty) == today;
                string message = alreadyLoggedToday ? "프로그램 재시작 됨" : "프로그램 시작 됨";
                string url = settings.apiUrl + Uri.EscapeDataString(message);

                using UnityWebRequest request = UnityWebRequest.Get(url);
                await request.SendWebRequest().WithCancellation(cancellationToken);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    if (_logger != null) _logger.ZLogInformation($"[ApiManager] 시작 로그 전송 성공: {url}");

                    // 같은 날 재실행 시 "재시작"으로 구분되도록, 전송이 실제로 성공했을 때만 날짜를 갱신함.
                    PlayerPrefs.SetString(LastStartupLogDateKey, today);
                    PlayerPrefs.Save();
                }
                else
                {
                    if (_logger != null) _logger.ZLogError($"[ApiManager] 시작 로그 전송 실패: {url}, {request.error}");
                }
            }
            catch (OperationCanceledException)
            {
                // 오브젝트 파괴로 인한 정상적인 취소
            }
            catch (Exception e)
            {
                if (_logger != null) _logger.ZLogError($"[ApiManager] 시작 로그 전송 중 예외 발생: {e.Message}");
            }
        }
    }
}
