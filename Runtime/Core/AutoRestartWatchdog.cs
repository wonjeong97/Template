using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer.Unity;
using HuliacDev.Data;
using ZLogger;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.IO;
using System.Text;
#endif

namespace HuliacDev.Core
{
    /// <summary>
    /// 앱이 크래시·강제 종료·응답 없음으로 끝나면 다시 실행하는 감시 스크립트(PowerShell)를 앱 시작 시 띄우는 서비스(Windows 스탠드얼론 빌드 전용).
    /// <para>
    /// 감시 스크립트는 앱 프로세스가 끝나기를 기다렸다가 종료 코드를 봄. GameCloser·ShutdownScheduler·Alt+F4·창 닫기는 모두
    /// Application.Quit 경로로 끝나 종료 코드가 0이므로, 일부러 끈 것으로 보고 다시 실행하지 않고 감시도 함께 끝냄.
    /// 크래시(접근 위반 0xC0000005 등)와 taskkill /F처럼 프로세스를 바로 끝낸 경우(1)는 앱 코드가 실행될 기회가 없어 QuitReason으로는
    /// 구분할 수 없지만 종료 코드가 0이 아니므로 다시 실행함. 창이 Windows에 '응답 없음'으로 판정된 상태가 정해진 시간 넘게
    /// 이어지면 강제 종료하고 다시 실행함.
    /// </para>
    /// <para>
    /// 감시 스크립트는 다시 실행한 앱도 계속 지켜보며, 다시 실행한 앱에 환경 변수로 자기 PID와 재시작 이유를 넘김.
    /// 다시 실행된 앱은 그 감시가 살아 있으면 감시를 또 띄우지 않고 재시작 이유만 로그에 남김.
    /// 감시 스크립트가 시작·재시작·종료한 시각과 이유는 로그 폴더의 AutoRestart.log에 남음.
    /// </para>
    /// <para>
    /// 현장 PC에 설치·작업 스케줄러 등록 없이 동작하도록 스크립트를 이 클래스에 문자열로 두고, 시작할 때마다 persistentDataPath에 씀
    /// (패키지 버전과 스크립트가 항상 맞음). Settings.json의 autoRestart.enabled가 true일 때만 띄우며, 에디터와 다른 플랫폼에서는 띄우지 않음.
    /// </para>
    /// </summary>
    public sealed class AutoRestartWatchdog : IStartable, IDisposable
    {
        /// <summary>감시 스크립트가 다시 실행한 앱에 자기 PID를 알려 주는 환경 변수 이름. WatchdogScript와 맞춰야 함.</summary>
        internal const string WatchdogPidVariable = "HULIACDEV_AUTORESTART_WATCHDOG_PID";

        /// <summary>감시 스크립트가 다시 실행한 앱에 재시작 이유를 알려 주는 환경 변수 이름. WatchdogScript와 맞춰야 함.</summary>
        internal const string RestartReasonVariable = "HULIACDEV_AUTORESTART_REASON";

