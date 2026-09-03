using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wonjeong.Tools.ShutdownScheduleEditor;

/// <summary>
/// StreamingAssets의 ShutdownSettings.json(요일별 기본 스케줄 + 특정 날짜 재정의)을 편집하는 도구.
/// 이 도구가 다루지 않는 키가 파일에 들어있어도 원본 그대로 보존한 채 저장함.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly string[] DayKeys =
    {
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"
    };

    private static readonly string[] DayLabels = { "월요일", "화요일", "수요일", "목요일", "금요일", "토요일", "일요일" };

    private const string TimeFormat = "HH:mm";
    private const string DateFormat = "yyyy-MM-dd";
    /// <summary>
    /// 편집 대상 파일명. Program.cs가 exe와 같은 폴더에서 이 파일을 자동으로 찾을 때도 참조함.
    /// </summary>
    internal const string TargetFileName = "ShutdownSettings.json";

    /// <summary>45초 뒤 강제 종료. 종료 로그 전송이 끝날 시간을 확보하려고 지연을 둠.</summary>
    private const string DefaultShutdownArguments = "-s -f -t 45";

    /// <summary>등록된 작업에서 지연 시간을 읽지 못했을 때 쓰는 기본값(분).</summary>
    private const int DefaultBackupDelayMinutes = 5;

    /// <summary>월요일 기본 종료 시각.</summary>
    private const string DefaultMondayTime = "09:10";

    /// <summary>월요일을 제외한 나머지 요일의 기본 종료 시각.</summary>
    private const string DefaultTime = "17:35";

    private readonly CheckBox[] _dayEnabledCheckBoxes = new CheckBox[DayKeys.Length];
    private readonly DateTimePicker[] _dayTimePickers = new DateTimePicker[DayKeys.Length];

    // "특정 날짜" 영역만 Dock=Fill이고 나머지는 전부 Dock=Top/Bottom 고정 영역이라, 창 높이가
    // 이 고정 영역들의 실제(폰트/DPI 반영 후) 필요 높이를 못 따라가면 Fill 영역이 밀려서
    // 안 보이게 됨. OnLoad/OnDpiChanged에서 이 영역들의 실제 높이를 다시 합산해 창을 그만큼
    // 키우기 위해 참조를 들고 있음.
    private MenuStrip _menu = null!;
    private GroupBox _weekdayGroup = null!;
    private GroupBox _argumentsGroup = null!;
    private FlowLayoutPanel _saveBar = null!;
    private StatusStrip _statusBar = null!;

    private DataGridView _overridesGrid = null!;
    private DateTimePicker _overrideDatePicker = null!;
    private CheckBox _overrideEnabledCheckBox = null!;
    private DateTimePicker _overrideTimePicker = null!;
    private Button _overrideAddButton = null!;
    private Button _overrideRemoveButton = null!;
    private TextBox _argumentsTextBox = null!;
    private Button _argumentsResetButton = null!;
    private Button _saveButton = null!;
    private Button _updateBackupButton = null!;
    private Button _deleteBackupButton = null!;
    private ToolStripStatusLabel _statusLabel = null!;

    private JsonObject? _root;
    private string? _currentPath;
    private bool _isDirty;

    public MainForm(string? initialPath)
    {
        // Program.cs의 ApplicationConfiguration.Initialize()가 이미 PerMonitorV2 DPI 인식을
        // 설정함. 여기에 AutoScaleMode.Dpi/AutoScaleDimensions를 추가로 지정했더니 두 스케일링
        // 메커니즘이 충돌해 창이 절반 크기로 줄어드는 것으로 실측 확인됨(680x760 지정 → 실제
        // 345x384). 그래서 AutoScaleMode는 건드리지 않고, 버튼이 잘리는 문제는 아래 각 패널의
        // 고정 Height를 없애고 AutoSize로 바꾸는 방식으로 해결함(DPI 배율이 얼마든 실제 필요한
        // 크기만큼 패널이 스스로 커지므로 특정 배율을 가정할 필요가 없음).

        // ApplicationIcon(csproj)은 exe 파일 자체의 아이콘(탐색기)만 바꾸고, 창의 제목표시줄·
        // 작업표시줄 아이콘은 Form.Icon을 따로 지정해야 함. 같은 아이콘을 다시 임베드하지 않고
        // exe에 이미 구운 아이콘을 그대로 추출해 재사용함.
        string? exePath = Environment.ProcessPath;
        Icon? appIcon = string.IsNullOrEmpty(exePath) ? null : Icon.ExtractAssociatedIcon(exePath);
        if (appIcon != null)
        {
            Icon = appIcon;
        }

        Text = "종료 스케줄 편집기";
        Width = 680;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(620, 680);

        // WinForms는 도킹을 Controls에 추가한 순서의 "역순"으로 처리하므로(마지막에 추가한 것이
        // 가장 먼저 바깥 가장자리를 차지함), 화면 위에서부터 메뉴 → 요일 패널 → 상태바 → 나머지를
        // 채우는 날짜 패널 순으로 배치하려면 추가는 정확히 그 반대 순서여야 함.
        BuildOverridesPanel();
        BuildArgumentsPanel();
        BuildSaveBar();
        BuildStatusBar();
        BuildWeekdayPanel();
        BuildMenu();

        SetControlsEnabled(false);

        if (!string.IsNullOrEmpty(initialPath) && File.Exists(initialPath))
        {
            OpenFile(initialPath!);
        }
    }

    /// <summary>
    /// DateTimePicker는 TextBox/Label과 달리 내용 기준 AutoSize를 지원하지 않아 Width를 직접
    /// 계산해야 함. 생성 시점에는 아직 이 폼이 최종적으로 어느 모니터에 뜰지, 그래서 어떤
    /// 폰트 크기로 보정될지 확정되지 않을 수 있으므로, 폰트가 확정된 뒤인 OnLoad에서 다시
    /// 계산함. 컬럼을 AutoSize로 바꿔둔 덕분에 여기서 Width만 갱신하면 컬럼도 같이 넓어짐.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyDynamicFieldWidths();
        EnsureMinimumHeightForContent();
    }

    /// <summary>
    /// 창을 다른 DPI의 모니터로 드래그하면 OnLoad는 다시 불리지 않고 이 이벤트만 발생함.
    /// 폰트는 프레임워크가 알아서 갱신해주지만(라벨/체크박스는 AutoSize라 자동 반영됨),
    /// 1) DateTimePicker Width는 직접 계산한 값이라 여기서도 다시 계산해야 하고,
    /// 2) 창 자체 크기는 그대로 두면 요일별 스케줄 등 위쪽 영역이 커진 폰트만큼 늘어나면서
    /// Dock=Fill인 "특정 날짜" 영역이 밀려 거의 안 보이게 됨. SuggestedRectangle이 새 DPI에
    /// 맞춰 Windows가 계산해준 창 크기이므로 그대로 적용해 전체 창도 같이 키움.
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Bounds = e.SuggestedRectangle;
        ApplyDynamicFieldWidths();
        EnsureMinimumHeightForContent();
    }

    /// <summary>
    /// SuggestedRectangle은 Windows가 "이전 창 크기 × DPI 비율"로 계산한 값이라, 이 폼처럼
    /// AutoSize와 폰트 기준 폭 계산을 섞어 쓰는 레이아웃에서는 실제 필요한 높이보다 부족할 수
    /// 있음(요일별/인수 영역이 커진 만큼 딱 비례해서 늘어난다는 보장이 없음). 그래서 나머지
    /// 고정 영역들의 실측 높이를 직접 더해 부족하면 창을 그만큼 키움 — "특정 날짜" 영역이
    /// 밀려서 안 보이는 사고를 근본적으로 막기 위함.
    /// </summary>
    private void EnsureMinimumHeightForContent()
    {
        PerformLayout();

        int fixedHeight = _menu.Height + _weekdayGroup.Height + _argumentsGroup.Height +
                          _saveBar.Height + _statusBar.Height;
        int minOverridesHeight = Font.Height * 12;
        int desired = fixedHeight + minOverridesHeight;

        if (Height < desired)
        {
            Height = desired;
        }
    }

    private void ApplyDynamicFieldWidths()
    {
        foreach (DateTimePicker picker in _dayTimePickers)
        {
            picker.Width = MeasureFieldWidth(picker, "00:00");
        }

        _overrideDatePicker.Width = MeasureFieldWidth(_overrideDatePicker, "0000-00-00");
        _overrideTimePicker.Width = MeasureFieldWidth(_overrideTimePicker, "00:00");
    }

    /// <summary>
    /// 표본 텍스트를 현재(모니터별로 보정된) 폰트로 측정해 필요한 폭을 구함. 스핀 버튼 등
    /// 네이티브 크롬 여유분도 고정 픽셀이 아니라 폰트 크기에 비례하게 둬, 고DPI 모니터에서
    /// 크롬이 커지는 만큼 같이 커지게 함.
    /// </summary>
    private static int MeasureFieldWidth(Control control, string sampleText)
    {
        Size textSize = TextRenderer.MeasureText(sampleText, control.Font);
        return textSize.Width + control.Font.Height * 2;
    }

    private void BuildMenu()
    {
        MenuStrip menu = new();

        ToolStripMenuItem fileMenu = new("파일(&F)");
        ToolStripMenuItem openProjectItem = new("프로젝트에서 열기(&P)...", null, (_, _) => OnOpenFromProjectClicked()) { ShortcutKeys = Keys.Control | Keys.P };
        ToolStripMenuItem openItem = new($"{TargetFileName} 파일 직접 열기(&O)...", null, (_, _) => OnOpenClicked()) { ShortcutKeys = Keys.Control | Keys.O };
        ToolStripMenuItem saveItem = new("저장(&S)", null, (_, _) => OnSaveClicked()) { ShortcutKeys = Keys.Control | Keys.S };
        ToolStripMenuItem saveAsItem = new("다른 이름으로 저장(&A)...", null, (_, _) => OnSaveAsClicked());
        ToolStripMenuItem exitItem = new("종료(&X)", null, (_, _) => Close());

        fileMenu.DropDownItems.Add(openProjectItem);
        fileMenu.DropDownItems.Add(openItem);
        fileMenu.DropDownItems.Add(saveItem);
        fileMenu.DropDownItems.Add(saveAsItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(exitItem);

        ToolStripMenuItem toolMenu = new("도구(&T)");
        toolMenu.DropDownItems.Add(new ToolStripMenuItem("작업 스케줄러 백업(&B)...", null, (_, _) => OnTaskSchedulerClicked()));
        toolMenu.DropDownItems.Add(new ToolStripMenuItem("작업 스케줄러 백업 삭제(&D)...", null, (_, _) => OnDeleteTaskSchedulerClicked()));

        menu.Items.Add(fileMenu);
        menu.Items.Add(toolMenu);

        MainMenuStrip = menu;
        _menu = menu;
        Controls.Add(menu);
    }

    private void BuildWeekdayPanel()
    {
        // 고정 Height 대신 AutoSize를 써서, 실제 렌더링된 자식 컨트롤 크기(폰트/DPI에 따라
        // 달라짐)에 맞춰 그룹박스 스스로 커지게 함. 픽셀 값을 하드코딩하면 화면 배율이나
        // 폰트 설정에 따라 버튼/행이 잘리는 문제가 재발하기 쉬움.
        GroupBox group = new()
        {
            Text = "요일별 기본 스케줄",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 8, 12, 8)
        };

        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = DayKeys.Length
        };
        // 세 컬럼 모두 AutoSize로 둠. Absolute 픽셀 값을 쓰면 PerMonitorV2가 모니터별로
        // 폰트를 정확히 키워줘도(라벨/체크박스 자체는 AutoSize라 문제 없음) 컬럼 폭은 고정된
        // 그대로라 높은 DPI 모니터에서 "월요일" 같은 라벨이 줄바꿈되거나 잘리는 문제가 있었음.
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        for (int i = 0; i < DayKeys.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label label = new()
            {
                Text = DayLabels[i],
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left,
                AutoSize = true
            };

            CheckBox enabledCheckBox = new()
            {
                Text = "종료함",
                Anchor = AnchorStyles.Left,
                AutoSize = true
            };
            enabledCheckBox.CheckedChanged += (_, _) => MarkDirty();

            DateTimePicker timePicker = new()
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = TimeFormat,
                ShowUpDown = true,
                Anchor = AnchorStyles.Left,
                Width = 90
            };
            timePicker.ValueChanged += (_, _) => MarkDirty();

            _dayEnabledCheckBoxes[i] = enabledCheckBox;
            _dayTimePickers[i] = timePicker;

            table.Controls.Add(label, 0, i);
            table.Controls.Add(enabledCheckBox, 1, i);
            table.Controls.Add(timePicker, 2, i);
        }

        group.Controls.Add(table);
        _weekdayGroup = group;
        Controls.Add(group);
    }

    private void BuildOverridesPanel()
    {
        GroupBox group = new()
        {
            Text = "특정 날짜 (등록된 날짜는 그 요일의 기본 스케줄 대신 이 설정을 사용)",
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8)
        };

        _overridesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            // 잘못된 형식이 입력될 여지를 없애기 위해 직접 편집은 막고, 위쪽 입력 줄로만 등록함.
            ReadOnly = true
        };
        _overridesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "date",
            HeaderText = "날짜",
            FillWeight = 40
        });
        _overridesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "enabled",
            HeaderText = "종료함",
            FillWeight = 25
        });
        _overridesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "time",
            HeaderText = "시간",
            FillWeight = 35
        });

        FlowLayoutPanel inputPanel = new()
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false
        };

        _overrideDatePicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = DateFormat,
            Width = 120,
            Margin = new Padding(0, 6, 8, 0)
        };

        _overrideEnabledCheckBox = new CheckBox
        {
            Text = "종료함",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 9, 8, 0)
        };

        _overrideTimePicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = TimeFormat,
            ShowUpDown = true,
            Width = 90,
            Margin = new Padding(0, 6, 8, 0)
        };
        _overrideTimePicker.Value = ParseTimeOrDefault(DefaultTime, DefaultTime);

        _overrideAddButton = new Button { Text = "날짜 추가", AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
        _overrideAddButton.Click += (_, _) => AddOrUpdateOverride();

        // 종료하지 않는 날은 시간을 정할 필요가 없으므로 입력을 비활성화해 혼동을 줄임.
        _overrideEnabledCheckBox.CheckedChanged += (_, _) => _overrideTimePicker.Enabled = _overrideEnabledCheckBox.Checked;

        inputPanel.Controls.Add(new Label { Text = "날짜", AutoSize = true, Margin = new Padding(0, 10, 6, 0) });
        inputPanel.Controls.Add(_overrideDatePicker);
        inputPanel.Controls.Add(_overrideEnabledCheckBox);
        inputPanel.Controls.Add(new Label { Text = "시간", AutoSize = true, Margin = new Padding(0, 10, 6, 0) });
        inputPanel.Controls.Add(_overrideTimePicker);
        inputPanel.Controls.Add(_overrideAddButton);

        FlowLayoutPanel buttonPanel = new()
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false
        };
        _overrideRemoveButton = new Button { Text = "선택한 날짜 삭제", AutoSize = true };
        _overrideRemoveButton.Click += (_, _) =>
        {
            // 선택된 행이 없으면 지운 것이 없으므로 변경 표시(*)를 붙이지 않음.
            if (_overridesGrid.SelectedRows.Count == 0) return;

            foreach (DataGridViewRow row in _overridesGrid.SelectedRows.Cast<DataGridViewRow>().ToList())
            {
                _overridesGrid.Rows.Remove(row);
            }
            MarkDirty();
        };
        buttonPanel.Controls.Add(_overrideRemoveButton);

        // 그룹 박스 안에서도 바깥과 같은 역순 도킹 규칙이 적용되므로, 위에서부터
        // 입력 줄 → 목록 → 삭제 버튼 순으로 보이게 하려면 반대 순서로 추가해야 함.
        group.Controls.Add(_overridesGrid);
        group.Controls.Add(buttonPanel);
        group.Controls.Add(inputPanel);
        Controls.Add(group);
    }

    /// <summary>
    /// 입력 줄의 값을 목록에 등록함. 같은 날짜가 이미 있으면 새 행을 만들지 않고 덮어씀
    /// (한 날짜에 서로 다른 설정이 두 개 생기면 어느 쪽이 적용될지 알 수 없기 때문).
    /// </summary>
    private void AddOrUpdateOverride()
    {
        string date = _overrideDatePicker.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
        bool enabled = _overrideEnabledCheckBox.Checked;
        string time = _overrideTimePicker.Value.ToString(TimeFormat, CultureInfo.InvariantCulture);

        foreach (DataGridViewRow row in _overridesGrid.Rows)
        {
            if (!string.Equals(Convert.ToString(row.Cells["date"].Value), date, StringComparison.Ordinal)) continue;

            DialogResult overwrite = MessageBox.Show(
                this,
                $"{date}은(는) 이미 등록되어 있습니다. 덮어쓸까요?",
                "중복 날짜",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (overwrite != DialogResult.Yes) return;

            row.Cells["enabled"].Value = enabled;
            row.Cells["time"].Value = enabled ? time : string.Empty;
            MarkDirty();
            return;
        }

        _overridesGrid.Rows.Add(date, enabled, enabled ? time : string.Empty);
        _overridesGrid.Sort(_overridesGrid.Columns["date"]!, System.ComponentModel.ListSortDirection.Ascending);
        MarkDirty();
    }

    /// <summary>
    /// 종료 시각에 실행할 shutdown 명령의 인수를 편집하는 영역.
    /// </summary>
    private void BuildArgumentsPanel()
    {
        GroupBox group = new()
        {
            Text = "종료 명령 인수",
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 8, 12, 12)
        };

        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label label = new()
        {
            Text = "shutdown",
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0)
        };

        _argumentsTextBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Text = DefaultShutdownArguments
        };
        _argumentsTextBox.TextChanged += (_, _) => MarkDirty();

        _argumentsResetButton = new Button
        {
            Text = "기본값",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _argumentsResetButton.Click += (_, _) => _argumentsTextBox.Text = DefaultShutdownArguments;

        Label hint = new()
        {
            Text = "-s 종료 · -r 재부팅 · -f 실행 중인 앱 강제 종료 · -t 지연 시간(초)",
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
            AutoSize = true
        };

        table.Controls.Add(label, 0, 0);
        table.Controls.Add(_argumentsTextBox, 1, 0);
        table.Controls.Add(_argumentsResetButton, 2, 0);
        table.Controls.Add(hint, 1, 1);

        group.Controls.Add(table);
        _argumentsGroup = group;
        Controls.Add(group);
    }

    /// <summary>
    /// 메뉴를 열지 않고도 바로 저장하거나 작업 스케줄러를 갱신할 수 있도록 창 하단에 버튼을 둠.
    /// </summary>
    private void BuildSaveBar()
    {
        FlowLayoutPanel saveBar = new()
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 8, 12, 8)
        };

        _saveButton = new Button
        {
            Text = "저장 (Ctrl+S)",
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4)
        };
        _saveButton.Click += (_, _) => OnSaveClicked();

        _updateBackupButton = new Button
        {
            Text = "작업 스케줄러 갱신",
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 0)
        };
        _updateBackupButton.Click += (_, _) => OnUpdateTaskSchedulerClicked();

        _deleteBackupButton = new Button
        {
            Text = "작업 스케줄러 삭제",
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 0)
        };
        _deleteBackupButton.Click += (_, _) => OnDeleteTaskSchedulerClicked();

        saveBar.Controls.Add(_saveButton);
        saveBar.Controls.Add(_updateBackupButton);
        saveBar.Controls.Add(_deleteBackupButton);
        _saveBar = saveBar;
        Controls.Add(saveBar);
    }

    private void BuildStatusBar()
    {
        StatusStrip status = new();
        _statusLabel = new ToolStripStatusLabel("파일을 열어주세요.");
        status.Items.Add(_statusLabel);
        _statusBar = status;
        Controls.Add(status);
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (CheckBox checkBox in _dayEnabledCheckBoxes) checkBox.Enabled = enabled;
        foreach (DateTimePicker picker in _dayTimePickers) picker.Enabled = enabled;

        _overridesGrid.Enabled = enabled;
        _overrideDatePicker.Enabled = enabled;
        _overrideEnabledCheckBox.Enabled = enabled;
        _overrideTimePicker.Enabled = enabled && _overrideEnabledCheckBox.Checked;
        _overrideAddButton.Enabled = enabled;
        _overrideRemoveButton.Enabled = enabled;
        _argumentsTextBox.Enabled = enabled;
        _argumentsResetButton.Enabled = enabled;
        _saveButton.Enabled = enabled;
        _updateBackupButton.Enabled = enabled;
        _deleteBackupButton.Enabled = enabled;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string fileName = _currentPath == null ? "(파일 없음)" : Path.GetFileName(_currentPath);
        Text = $"종료 스케줄 편집기 - {fileName}{(_isDirty ? " *" : string.Empty)}";
    }

    private bool ConfirmDiscardUnsavedChanges()
    {
        if (!_isDirty) return true;

        DialogResult result = MessageBox.Show(
            "저장하지 않은 변경 사항이 있습니다. 계속하시겠습니까?",
            "확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        return result == DialogResult.Yes;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmDiscardUnsavedChanges())
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>
    /// 백업 작업이 등록된 상태에서 스케줄을 저장하면, 작업 스케줄러도 확인 없이 곧바로 함께 갱신함.
    /// <para>
    /// 특정 날짜를 휴무로 지정하거나 요일을 끄는 변경은 가드 스크립트가 실행 시점에 파일을 다시
    /// 읽으므로 재등록 없이도 반영되지만, 시각을 바꾸거나 새로 종료를 켜는 변경은 트리거 자체가
    /// 옛날 값이거나 아예 없어서 백업이 조용히 동작하지 않게 됨. 예전엔 저장 시점에 갱신할지
    /// 물어봤는데, 이 확인창 자체를 깜빡하고 "아니오"에 가깝게 지나치기 쉬워 결국 같은 문제가
    /// 재발했음. 저장 = 백업도 최신 상태로 확정, 이 한 가지 규칙만 기억하면 되도록 확인 없이
    /// 바로 갱신하는 쪽으로 바꿈(UAC 승격 창이 저장마다 뜨는 대가는 있음).
    /// </para>
    /// </summary>
    private void SyncBackupIfRegistered()
    {
        if (_currentPath == null) return;
        if (!TaskSchedulerIntegration.IsRegistered()) return;

        if (!TaskSchedulerIntegration.TryGetRegisteredDelayMinutes(out int delayMinutes))
        {
            delayMinutes = DefaultBackupDelayMinutes;
        }

        (IReadOnlyList<TaskSchedulerIntegration.WeeklyTrigger> weekly,
         IReadOnlyList<TaskSchedulerIntegration.OneTimeTrigger> oneTime,
         string? error) = BuildBackupTriggers(delayMinutes);

        if (error != null)
        {
            MessageBox.Show(this, error, "갱신할 수 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        (bool success, string message) = TaskSchedulerIntegration.Register(_currentPath, weekly, oneTime, delayMinutes);

        if (success)
        {
            _statusLabel.Text = $"저장됨 + 백업 작업 갱신됨(+{delayMinutes}분): {_currentPath}";
        }
        else
        {
            MessageBox.Show(this, message, "갱신 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnTaskSchedulerClicked()
    {
        if (_currentPath == null)
        {
            MessageBox.Show(this, "먼저 설정 파일을 열어주세요.", "파일 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 작업은 파일 경로를 참조하므로, 저장되지 않은 변경은 백업에 반영되지 않음.
        if (_isDirty)
        {
            DialogResult save = MessageBox.Show(
                this,
                "저장하지 않은 변경이 있습니다. 백업 작업은 저장된 파일을 기준으로 동작하므로 먼저 저장해야 합니다.\n지금 저장할까요?",
                "저장 필요",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (save != DialogResult.Yes) return;

            TrySave(_currentPath, syncBackup: false);
            if (_isDirty) return;
        }

        using TaskSchedulerForm dialog = new(_currentPath, BuildBackupTriggers);
        dialog.ShowDialog(this);
    }

    /// <summary>
    /// 창 하단의 [작업 스케줄러 갱신] 버튼 클릭 시, 현재 스케줄을 작업 스케줄러에 즉시 갱신함.
    /// 저장되지 않은 변경이 있으면 먼저 저장하고, 아직 등록되지 않은 작업이면 등록 대화 상자를 열어줌.
    /// </summary>
    private void OnUpdateTaskSchedulerClicked()
    {
        if (_currentPath == null)
        {
            MessageBox.Show(this, "먼저 설정 파일을 열어주세요.", "파일 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_isDirty)
        {
            DialogResult save = MessageBox.Show(
                this,
                "저장하지 않은 변경이 있습니다. 백업 작업은 저장된 파일을 기준으로 동작하므로 먼저 저장해야 합니다.\n지금 저장하고 갱신할까요?",
                "저장 필요",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (save != DialogResult.Yes) return;

            TrySave(_currentPath, syncBackup: false);
            if (_isDirty) return;
        }

        if (!TaskSchedulerIntegration.IsRegistered())
        {
            DialogResult openDialog = MessageBox.Show(
                this,
                "작업 스케줄러에 백업 작업이 아직 등록되어 있지 않습니다.\n등록 대화 상자를 열어 새로 등록하시겠습니까?",
                "작업 미등록",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (openDialog == DialogResult.Yes)
            {
                using TaskSchedulerForm dialog = new(_currentPath, BuildBackupTriggers);
                dialog.ShowDialog(this);
            }
            return;
        }

        if (!TaskSchedulerIntegration.TryGetRegisteredDelayMinutes(out int delayMinutes))
        {
            delayMinutes = DefaultBackupDelayMinutes;
        }

        (IReadOnlyList<TaskSchedulerIntegration.WeeklyTrigger> weekly,
         IReadOnlyList<TaskSchedulerIntegration.OneTimeTrigger> oneTime,
         string? error) = BuildBackupTriggers(delayMinutes);

        if (error != null)
        {
            MessageBox.Show(this, error, "갱신할 수 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        (bool success, string message) = TaskSchedulerIntegration.Register(_currentPath, weekly, oneTime, delayMinutes);

        if (success)
        {
            _statusLabel.Text = $"백업 작업 갱신됨(+{delayMinutes}분): {_currentPath}";
            MessageBox.Show(this, message, "갱신 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, message, "갱신 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 창 하단의 [작업 스케줄러 삭제] 버튼 또는 메뉴 클릭 시, 등록된 백업 작업을 작업 스케줄러에서 제거함.
    /// </summary>
    private void OnDeleteTaskSchedulerClicked()
    {
        if (!TaskSchedulerIntegration.IsRegistered())
        {
            MessageBox.Show(this, "작업 스케줄러에 등록된 백업 작업이 없습니다.", "작업 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            "백업 작업을 작업 스케줄러에서 삭제할까요?\n\n삭제하면 유니티 앱이 멈췄을 때 PC가 자동으로 꺼지지 않습니다.",
            "작업 삭제 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        (bool success, string message) = TaskSchedulerIntegration.Unregister();

        if (success)
        {
            _statusLabel.Text = "백업 작업이 작업 스케줄러에서 삭제되었습니다.";
            MessageBox.Show(this, message, "삭제 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, message, "삭제 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 현재 편집 중인 스케줄을 작업 스케줄러 트리거로 변환함.
    /// 요일 트리거는 활성화된 요일에서, 일회성 트리거는 "종료함"으로 등록된 특정 날짜에서 만듦
    /// (그 요일이 꺼져 있어도 그날만 종료해야 하는 경우를 트리거가 없어 놓치지 않도록).
    /// </summary>
    private (IReadOnlyList<TaskSchedulerIntegration.WeeklyTrigger> Weekly,
             IReadOnlyList<TaskSchedulerIntegration.OneTimeTrigger> OneTime,
             string? Error) BuildBackupTriggers(int delayMinutes)
    {
        List<TaskSchedulerIntegration.WeeklyTrigger> weekly = new();
        List<TaskSchedulerIntegration.OneTimeTrigger> oneTime = new();

        DayOfWeek[] daysInOrder =
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        for (int i = 0; i < DayKeys.Length; i++)
        {
            if (!_dayEnabledCheckBoxes[i].Checked) continue;

            TimeSpan scheduled = _dayTimePickers[i].Value.TimeOfDay;
            TimeSpan fireAt = scheduled.Add(TimeSpan.FromMinutes(delayMinutes));

            // 자정을 넘기면 트리거가 다음 날로 밀려 가드가 엉뚱한 요일의 스케줄을 보게 되므로 막음.
            if (fireAt >= TimeSpan.FromDays(1))
            {
                return (weekly, oneTime,
                    $"{DayLabels[i]} {scheduled:hh\\:mm} + {delayMinutes}분이 자정을 넘깁니다.\n" +
                    "종료 시각을 앞당기거나 지연 시간을 줄여주세요.");
            }

            weekly.Add(new TaskSchedulerIntegration.WeeklyTrigger(daysInOrder[i], fireAt));
        }

        foreach (DataGridViewRow row in _overridesGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells["enabled"].Value is not true) continue;

            string dateText = Convert.ToString(row.Cells["date"].Value) ?? string.Empty;
            string timeText = Convert.ToString(row.Cells["time"].Value) ?? string.Empty;

            if (!DateTime.TryParseExact(dateText, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)) continue;
            if (!DateTime.TryParseExact(timeText, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)) continue;

            // 이미 지난 날짜는 트리거를 만들어도 실행되지 않으므로 건너뜀.
            if (date.Date < DateTime.Today) continue;

            TimeSpan fireAt = time.TimeOfDay.Add(TimeSpan.FromMinutes(delayMinutes));
            if (fireAt >= TimeSpan.FromDays(1))
            {
                return (weekly, oneTime,
                    $"{dateText} {timeText} + {delayMinutes}분이 자정을 넘깁니다.\n" +
                    "종료 시각을 앞당기거나 지연 시간을 줄여주세요.");
            }

            oneTime.Add(new TaskSchedulerIntegration.OneTimeTrigger(date, fireAt));
        }

        return (weekly, oneTime, null);
    }

    private void OnOpenFromProjectClicked()
    {
        if (!ConfirmDiscardUnsavedChanges()) return;

        using FolderBrowserDialog dialog = new()
        {
            Description = "Unity 프로젝트 루트 폴더(Assets가 있는 폴더) 또는 StreamingAssets 폴더를 선택하세요.",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string settingsPath = ResolveShutdownSettingsPath(dialog.SelectedPath);

        if (!File.Exists(settingsPath))
        {
            DialogResult create = MessageBox.Show(
                this,
                $"{TargetFileName}이 없습니다. 새로 만들까요?\n{settingsPath}",
                "파일 없음",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (create != DialogResult.Yes) return;

            try
            {
                CreateDefaultSettingsFile(settingsPath);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, $"파일 생성 중 오류가 발생했습니다.\n{e.Message}", "생성 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        OpenFile(settingsPath);
    }

    /// <summary>
    /// 선택한 폴더가 StreamingAssets 폴더 자신이면 그대로, Unity 프로젝트 루트(Assets 폴더 포함)면
    /// Assets/StreamingAssets를, 그 외에는 선택 폴더 바로 아래 StreamingAssets를 파일 위치로 간주함.
    /// </summary>
    private static string ResolveShutdownSettingsPath(string selectedFolder)
    {
        string trimmed = selectedFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string streamingAssetsDir;
        if (Path.GetFileName(trimmed).Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase))
        {
            streamingAssetsDir = trimmed;
        }
        else
        {
            string assetsDir = Path.Combine(trimmed, "Assets");
            streamingAssetsDir = Directory.Exists(assetsDir)
                ? Path.Combine(assetsDir, "StreamingAssets")
                : Path.Combine(trimmed, "StreamingAssets");
        }

        return Path.Combine(streamingAssetsDir, TargetFileName);
    }

    /// <summary>
    /// 파일이 아직 없는 프로젝트를 위해 기본값(모든 요일 비활성, 월 09:10·그 외 17:35, 특정 날짜 없음)을 담은
    /// 파일을 새로 만듦.
    /// </summary>
    internal static void CreateDefaultSettingsFile(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        JsonObject root = new();
        for (int i = 0; i < DayKeys.Length; i++)
        {
            root[DayKeys[i]] = new JsonObject { ["enabled"] = false, ["time"] = GetDefaultTimeFor(i) };
        }
        root["dateOverrides"] = new JsonArray();
        root["shutdownArguments"] = DefaultShutdownArguments;

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private void OnOpenClicked()
    {
        if (!ConfirmDiscardUnsavedChanges()) return;

        using OpenFileDialog dialog = new()
        {
            Filter = $"{TargetFileName}|{TargetFileName}|JSON 파일|*.json|모든 파일|*.*",
            Title = $"{TargetFileName} 열기"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            OpenFile(dialog.FileName);
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            JsonNode? parsed = string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
            _root = parsed as JsonObject ?? new JsonObject();
            _currentPath = path;
            _isDirty = false;

            LoadShutdownSettingIntoUi();
            SetControlsEnabled(true);
            UpdateTitle();
            _statusLabel.Text = $"열림: {path}";
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"파일을 여는 중 오류가 발생했습니다.\n{e.Message}", "열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadShutdownSettingIntoUi()
    {
        JsonObject shutdown = _root!;

        for (int i = 0; i < DayKeys.Length; i++)
        {
            JsonObject day = shutdown[DayKeys[i]] as JsonObject ?? new JsonObject();
            bool enabled = (bool?)day["enabled"] ?? false;
            string time = (string?)day["time"] ?? GetDefaultTimeFor(i);

            _dayEnabledCheckBoxes[i].Checked = enabled;
            _dayTimePickers[i].Value = ParseTimeOrDefault(time, GetDefaultTimeFor(i));
        }

        string arguments = (string?)shutdown["shutdownArguments"] ?? string.Empty;
        _argumentsTextBox.Text = string.IsNullOrWhiteSpace(arguments) ? DefaultShutdownArguments : arguments;

        _overridesGrid.Rows.Clear();
        if (shutdown["dateOverrides"] is JsonArray overrides)
        {
            foreach (JsonNode? node in overrides)
            {
                if (node is not JsonObject obj) continue;

                string date = (string?)obj["date"] ?? string.Empty;
                bool enabled = (bool?)obj["enabled"] ?? false;
                string time = (string?)obj["time"] ?? string.Empty;
                _overridesGrid.Rows.Add(date, enabled, time);
            }
        }

        _isDirty = false;
    }

    /// <summary>
    /// 요일별 기본 종료 시각을 반환함. DayKeys의 첫 항목이 월요일임.
    /// </summary>
    private static string GetDefaultTimeFor(int dayIndex)
    {
        return dayIndex == 0 ? DefaultMondayTime : DefaultTime;
    }

    private static DateTime ParseTimeOrDefault(string time, string fallback)
    {
        if (DateTime.TryParseExact(time, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
        {
            return DateTime.Today.Add(result.TimeOfDay);
        }

        // 폴백 값은 코드 상수라 항상 파싱에 성공함.
        DateTime.TryParseExact(fallback, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fallbackResult);
        return DateTime.Today.Add(fallbackResult.TimeOfDay);
    }

    private void OnSaveClicked()
    {
        if (_currentPath == null)
        {
            OnSaveAsClicked();
            return;
        }
        TrySave(_currentPath);
    }

    private void OnSaveAsClicked()
    {
        using SaveFileDialog dialog = new()
        {
            Filter = $"{TargetFileName}|{TargetFileName}|JSON 파일|*.json|모든 파일|*.*",
            Title = "다른 이름으로 저장",
            FileName = _currentPath == null ? TargetFileName : Path.GetFileName(_currentPath)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            TrySave(dialog.FileName);
        }
    }

    private void TrySave(string path, bool syncBackup = true)
    {
        if (_root == null) return;

        if (!TryBuildShutdownSetting(out JsonObject? shutdownSetting, out string? validationError))
        {
            MessageBox.Show(this, validationError, "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            // 이 도구가 다루지 않는 키가 파일에 있어도 지워지지 않도록, 루트를 통째로 바꾸지 않고
            // 편집 대상 키만 덮어씀.
            foreach (KeyValuePair<string, JsonNode?> pair in shutdownSetting!)
            {
                _root[pair.Key] = pair.Value?.DeepClone();
            }

            JsonSerializerOptions options = new() { WriteIndented = true };
            File.WriteAllText(path, _root.ToJsonString(options));

            _currentPath = path;
            _isDirty = false;
            UpdateTitle();
            _statusLabel.Text = $"저장됨: {path}";

            if (syncBackup)
            {
                SyncBackupIfRegistered();
            }
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"저장 중 오류가 발생했습니다.\n{e.Message}", "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool TryBuildShutdownSetting(out JsonObject? shutdownSetting, out string? validationError)
    {
        shutdownSetting = new JsonObject();
        validationError = null;

        string arguments = _argumentsTextBox.Text.Trim();
        if (arguments.Length == 0)
        {
            validationError = "종료 명령 인수가 비어 있습니다. \"기본값\" 버튼으로 되돌리거나 직접 입력하세요.";
            shutdownSetting = null;
            return false;
        }
        shutdownSetting["shutdownArguments"] = arguments;

        for (int i = 0; i < DayKeys.Length; i++)
        {
            shutdownSetting[DayKeys[i]] = new JsonObject
            {
                ["enabled"] = _dayEnabledCheckBoxes[i].Checked,
                ["time"] = _dayTimePickers[i].Value.ToString(TimeFormat, CultureInfo.InvariantCulture)
            };
        }

        JsonArray overridesArray = new();
        foreach (DataGridViewRow row in _overridesGrid.Rows)
        {
            if (row.IsNewRow) continue;

            string date = Convert.ToString(row.Cells["date"].Value) ?? string.Empty;
            bool enabled = row.Cells["enabled"].Value is true;
            string time = Convert.ToString(row.Cells["time"].Value) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(date)) continue;

            // 목록은 직접 편집할 수 없지만, 손으로 고친 파일을 열었을 수 있으므로 저장 전에 확인함.
            if (!DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                validationError = $"날짜 형식이 올바르지 않습니다 ({DateFormat}): \"{date}\"";
                shutdownSetting = null;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(time) &&
                !DateTime.TryParseExact(time, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                validationError = $"시간 형식이 올바르지 않습니다 ({TimeFormat}): \"{time}\"";
                shutdownSetting = null;
                return false;
            }

            overridesArray.Add(new JsonObject
            {
                ["date"] = date,
                ["enabled"] = enabled,
                ["time"] = time
            });
        }

        shutdownSetting["dateOverrides"] = overridesArray;
        return true;
    }
}
