namespace Wonjeong.Tools.ShutdownScheduleEditor;

/// <summary>
/// 백업용 작업 스케줄러 항목을 등록·제거하는 대화 상자.
/// 현재 편집 중인 스케줄을 그대로 트리거로 옮기므로, 스케줄을 바꾼 뒤에는 저장하고
/// 다시 등록해야 요일·시각 변경이 반영됨(특정 날짜 규칙은 가드 스크립트가 실행 시점에
/// 다시 읽으므로 재등록 없이도 반영됨).
/// </summary>
public sealed class TaskSchedulerForm : Form
{
    private readonly string _settingsPath;
    private readonly Func<int, (IReadOnlyList<TaskSchedulerIntegration.WeeklyTrigger> Weekly,
                               IReadOnlyList<TaskSchedulerIntegration.OneTimeTrigger> OneTime,
                               string? Error)> _buildTriggers;

    private NumericUpDown _delayInput = null!;
    private Label _statusLabel = null!;

    public TaskSchedulerForm(
        string settingsPath,
        Func<int, (IReadOnlyList<TaskSchedulerIntegration.WeeklyTrigger>,
                   IReadOnlyList<TaskSchedulerIntegration.OneTimeTrigger>,
                   string?)> buildTriggers)
    {
        _settingsPath = settingsPath;
        _buildTriggers = buildTriggers;

        // MainForm과 같은 이유로 AutoScaleMode는 건드리지 않음(주석 참고). 버튼 줄이 잘리는
        // 문제는 아래에서 각 FlowLayoutPanel을 고정 Height 대신 AutoSize로 바꿔서 해결함.

        // 이 창도 독립된 최상위 Form이라 제목표시줄 아이콘을 따로 지정해야 함(MainForm 참고).
        string? exePath = Environment.ProcessPath;
        Icon? appIcon = string.IsNullOrEmpty(exePath) ? null : Icon.ExtractAssociatedIcon(exePath);
        if (appIcon != null)
        {
            Icon = appIcon;
        }

        Text = "작업 스케줄러 백업";
        Width = 620;
        Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 420);

        BuildUi();
        RefreshStatus();
    }

    /// <summary>
    /// MainForm과 같은 이유로 NumericUpDown의 Width를 폰트가 확정된 뒤 다시 계산함
    /// (자세한 설명은 MainForm.OnLoad 참고).
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _delayInput.Width = MeasureFieldWidth(_delayInput, "000");
    }

    /// <summary>
    /// MainForm과 같은 이유(다른 DPI 모니터로 드래그해도 OnLoad는 다시 안 불림)로 여기서도
    /// Width를 다시 계산하고, SuggestedRectangle로 창 전체 크기도 새 DPI에 맞게 키움.
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Bounds = e.SuggestedRectangle;
        _delayInput.Width = MeasureFieldWidth(_delayInput, "000");
    }

    private static int MeasureFieldWidth(Control control, string sampleText)
    {
        Size textSize = TextRenderer.MeasureText(sampleText, control.Font);
        return textSize.Width + control.Font.Height * 2;
    }

    private void BuildUi()
    {
        Label description = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(16, 14, 16, 6),
            Text =
                "유니티 앱이 멈춰서 스스로 PC를 끄지 못한 경우를 대비한 백업입니다.\n\n" +
                "예정 시각 + 아래 지연 시간에 작업이 실행되고, 그 시점에 ShutdownSettings.json을\n" +
                "다시 읽어 오늘이 정말 종료 예정일 때만 PC를 끕니다. 따라서 '종료 안 함'으로\n" +
                "설정한 요일이나 특정 날짜에는 백업도 실행되지 않습니다."
        };

        FlowLayoutPanel delayPanel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 0, 16, 0),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        delayPanel.Controls.Add(new Label { Text = "지연 시간: 예정 시각 +", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });

        // 이미 등록되어 있다면 그때 쓴 값을 그대로 보여줘, 다시 등록할 때 모르는 사이에
        // 기본값으로 되돌아가지 않도록 함.
        int initialDelay = TaskSchedulerIntegration.TryGetRegisteredDelayMinutes(out int registered) ? registered : 5;

        _delayInput = new NumericUpDown { Minimum = 1, Maximum = 120, Value = initialDelay, Width = 70, Margin = new Padding(0, 4, 6, 0) };
        delayPanel.Controls.Add(_delayInput);
        delayPanel.Controls.Add(new Label { Text = "분", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });

        FlowLayoutPanel buttonPanel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 8, 16, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        Button registerButton = new() { Text = "등록 / 갱신", AutoSize = true, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 0) };
        registerButton.Click += (_, _) => OnRegister();

        Button unregisterButton = new() { Text = "등록 해제", AutoSize = true, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 0) };
        unregisterButton.Click += (_, _) => OnUnregister();

        Button refreshButton = new() { Text = "상태 새로 고침", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        refreshButton.Click += (_, _) => RefreshStatus();

        buttonPanel.Controls.Add(registerButton);
        buttonPanel.Controls.Add(unregisterButton);
        buttonPanel.Controls.Add(refreshButton);

        GroupBox statusGroup = new()
        {
            Text = "현재 상태",
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 12)
        };
        _statusLabel = new Label { Dock = DockStyle.Fill, AutoSize = false };
        statusGroup.Controls.Add(_statusLabel);

        // 바깥과 같은 역순 도킹 규칙 — 위에서부터 설명 → 지연 시간 → 버튼 → 상태 순으로
        // 보이게 하려면 반대 순서로 추가해야 함.
        Controls.Add(statusGroup);
        Controls.Add(buttonPanel);
        Controls.Add(delayPanel);
        Controls.Add(description);
    }

    private void RefreshStatus()
    {
        (bool registered, string detail) = TaskSchedulerIntegration.QueryStatus();
        _statusLabel.Text = registered
            ? $"등록됨 ({TaskSchedulerIntegration.TaskName})\n\n{detail}"
            : $"등록되어 있지 않습니다.\n\n작업 이름: {TaskSchedulerIntegration.TaskName}";
    }

    private void OnRegister()
    {
        int delay = (int)_delayInput.Value;

        (IReadOnlyList<TaskSchedulerIntegration.WeeklyTrigger> weekly,
         IReadOnlyList<TaskSchedulerIntegration.OneTimeTrigger> oneTime,
         string? error) = _buildTriggers(delay);

        if (error != null)
        {
            MessageBox.Show(this, error, "등록할 수 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        (bool success, string message) = TaskSchedulerIntegration.Register(_settingsPath, weekly, oneTime, delay);

        MessageBox.Show(this, message, success ? "등록 완료" : "등록 실패",
            MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

        RefreshStatus();
    }

    private void OnUnregister()
    {
        DialogResult confirm = MessageBox.Show(
            this,
            "백업 작업을 작업 스케줄러에서 제거할까요?\n제거하면 유니티가 멈췄을 때 PC가 켜진 채로 남습니다.",
            "확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        (bool success, string message) = TaskSchedulerIntegration.Unregister();

        MessageBox.Show(this, message, success ? "제거 완료" : "제거 실패",
            MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

        RefreshStatus();
    }
}