        /// <summary>
        /// 감시 스크립트 본문. 인수 이름은 AutoRestartPolicy.BuildWatchdogArguments와 맞춰야 함.
        /// 앱의 종료 코드를 읽으려면 앱이 살아 있을 때 프로세스 핸들을 열어 둬야 하므로, 시작하자마자 핸들을 엶.
        /// 다시 실행할 때는 ShellExecute(보안 경고 창이 뜰 수 있음)가 아니라 CreateProcess로 같은 실행 파일과 인수를 씀.
        /// </summary>
        internal const string WatchdogScript = @"# This file is rewritten by HuliacDev Template AutoRestartWatchdog every time the app starts. Do not edit.
param(
    [Parameter(Mandatory = $true)][int]$AppPid,
    [Parameter(Mandatory = $true)][string]$LogPath,
    [int]$HangRestartSeconds = 60,
    [int]$RestartDelaySeconds = 5,
    [int]$MaxRestartDelaySeconds = 600,
    [int]$QuickExitSeconds = 60
)

$ErrorActionPreference = 'Stop'

# 대기 시간이 0이면 간격 계산이 NaN이 되어 감시가 예외로 끝나므로 최소 1초로 맞춤(C#에서도 1초 이상으로 넘김).
if ($RestartDelaySeconds -lt 1) { $RestartDelaySeconds = 1 }

function Write-WatchdogLog([string]$message) {
    try {
        $line = '{0} | {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
        Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    } catch { }
}

# 켜자마자 끝난 횟수가 이어질수록 두 번째부터 간격을 두 배씩 늘림(처음 한 번은 원래 간격).
function Get-RestartDelay([int]$quickExitCount) {
    return [int][Math]::Min($MaxRestartDelaySeconds, $RestartDelaySeconds * [Math]::Pow(2, [Math]::Max(0, $quickExitCount - 1)))
}

try {
    Add-Type -Namespace HuliacDev -Name AutoRestartNative -MemberDefinition '[DllImport(""user32.dll"")] public static extern bool IsHungAppWindow(System.IntPtr hWnd); [DllImport(""user32.dll"")] public static extern bool IsWindow(System.IntPtr hWnd); [DllImport(""user32.dll"")] public static extern int GetSystemMetrics(int index);'
} catch {
    Write-WatchdogLog ""Could not load user32 functions; watchdog exits: $_""
    exit 1
}

# GetSystemMetrics(SM_SHUTTINGDOWN)이 0이 아니면 Windows가 종료·로그오프 중이므로 다시 실행하지 않음.
function Test-ShuttingDown {
    return [HuliacDev.AutoRestartNative]::GetSystemMetrics(0x2000) -ne 0
}

try {
    $app = [System.Diagnostics.Process]::GetProcessById($AppPid)
    # 앱이 끝난 뒤에도 ExitCode를 읽으려면 실행 중에 핸들을 열어 둬야 함.
    $null = $app.Handle
    $exePath = $app.MainModule.FileName
    $startedAt = $app.StartTime
} catch {
    Write-WatchdogLog ""Could not open the app process (pid $AppPid); watchdog exits: $_""
    exit 1
}

$commandLine = $null
try {
    $commandLine = (Get-CimInstance -ClassName Win32_Process -Filter ""ProcessId = $AppPid"").CommandLine
} catch {
    Write-WatchdogLog ""Could not read the app command line; restarts will use no arguments: $_""
}

# 다시 실행할 때 같은 인수(-screen-fullscreen 등)를 넘기도록 명령줄에서 실행 파일 부분만 뺌.
$arguments = ''
if ($commandLine) {
    $trimmed = $commandLine.TrimStart()
    if ($trimmed.StartsWith('""')) {
        $end = $trimmed.IndexOf('""', 1)
        if ($end -ge 0) { $arguments = $trimmed.Substring($end + 1).Trim() }
    } elseif ($trimmed.StartsWith($exePath, [StringComparison]::OrdinalIgnoreCase)) {
        # 따옴표 없이 공백 든 경로로 실행된 경우 경로 중간에서 자르지 않도록 실행 파일 경로 길이만큼 뺌.
        $arguments = $trimmed.Substring($exePath.Length).Trim()
    } elseif ($trimmed -match '^\S+\s+(.*)$') {
        $arguments = $Matches[1].Trim()
    }
}

# 이 감시가 다시 실행한 앱이 감시를 또 띄우지 않도록 PID를 넘김(자식 프로세스는 환경 변수를 물려받음).
$env:HULIACDEV_AUTORESTART_WATCHDOG_PID = [string]$PID

if ($HangRestartSeconds -gt 0) { $hangText = ""${HangRestartSeconds}s"" } else { $hangText = 'off' }
Write-WatchdogLog (""Watching {0} (pid {1}). Hang restart: {2}."" -f $exePath, $AppPid, $hangText)

$quickExitCount = 0

while ($true) {
    $window = [IntPtr]::Zero
    $hungSince = $null
    $killedForHang = $false

    while (-not $app.WaitForExit(2000)) {
        if ($HangRestartSeconds -le 0) { continue }

        # 응답 없는 창은 Windows가 고스트 창으로 바꾸면서 숨겨 MainWindowHandle이 0이 될 수 있으므로, 처음 찾은 창을 계속 씀.
        if ($window -eq [IntPtr]::Zero -or -not [HuliacDev.AutoRestartNative]::IsWindow($window)) {
            # 그 사이 앱이 끝나면 MainWindowHandle이 예외를 던지므로, 창 없음으로 보고 다음 WaitForExit에서 종료를 처리함.
            try {
                $app.Refresh()
                $window = $app.MainWindowHandle
            } catch {
                $window = [IntPtr]::Zero
            }
            if ($window -eq [IntPtr]::Zero) {
                $hungSince = $null
                continue
            }
        }

        if (-not [HuliacDev.AutoRestartNative]::IsHungAppWindow($window)) {
            $hungSince = $null
            continue
        }

        if ($null -eq $hungSince) {
            $hungSince = Get-Date
            continue
        }

        if (((Get-Date) - $hungSince).TotalSeconds -lt $HangRestartSeconds) { continue }

        Write-WatchdogLog (""App (pid {0}) has not responded for {1}s; killing it."" -f $app.Id, $HangRestartSeconds)
        try { $app.Kill() } catch { Write-WatchdogLog ""Failed to kill the app: $_"" }

        # 강제 종료로 끝난 것을 확인했을 때만 표시함. 실패한 뒤 앱이 살아나 정상 종료(0)하면 다시 실행하지 않아야 하기 때문.
        if ($app.WaitForExit(15000)) {
            $killedForHang = $true
            break
        }

        # 강제 종료가 아직 끝나지 않았으면 판정 시간 뒤 다시 시도함.
        $hungSince = Get-Date
    }

    $exitCode = $app.ExitCode
    # [int]는 반올림하므로 59.6초가 60이 되어 '켜자마자 끝남'에서 빠지지 않게 버림.
    $ranSeconds = [int][Math]::Floor(((Get-Date) - $startedAt).TotalSeconds)
    $app.Dispose()

    if (-not $killedForHang -and $exitCode -eq 0) {
        Write-WatchdogLog (""App exited normally (exit code 0, ran {0}s); not restarting. Watchdog exits."" -f $ranSeconds)
        exit 0
    }

    if ($killedForHang) {
        $reason = ""not responding for ${HangRestartSeconds}s""
    } else {
        $reason = 'exit code {0} (0x{0:X8})' -f $exitCode
    }

    if (Test-ShuttingDown) {
        Write-WatchdogLog ""App ended ($reason) while Windows is shutting down; not restarting. Watchdog exits.""
        exit 0
    }

    # 켜자마자 계속 끝나면(설정 오류, 장치 없음 등) 크래시 덤프가 쌓여 디스크가 차지 않도록 간격을 두 배씩 늘림.
    if ($ranSeconds -lt $QuickExitSeconds) { $quickExitCount++ } else { $quickExitCount = 0 }
    $delay = Get-RestartDelay $quickExitCount
    Write-WatchdogLog (""App ended ({0}, ran {1}s); restarting in {2}s."" -f $reason, $ranSeconds, $delay)

    while ($true) {
        Start-Sleep -Seconds $delay

        if (Test-ShuttingDown) {
            Write-WatchdogLog 'Windows is shutting down; not restarting. Watchdog exits.'
            exit 0
        }

        $env:HULIACDEV_AUTORESTART_REASON = $reason
        try {
            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = $exePath
            $startInfo.Arguments = $arguments
            $startInfo.WorkingDirectory = Split-Path -Parent $exePath
            $startInfo.UseShellExecute = $false
            $app = [System.Diagnostics.Process]::Start($startInfo)
            $null = $app.Handle
            $startedAt = Get-Date
            Write-WatchdogLog (""Restarted the app (new pid {0})."" -f $app.Id)
            break
        } catch {
            $quickExitCount++
            $delay = Get-RestartDelay $quickExitCount
            Write-WatchdogLog ""Failed to restart the app; retrying in ${delay}s: $_""
        }
    }
}
";

