using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Wonjeong.Tools.ShutdownScheduleEditor;

/// <summary>
/// 유니티가 멈춰서 스스로 종료하지 못한 경우를 대비해, 예정 시각 +N분에 PC를 끄는
/// 백업용 Windows 작업 스케줄러 항목을 등록·제거하는 기능.
/// <para>
/// 작업 스케줄러의 트리거는 "요일 + 시각" 반복은 표현할 수 있어도 "특정 날짜는 제외"를
/// 표현할 수 없음. 그래서 트리거는 시각을 잡는 역할만 하고, 실제로 끌지 말지는 작업이
/// 실행되는 순간 ShutdownSettings.json을 다시 읽는 가드 스크립트가 판단함. 덕분에 스케줄을
/// 수정해도 특정 날짜 규칙은 다시 등록하지 않아도 그대로 반영됨.
/// </para>
/// </summary>
public static class TaskSchedulerIntegration
{
    public const string TaskName = "shutdown-backup";
    public const string GuardScriptFileName = "ShutdownBackupGuard.ps1";

    /// <summary>요일별 반복 트리거 하나(요일 + 실행 시각).</summary>
    public readonly record struct WeeklyTrigger(DayOfWeek Day, TimeSpan Time);

    /// <summary>특정 날짜 한 번만 실행되는 트리거(날짜 + 실행 시각).</summary>
    public readonly record struct OneTimeTrigger(DateTime Date, TimeSpan Time);

    /// <summary>
    /// 작업이 실행될 때마다 설정 파일을 다시 읽어, 오늘 정말 종료 예정이고 예정 시각이
    /// 지났을 때만 종료를 실행하는 가드 스크립트.
    /// </summary>
    private const string GuardScript = """
        # 이 파일은 종료 스케줄 편집기가 자동으로 생성합니다. 직접 수정하지 마세요.
        param(
            [Parameter(Mandatory = $true)][string]$SettingsPath,
            [int]$DelayMinutes = 5
        )

        $logPath = Join-Path (Split-Path -Parent $SettingsPath) 'ShutdownBackup.log'
        function Write-BackupLog([string]$message) {
            "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message" | Out-File -FilePath $logPath -Append -Encoding utf8
        }

        if (-not (Test-Path -LiteralPath $SettingsPath)) {
            Write-BackupLog "설정 파일을 찾을 수 없어 중단: $SettingsPath"
            exit 1
        }

        try {
            $config = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        } catch {
            Write-BackupLog "설정 파일을 읽지 못해 중단: $_"
            exit 1
        }

        $now = Get-Date
        $dateKey = $now.ToString('yyyy-MM-dd')
        $dayKeys = @('sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday')
        $dayKey = $dayKeys[[int]$now.DayOfWeek]

        # 특정 날짜 설정이 있으면 그 요일의 기본 스케줄보다 우선함.
        $enabled = $false
        $timeText = $null
        $override = $null
        if ($config.dateOverrides) {
            $override = $config.dateOverrides | Where-Object { $_.date -eq $dateKey } | Select-Object -First 1
        }

        if ($null -ne $override) {
            $enabled = [bool]$override.enabled
            $timeText = $override.time
            $source = "특정 날짜($dateKey)"
        } elseif ($config.$dayKey) {
            $enabled = [bool]$config.$dayKey.enabled
            $timeText = $config.$dayKey.time
            $source = "요일($dayKey)"
        } else {
            Write-BackupLog "오늘($dayKey) 스케줄이 설정 파일에 없어 건너뜀"
            exit 0
        }

        if (-not $enabled) {
            Write-BackupLog "$source 설정이 '종료 안 함'이라 건너뜀"
            exit 0
        }

        $scheduled = [datetime]::MinValue
        $formats = [string[]]@('HH:mm')
        if (-not [datetime]::TryParseExact($timeText, $formats, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$scheduled)) {
            Write-BackupLog "시간 형식이 올바르지 않아 중단: '$timeText'"
            exit 1
        }

        # 유니티가 정상 종료했다면 이 시점에 PC는 이미 꺼져 있음. 아직 켜져 있다는 것은
        # 유니티가 종료하지 못했다는 뜻이므로 대신 종료함.
        #
        # 이 검사의 목적은 "예정 시각보다 한참 이른 실행"(수동 실행, 또는 스케줄을 더 늦은
        # 시각으로 바꿨는데 옛 트리거가 남아 먼저 발동한 경우)을 걸러내는 것임. 작업 스케줄러가
        # 타이머 오차나 프로세스 생성 지연으로 예정 시각보다 1초 미만 일찍 기동하는 경우까지
        # 걸러내면 그날 백업을 통째로 놓치므로, 경계에 여유를 둠.
        $deadline = $now.Date.Add($scheduled.TimeOfDay).AddMinutes($DelayMinutes)
        if ($now -lt $deadline.AddSeconds(-30)) {
            Write-BackupLog "아직 예정 시각($($deadline.ToString('HH:mm')))이 되지 않아 건너뜀"
            exit 0
        }

        $shutdownArguments = $config.shutdownArguments
        if ([string]::IsNullOrWhiteSpace($shutdownArguments)) { $shutdownArguments = '-s -f -t 45' }

        Write-BackupLog "$source 예정 시각이 지났으나 아직 실행 중이므로 종료 실행: shutdown $shutdownArguments"
        Start-Process -FilePath 'shutdown.exe' -ArgumentList $shutdownArguments -NoNewWindow
        """;

