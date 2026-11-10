using NUnit.Framework;
using UnityEngine;
using HuliacDev.Core;
using HuliacDev.Data;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// 자동 재시작 설정 해석(AutoRestartPolicy)과 감시 스크립트 명령줄·본문의 약속 검증.
    /// 감시 스크립트 실행과 프로세스 재시작은 Windows 스탠드얼론 빌드에서만 동작해 테스트할 수 없으므로,
    /// 스크립트에 넘길 값과 C#·스크립트 사이에 맞춰야 하는 이름만 검증함.
    /// </summary>
    public class AutoRestartPolicyTests
    {
        /// <summary>
        /// Settings.json에 hangRestartSeconds를 적지 않으면(표시값 -1) 기본값 60초를 써야 함.
        /// 기본값이 0이면 키를 빠뜨린 프로젝트에서 응답 없음 감지가 조용히 꺼짐.
        /// </summary>
        [Test]
        public void 응답_없음_판정_시간을_적지_않으면_기본값을_쓴다()
        {
            AutoRestartSetting setting = JsonUtility.FromJson<AutoRestartSetting>("{\"enabled\": true}");

            Assert.AreEqual(60, AutoRestartPolicy.ResolveHangRestartSeconds(setting.hangRestartSeconds));
            Assert.AreEqual(5, AutoRestartPolicy.ResolveRestartDelaySeconds(setting.restartDelaySeconds));
        }

        /// <summary>
        /// hangRestartSeconds가 0이면 응답 없음 감지를 끄고(0), 음수는 표시값(-1)이 아니어도 미지정으로 보고,
        /// 최소값보다 작은 양수는 최소값으로 올려야 함.
        /// </summary>
        [Test]
        public void 응답_없음_판정_시간은_0이면_끄고_짧으면_최소값으로_올린다()
        {
            Assert.AreEqual(0, AutoRestartPolicy.ResolveHangRestartSeconds(0f));
            Assert.AreEqual(60, AutoRestartPolicy.ResolveHangRestartSeconds(-5f), "-1이 아닌 음수를 끔으로 봄");
            Assert.AreEqual(10, AutoRestartPolicy.ResolveHangRestartSeconds(3f));
            Assert.AreEqual(90, AutoRestartPolicy.ResolveHangRestartSeconds(90f));
            Assert.AreEqual(31, AutoRestartPolicy.ResolveHangRestartSeconds(30.5f), "소수 초를 버려 판정이 더 짧아짐");
        }

        /// <summary>
        /// restartDelaySeconds가 0 이하면 기본값을, 최소값보다 작은 양수면 최소값을, 상한(600초)보다 크면 상한을 써야 함.
        /// 상한을 넘는 값을 그대로 두면 시작 로그에 적힌 대기 시간이 실제 대기(스크립트가 상한으로 자름)와 달라짐.
        /// </summary>
        [Test]
        public void 재시작_대기_시간은_0_이하면_기본값이고_범위를_벗어나면_최소값이나_상한으로_맞춘다()
        {
            Assert.AreEqual(5, AutoRestartPolicy.ResolveRestartDelaySeconds(0f));
            Assert.AreEqual(5, AutoRestartPolicy.ResolveRestartDelaySeconds(-3f));
            Assert.AreEqual(1, AutoRestartPolicy.ResolveRestartDelaySeconds(0.2f));
            Assert.AreEqual(20, AutoRestartPolicy.ResolveRestartDelaySeconds(20f));
            Assert.AreEqual(600, AutoRestartPolicy.ResolveRestartDelaySeconds(900f));
        }

        /// <summary>
        /// 명령줄의 인수 이름과 값이 스크립트 param과 맞아야 함. 한쪽만 바꾸면 PowerShell이 필수 인수 없음으로 바로 끝나
        /// 감시가 조용히 동작하지 않고, 값의 순서가 바뀌면 응답 없음 판정과 재시작 간격이 뒤바뀌므로, 값마다 다른 수를 넘겨 확인함.
        /// </summary>
        [Test]
        public void 명령줄_인수_이름과_값이_스크립트_param과_맞는다()
        {
            string arguments = AutoRestartPolicy.BuildWatchdogArguments(@"C:\Data\AutoRestartWatchdog.ps1", 1234, @"C:\Data\Logs\AutoRestart.log",
                @"Local\HuliacDev.AutoRestart.Ready.1234", 77, 3);

            StringAssert.Contains("-File \"C:\\Data\\AutoRestartWatchdog.ps1\"", arguments);
            StringAssert.Contains("-AppPid 1234 ", arguments);
            StringAssert.Contains("-LogPath \"C:\\Data\\Logs\\AutoRestart.log\"", arguments);
            StringAssert.Contains("-ReadyEventName \"Local\\HuliacDev.AutoRestart.Ready.1234\"", arguments);
            StringAssert.Contains("-HangRestartSeconds 77 ", arguments);
            StringAssert.Contains("-RestartDelaySeconds 3 ", arguments);
            StringAssert.Contains("-MaxRestartDelaySeconds " + AutoRestartPolicy.MaxRestartDelaySeconds + " ", arguments);
            StringAssert.EndsWith("-QuickExitSeconds " + AutoRestartPolicy.QuickExitSeconds, arguments);

            // 본문에서 쓰는 이름이 아니라 param 선언("[int]$AppPid" 등)과 맞는지 봄.
            string[] parameters = { "AppPid", "LogPath", "ReadyEventName", "HangRestartSeconds", "RestartDelaySeconds", "MaxRestartDelaySeconds", "QuickExitSeconds" };
            foreach (string parameter in parameters)
            {
                StringAssert.Contains("]$" + parameter, AutoRestartWatchdog.WatchdogScript, "스크립트에 param 선언이 없음: " + parameter);
                StringAssert.Contains("-" + parameter + " ", arguments, "명령줄에 인수가 없음: " + parameter);
            }
        }

        /// <summary>
        /// 스크립트가 다시 실행한 앱에 넘기는 환경 변수 이름이 C# 쪽 이름과 같아야 함. 다르면 다시 실행된 앱이
        /// 감시가 살아 있는지 몰라 감시를 또 띄우고, 두 감시가 각자 앱을 다시 실행해 앱이 두 개 뜸.
        /// </summary>
        [Test]
        public void 스크립트의_환경_변수_이름이_CSharp과_맞는다()
        {
            StringAssert.Contains("$env:" + AutoRestartWatchdog.WatchdogPidVariable + " =", AutoRestartWatchdog.WatchdogScript);
            StringAssert.Contains("$env:" + AutoRestartWatchdog.RestartReasonVariable + " =", AutoRestartWatchdog.WatchdogScript);
        }

        /// <summary>
        /// 감시가 신호를 줄 이벤트 이름은 앱마다 달라야 하고(같은 PC의 다른 앱 감시와 섞이지 않게), 스크립트가 그 인수로 이벤트를 열어야 함.
        /// 스크립트가 열지 않으면 감시가 지켜보는데도 IsWatching이 켜지지 않아 TryRequestRestart가 늘 false를 돌려줌.
        /// </summary>
        [Test]
        public void 대기_이벤트_이름은_앱마다_다르고_스크립트가_그_이름으로_신호를_준다()
        {
            Assert.AreEqual(@"Local\HuliacDev.AutoRestart.Ready.1234", AutoRestartPolicy.BuildReadyEventName(1234));
            Assert.AreNotEqual(AutoRestartPolicy.BuildReadyEventName(1234), AutoRestartPolicy.BuildReadyEventName(5678));

            StringAssert.Contains("[System.Threading.EventWaitHandle]::OpenExisting($ReadyEventName)", AutoRestartWatchdog.WatchdogScript);
            StringAssert.Contains("$readyEvent.Set()", AutoRestartWatchdog.WatchdogScript);

            // 앱 핸들을 열기 전에 신호를 주면 앱이 바로 0이 아닌 코드로 끝날 때 감시가 핸들을 열지 못해 다시 켜지 않음.
            // 핸들 여는 줄이 없으면 IndexOf가 -1이라 순서 검사가 늘 통과하므로 있는지부터 확인함.
            StringAssert.Contains("$null = $app.Handle", AutoRestartWatchdog.WatchdogScript);
            Assert.Less(AutoRestartWatchdog.WatchdogScript.IndexOf("$null = $app.Handle", System.StringComparison.Ordinal),
                AutoRestartWatchdog.WatchdogScript.IndexOf("$readyEvent.Set()", System.StringComparison.Ordinal),
                "앱 핸들을 연 뒤에 신호를 줘야 함");
        }

        /// <summary>
        /// 재시작 요청 종료 코드가 스크립트의 분기 값과 같아야 함. 다르면 요청한 재시작도 다시 켜지기는 하지만
        /// 다시 켠 앱의 RestartReason과 AutoRestart.log에 일반 종료 코드로 남아 크래시와 구분되지 않음.
        /// </summary>
        [Test]
        public void 재시작_요청_종료_코드가_스크립트와_맞는다()
        {
            int code = AutoRestartWatchdog.RestartRequestedExitCode;

            Assert.AreNotEqual(0, code, "0이면 감시가 일부러 끈 것으로 보고 다시 켜지 않음");
            StringAssert.Contains("$exitCode -eq " + code + ")", AutoRestartWatchdog.WatchdogScript);
            StringAssert.Contains("restart requested by the app (exit code " + code + ")", AutoRestartWatchdog.WatchdogScript);
        }

        /// <summary>
        /// 환경 변수가 없거나 비어 있으면 다시 켠 실행이 아니므로 null이어야 함. 빈 문자열을 그대로 두면 WasRestarted가 true가 됨.
        /// </summary>
        [Test]
        public void 재시작_이유는_비어_있으면_null이다()
        {
            Assert.IsNull(AutoRestartWatchdog.NormalizeRestartReason(null));
            Assert.IsNull(AutoRestartWatchdog.NormalizeRestartReason(string.Empty));
            Assert.AreEqual("exit code 1 (0x00000001)", AutoRestartWatchdog.NormalizeRestartReason("exit code 1 (0x00000001)"));
        }

        /// <summary>
        /// 감시가 지켜보지 않는 환경(에디터)에서 재시작을 요청하면 끝내지 않고 false를 돌려줘야 하며, 종료 사유·코드도 바꾸지 않아야 함.
        /// 바꾸면 이후 사용자가 Alt+F4로 끌 때 0이 아닌 코드로 끝나거나 종료 로그 사유가 틀어짐.
        /// </summary>
        [Test]
        public void 지켜보지_않으면_재시작을_요청해도_끝내지_않고_종료_사유를_바꾸지_않는다()
        {
            Assert.IsFalse(AutoRestartWatchdog.IsWatching, "에디터에서는 감시를 띄우지 않음");
            Assert.IsFalse(AutoRestartWatchdog.WasRestarted, "에디터는 감시가 다시 켠 실행이 아님");

            string reasonBefore = QuitReason.Current;
            int exitCodeBefore = QuitReason.ExitCode;

            Assert.IsFalse(AutoRestartWatchdog.TryRequestRestart("Test"));
            Assert.AreEqual(reasonBefore, QuitReason.Current);
            Assert.AreEqual(exitCodeBefore, QuitReason.ExitCode);
        }
    }
}