        private const string ScriptFileName = "AutoRestartWatchdog.ps1";
        private const string LogFileName = "AutoRestart.log";

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // 한 프로세스에서 감시를 두 번 띄우면 두 감시가 각자 앱을 다시 실행해 앱이 두 개 뜨므로 막음
        // (RootLifetimeScope를 상속한 스코프가 둘 이상 만들어지는 구성 대비).
        private static bool _hasLaunched;

        private readonly string _logDirectory;
#endif

        private readonly AppSettingsProvider _settingsProvider;
        private readonly ILogger<AutoRestartWatchdog> _logger;

        private CancellationTokenSource _cts;

        /// <summary>
        /// 감시 로그를 쓸 폴더(앱 파일 로그와 같은 폴더)와 설정 제공자·로거를 받아 구성함.
        /// </summary>
        public AutoRestartWatchdog(string logDirectory, AppSettingsProvider settingsProvider, ILogger<AutoRestartWatchdog> logger)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _logDirectory = logDirectory;
#endif
            _settingsProvider = settingsProvider;
            _logger = logger;
        }

        /// <summary>
        /// VContainer 컨테이너 빌드 완료 시 자동 호출됨. 감시가 다시 실행한 앱이면 재시작 이유를 남기고,
        /// 앱 종료와 연결된 토큰으로 설정을 읽어 감시를 띄움.
        /// </summary>
        void IStartable.Start()
        {
            LogRestartReason();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(Application.exitCancellationToken);
            // persistentDataPath는 메인 스레드에서만 읽을 수 있으므로 여기서 읽어 넘김.
            StartWatchdogAsync(Application.persistentDataPath, _cts.Token).Forget();
        }