    /// <summary>
    /// 가드 스크립트를 설정 파일 옆에 쓰고, 작업 스케줄러에 백업 작업을 등록(또는 갱신)함.
    /// </summary>
    public static (bool Success, string Message) Register(
        string settingsPath,
        IReadOnlyList<WeeklyTrigger> weekly,
        IReadOnlyList<OneTimeTrigger> oneTime,
        int delayMinutes)
    {
        if (weekly.Count == 0 && oneTime.Count == 0)
        {
            return (false, "종료 예정인 요일이나 날짜가 하나도 없어 등록할 백업 작업이 없습니다.");
        }

        string? directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            return (false, "설정 파일 경로가 올바르지 않습니다.");
        }

        string guardPath = Path.Combine(directory, GuardScriptFileName);

        try
        {
            File.WriteAllText(guardPath, GuardScript, new UTF8Encoding(true));
        }
        catch (Exception e)
        {
            return (false, $"가드 스크립트를 저장하지 못했습니다.\n{e.Message}");
        }

        string xml = BuildTaskXml(settingsPath, guardPath, weekly, oneTime, delayMinutes);
        string xmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}.xml");

        try
        {
            // schtasks는 UTF-16 XML을 기대함.
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);

            (int exitCode, string output) = RunSchtasksElevated($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");

            if (exitCode != 0)
            {
                return (false, $"작업 스케줄러 등록에 실패했습니다.\n\n{output}");
            }

            int triggerCount = weekly.Count + oneTime.Count;
            return (true,
                $"백업 작업을 등록했습니다.\n\n" +
                $"작업 이름: {TaskName}\n" +
                $"트리거: 요일 {weekly.Count}개, 특정 날짜 {oneTime.Count}개 (총 {triggerCount}개)\n" +
                $"가드 스크립트: {guardPath}\n\n" +
                $"각 예정 시각 +{delayMinutes}분에 실행되며, 그때 설정 파일을 다시 읽어\n" +
                $"오늘이 정말 종료 예정일 때만 PC를 끕니다.");
        }
        catch (Exception e)
        {
            return (false, $"작업 스케줄러 등록 중 오류가 발생했습니다.\n{e.Message}");
        }
        finally
        {
            try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { /* 임시 파일 정리 실패는 무시 */ }
        }
    }

    public static (bool Success, string Message) Unregister()
    {
        (int exitCode, string output) = RunSchtasksElevated($"/Delete /TN \"{TaskName}\" /F");

        return exitCode == 0
            ? (true, "백업 작업을 제거했습니다.")
            : (false, $"백업 작업을 제거하지 못했습니다.\n\n{output}");
    }

    /// <summary>등록 여부와, 등록되어 있다면 schtasks가 보고하는 요약을 돌려줌.</summary>
    public static (bool Registered, string Detail) QueryStatus()
    {
        (int exitCode, string output) = RunSchtasks($"/Query /TN \"{TaskName}\" /FO LIST");

        return exitCode == 0 ? (true, output.Trim()) : (false, "등록되어 있지 않습니다.");
    }

    public static bool IsRegistered()
    {
        (int exitCode, string _) = RunSchtasks($"/Query /TN \"{TaskName}\"");
        return exitCode == 0;
    }

    /// <summary>
    /// 이미 등록된 작업이 어떤 지연 시간으로 등록됐는지 알아냄. 스케줄을 수정한 뒤 다시 등록할 때
    /// 사용자가 지연 시간을 매번 다시 입력하지 않아도 되도록, 등록된 명령줄에서 값을 되읽음.
    /// </summary>
    public static bool TryGetRegisteredDelayMinutes(out int delayMinutes)
    {
        delayMinutes = 0;

        (int exitCode, string output) = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");

        if (exitCode != 0)
        {
            return false;
        }

        Match match = Regex.Match(output, @"-DelayMinutes\s+(\d+)");

        return match.Success && int.TryParse(match.Groups[1].Value, out delayMinutes);
    }

    /// <summary>
    /// 작업을 만들거나 지우는 등 쓰기 작업을 관리자 권한으로 실행함(UAC 승격).
    /// S4U 로그온 유형과 최고 권한 실행은 승격 없이는 등록되지 않으며, 등록된 작업을 임의로
    /// 지우지 못하게 하는 효과도 있음.
    /// <para>
    /// 승격에는 ShellExecute가 필요하고 ShellExecute는 출력 리디렉션을 지원하지 않으므로,
    /// 성공 여부는 종료 코드로만 판단함.
    /// </para>
    /// </summary>
    private static (int ExitCode, string Output) RunSchtasksElevated(string arguments)
    {
        ProcessStartInfo info = new()
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using Process? process = Process.Start(info);
            if (process == null) return (-1, "schtasks.exe를 실행하지 못했습니다.");

            process.WaitForExit();

            return process.ExitCode == 0
                ? (0, string.Empty)
                : (process.ExitCode, $"schtasks가 종료 코드 {process.ExitCode}로 실패했습니다.");
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: 사용자가 UAC 창에서 취소함.
            return (-1, "관리자 권한 요청이 취소되었습니다. 이 작업은 관리자 권한이 필요합니다.");
        }
        catch (Exception e)
        {
            return (-1, e.Message);
        }
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        ProcessStartInfo info = new()
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // schtasks의 한글 메시지가 깨지지 않도록 콘솔 코드 페이지에 맞춤.
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
        };

        try
        {
            using Process? process = Process.Start(info);
            if (process == null) return (-1, "schtasks.exe를 실행하지 못했습니다.");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            string combined = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n{stderr}";
            return (process.ExitCode, combined);
        }
        catch (Exception e)
        {
            return (-1, e.Message);
        }
    }

    private static string BuildTaskXml(
        string settingsPath,
        string guardPath,
        IReadOnlyList<WeeklyTrigger> weekly,
        IReadOnlyList<OneTimeTrigger> oneTime,
        int delayMinutes)
    {
        StringBuilder triggers = new();

        foreach (WeeklyTrigger trigger in weekly)
        {
            // 반복 트리거는 StartBoundary의 날짜가 아니라 DaysOfWeek로 실행일이 정해지므로,
            // 날짜는 충분히 과거의 고정값을 쓰고 시각만 의미를 가짐.
            string start = $"2020-01-01T{trigger.Time:hh\\:mm}:00";
            triggers.AppendLine($"""
                    <CalendarTrigger>
                      <StartBoundary>{start}</StartBoundary>
                      <Enabled>true</Enabled>
                      <ScheduleByWeek>
                        <DaysOfWeek><{trigger.Day}/></DaysOfWeek>
                        <WeeksInterval>1</WeeksInterval>
                      </ScheduleByWeek>
                    </CalendarTrigger>
                """);
        }

        foreach (OneTimeTrigger trigger in oneTime)
        {
            string start = $"{trigger.Date:yyyy-MM-dd}T{trigger.Time:hh\\:mm}:00";
            triggers.AppendLine($"""
                    <TimeTrigger>
                      <StartBoundary>{start}</StartBoundary>
                      <Enabled>true</Enabled>
                    </TimeTrigger>
                """);
        }

        string arguments = Escape($"-NoProfile -ExecutionPolicy Bypass -File \"{guardPath}\" -SettingsPath \"{settingsPath}\" -DelayMinutes {delayMinutes}");

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>유니티 앱이 스스로 종료하지 못했을 때를 대비한 백업 종료 작업. 종료 스케줄 편집기가 생성함.</Description>
              </RegistrationInfo>
              <Triggers>
            {triggers.ToString().TrimEnd()}
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Escape(GetCurrentUserId())}</UserId>
                  <!-- S4U: "사용자의 로그온 여부에 관계없이 실행" + "암호를 저장하지 않습니다".
                       로그인 세션에 의존하지 않으므로 자동 로그인이 풀려 있어도 백업이 동작함.
                       대신 네트워크 자원에는 접근할 수 없으나, 이 작업은 로컬에서 shutdown만
                       실행하므로 제약이 되지 않음. -->
                  <LogonType>S4U</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <!-- PC가 꺼져 있어 놓친 트리거를 다음 부팅 직후에 실행하면, 아침에 켠 키오스크를
                     그대로 다시 꺼버림. 놓친 종료는 이미 꺼져 있었다는 뜻이므로 따라잡지 않음. -->
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>powershell.exe</Command>
                  <Arguments>{arguments}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>
    /// S4U 로그온 유형은 어떤 계정으로 실행할지 명시해야 하므로 현재 사용자를 도메인 포함
    /// 형식으로 구성함.
    /// </summary>
    private static string GetCurrentUserId()
    {
        string domain = Environment.UserDomainName;
        string user = Environment.UserName;

        return string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
