using Cysharp.Text;
using UnityEngine;

namespace HuliacDev.Core
{
    /// <summary>
    /// 자동 재시작 설정을 감시 스크립트에 넘길 값과 명령줄로 바꾸는 순수 계산 클래스.
    /// Unity 오브젝트나 프로세스 실행 없이 단위 테스트할 수 있도록 AutoRestartWatchdog에서 분리함
    /// (AutoRestartWatchdog는 스크립트 저장과 프로세스 실행만 담당함).
    /// </summary>
    internal static class AutoRestartPolicy
    {
        /// <summary>hangRestartSeconds가 미지정(음수)일 때 쓰는 응답 없음 판정 시간(초). 큰 씬·영상 로딩으로 메인 스레드가 잠깐 막혀도 걸리지 않을 만큼 둠.</summary>
        public const int DefaultHangRestartSeconds = 60;

        /// <summary>응답 없음 판정 시간의 최소값(초). Windows는 5초 넘게 메시지를 처리하지 못한 창을 응답 없음으로 보므로, 너무 짧으면 잠깐 막힌 앱도 꺼짐.</summary>
        public const int MinHangRestartSeconds = 10;

        /// <summary>restartDelaySeconds가 미지정(0 이하)일 때 쓰는 재시작 대기 시간(초).</summary>
        public const int DefaultRestartDelaySeconds = 5;

        /// <summary>재시작 대기 시간의 최소값(초).</summary>
        public const int MinRestartDelaySeconds = 1;

        /// <summary>
        /// 앱이 켜지자마자 계속 끝날 때 두 배씩 늘려 가는 재시작 간격의 상한(초).
        /// 크래시마다 Unity가 덤프(약 1MB)를 남기므로, 시작하자마자 죽는 상태(설정 오류, 장치 없음 등)에서 디스크가 차지 않게 함.
        /// </summary>
        public const int MaxRestartDelaySeconds = 600;

        /// <summary>앱이 이 시간(초)보다 짧게 실행되고 끝나면 '켜자마자 끝남'으로 셈. 이어서 두 번째부터 재시작 간격을 두 배씩 늘림.</summary>
        public const int QuickExitSeconds = 60;

        /// <summary>
        /// 응답 없음 판정 시간(초)을 정함. 음수(미지정)면 기본값, 0이면 감지 끔(0 반환), 최소값보다 작은 양수면 최소값을 돌려줌.
        /// </summary>
        public static int ResolveHangRestartSeconds(float configuredSeconds)
        {
            if (configuredSeconds < 0f) return DefaultHangRestartSeconds;
            if (configuredSeconds <= 0f) return 0;
            return Mathf.Max(MinHangRestartSeconds, Mathf.CeilToInt(configuredSeconds));
        }

        /// <summary>
        /// 재시작 대기 시간(초)을 정함. 0 이하(미지정)면 기본값, 최소값보다 작은 양수면 최소값, 상한보다 크면 상한을 돌려줌.
        /// 감시 스크립트가 간격을 상한으로 자르므로, 시작 로그에 적는 값도 실제 대기와 같게 여기서 자름.
        /// </summary>
        public static int ResolveRestartDelaySeconds(float configuredSeconds)
        {
            if (configuredSeconds <= 0f) return DefaultRestartDelaySeconds;
            return Mathf.Clamp(Mathf.CeilToInt(configuredSeconds), MinRestartDelaySeconds, MaxRestartDelaySeconds);
        }

        /// <summary>
        /// 감시가 앱을 지켜보기 시작했을 때 신호를 줄 이벤트의 이름을 만듦. 앱마다 다르도록 앱 PID를 붙이고,
        /// 같은 로그온 세션 안에서만 보이도록 Local\ 이름 공간을 씀.
        /// </summary>
        public static string BuildReadyEventName(int appProcessId)
        {
            return ZString.Concat(@"Local\HuliacDev.AutoRestart.Ready.", appProcessId);
        }

        /// <summary>
        /// 감시가 사는 동안 열어 두는 이벤트의 이름을 만듦. 다시 실행된 앱이 환경 변수로 받은 감시 PID가 정말 그 감시인지 확인하는 데 씀.
        /// 감시 스크립트가 같은 이름("Local\HuliacDev.AutoRestart.Alive.$PID")으로 만듦.
        /// </summary>
        public static string BuildAliveEventName(int watchdogProcessId)
        {
            return ZString.Concat(@"Local\HuliacDev.AutoRestart.Alive.", watchdogProcessId);
        }

        /// <summary>
        /// 감시 스크립트를 숨은 PowerShell로 실행할 명령줄 인수를 만듦. 인수 이름은 AutoRestartWatchdog.WatchdogScript의 param과 맞춰야 함.
        /// Windows 경로와 이벤트 이름에는 큰따옴표가 들어가지 않으므로 큰따옴표로만 감쌈.
        /// </summary>
        public static string BuildWatchdogArguments(string scriptPath, int appProcessId, string logPath, string readyEventName,
            int hangRestartSeconds, int restartDelaySeconds)
        {
            return ZString.Format(
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{0}\" -AppPid {1} -LogPath \"{2}\" -ReadyEventName \"{3}\" -HangRestartSeconds {4} -RestartDelaySeconds {5} -MaxRestartDelaySeconds {6} -QuickExitSeconds {7}",
                scriptPath, appProcessId, logPath, readyEventName, hangRestartSeconds, restartDelaySeconds, MaxRestartDelaySeconds, QuickExitSeconds);
        }
    }
}