        /// <summary>
        /// 감시 스크립트가 앱을 다시 실행했으면(환경 변수가 있으면) 그 이유를 경고로 남김.
        /// 이전 실행의 로그에는 크래시·강제 종료 흔적이 남지 않으므로, 다시 켜진 앱의 로그에서 원인을 알 수 있게 함.
        /// </summary>
        private void LogRestartReason()
        {
            string reason = Environment.GetEnvironmentVariable(RestartReasonVariable);
            if (string.IsNullOrEmpty(reason) || _logger == null) return;

            _logger.ZLogWarning($"[AutoRestartWatchdog] Restarted by the watchdog after the previous run ended abnormally ({reason}).");
        }

        /// <summary>
        /// Settings.json의 autoRestart를 읽어, 켜져 있으면 Windows 스탠드얼론 빌드에서만 감시 스크립트를 띄움.
        /// Settings.json을 읽지 못하면 AppSettingsProvider의 대체 설정에 autoRestart가 없으므로 띄우지 않음.
        /// </summary>
        private async UniTaskVoid StartWatchdogAsync(string persistentDataPath, CancellationToken cancellationToken)
        {
            Settings settings;
            try
            {
                settings = await _settingsProvider.GetAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 컨테이너 파기나 앱 종료로 정상 취소됨
                return;
            }

            AutoRestartSetting setting = settings?.autoRestart;
            if (setting == null || !setting.enabled) return;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_hasLaunched) return;

            if (TryGetRunningWatchdog(out int runningWatchdogId))
            {
                _hasLaunched = true;
                if (_logger != null) _logger.ZLogInformation($"[AutoRestartWatchdog] Already watched by the watchdog that restarted this app (pid {runningWatchdogId}).");
                return;
            }

            int hangRestartSeconds = AutoRestartPolicy.ResolveHangRestartSeconds(setting.hangRestartSeconds);
            int restartDelaySeconds = AutoRestartPolicy.ResolveRestartDelaySeconds(setting.restartDelaySeconds);
            // persistentDataPath는 '/' 구분자를 쓰므로 로그·명령줄에 섞이지 않게 Windows 형식으로 맞춤.
            string scriptPath = Path.GetFullPath(Path.Combine(persistentDataPath, ScriptFileName));
            string logPath = Path.GetFullPath(Path.Combine(_logDirectory, LogFileName));

            int appProcessId;
            using (System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess())
            {
                appProcessId = current.Id;
            }

            string arguments = AutoRestartPolicy.BuildWatchdogArguments(scriptPath, appProcessId, logPath, hangRestartSeconds, restartDelaySeconds);

            // 실행 결과를 기다리는 사이 다른 스코프가 또 띄우지 않도록 먼저 표시함.
            _hasLaunched = true;

            try
            {
                // 실행은 짧고 감시는 앱과 별개 프로세스라 취소 토큰을 넘기지 않음. 넘기면 실행 직전에 취소될 때
                // 감시는 뜨지 않았는데 _hasLaunched만 남아, 이후 만들어지는 스코프도 감시를 띄우지 않게 됨.
                int watchdogId = await UniTask.RunOnThreadPool(() => LaunchWatchdog(scriptPath, logPath, arguments));

                if (_logger != null)
                {
                    _logger.ZLogInformation($"[AutoRestartWatchdog] Watchdog started (pid {watchdogId}). Restarts the app when it ends with a non-zero exit code or stays not responding for {hangRestartSeconds}s (0 = off), after {restartDelaySeconds}s. Log: {logPath}");
                }
            }
            catch (Exception e)
            {
                _hasLaunched = false;
                if (_logger != null) _logger.ZLogError($"[AutoRestartWatchdog] Failed to start the watchdog; the app will not be restarted automatically: {e.Message}");
            }
#else
            if (_logger != null) _logger.ZLogInformation($"[AutoRestartWatchdog] autoRestart is enabled, but the watchdog only runs in Windows standalone builds. Skipped on this platform.");
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>
        /// 이 앱을 다시 실행한 감시 스크립트가 아직 살아 있는지 확인함. 감시가 죽은 뒤 PID가 다른 프로세스에 재사용된 경우를
        /// 걸러내도록 프로세스 이름도 확인하며, 확인할 수 없으면 살아 있지 않은 것으로 봄(그러면 새 감시를 띄움).
        /// </summary>
        private static bool TryGetRunningWatchdog(out int watchdogId)
        {
            watchdogId = 0;

            if (!int.TryParse(Environment.GetEnvironmentVariable(WatchdogPidVariable), out int id)) return false;

            try
            {
                using (System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(id))
                {
                    if (process.HasExited || !string.Equals(process.ProcessName, "powershell", StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            catch (Exception)
            {
                // 이미 끝난 프로세스(ArgumentException)이거나 정보를 읽을 수 없음
                return false;
            }

            watchdogId = id;
            return true;
        }

        /// <summary>
        /// 감시 스크립트를 쓰고 숨은 PowerShell로 실행한 뒤 그 PID를 돌려줌. 파일 쓰기와 프로세스 생성이 메인 스레드를 막지 않도록
        /// 스레드 풀에서 호출함. Windows PowerShell 5.1은 BOM 없는 UTF-8 스크립트를 시스템 코드 페이지로 읽으므로 BOM을 붙임.
        /// </summary>
        private static int LaunchWatchdog(string scriptPath, string logPath, string arguments)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(scriptPath, WatchdogScript, new UTF8Encoding(true));

            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo))
            {
                return process.Id;
            }
        }
#endif

        /// <summary>
        /// 설정 로드를 취소함. 이미 띄운 감시 스크립트는 앱과 별개 프로세스라 그대로 앱을 지켜봄.
        /// </summary>
        public void Dispose()
        {
            if (_cts == null) return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }
}
