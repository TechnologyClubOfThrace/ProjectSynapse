using Synapse.Audio;
using Synapse.Input;

namespace Synapse.App;

internal sealed class MainForm : Form
{
    private static readonly Color WindowBackground = Color.FromArgb(7, 17, 13);
    private static readonly Color CardBackground = Color.FromArgb(15, 29, 23);
    private static readonly Color PrimaryText = Color.FromArgb(242, 239, 229);
    private static readonly Color SecondaryText = Color.FromArgb(157, 178, 162);
    private static readonly Color Accent = Color.FromArgb(224, 161, 58);
    private static readonly Color Success = Color.FromArgb(116, 199, 119);
    private static readonly Color Warning = Color.FromArgb(226, 181, 82);
    private static readonly Color Error = Color.FromArgb(228, 109, 94);
    private static readonly Color CardBorder = Color.FromArgb(48, 72, 59);
    private static readonly Color ControlBackground = Color.FromArgb(24, 42, 33);
    private static readonly string[] MusicalStageNames =
        ["Ρίζες", "Κορμός", "Κλαδιά", "Φύλλωμα", "Πλήρης ζωή"];

    private readonly AppOptions options;
    private readonly SynapseAudioEngine audioEngine = new();
    private readonly SerialTouchClient serialClient = new();
    private readonly TouchInputArmer inputArmer = new();
    private readonly TouchLevelClassifier touchClassifier;
    private readonly ParticipationAccumulator participationAccumulator = new();
    private readonly ParticipationEventStore? participationStore;
    private readonly SensorTelemetryRecorder telemetryRecorder;
    private readonly SessionController session = new();
    private readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 50 };
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private readonly Queue<string> logLines = new();

    private ToolStripButton resetButton = null!;
    private ToolStripButton fullScreenButton = null!;
    private ToolStripButton technicalPanelButton = null!;
    private ToolStripButton muteButton = null!;
    private Label phaseLabel = null!;
    private Label participationValueLabel = null!;
    private Label levelLabel = null!;
    private Label elapsedLabel = null!;
    private Label participationCounterLabel = null!;
    private Label synapseTimingLabel = null!;
    private Button participationCounterResetButton = null!;
    private AtmosphericProgressBar sessionProgress = null!;
    private TableLayoutPanel workspacePanel = null!;
    private TableLayoutPanel technicalPanel = null!;
    private Label audioStatus = null!;
    private Label sampleStatus = null!;
    private Label deviceStatus = null!;
    private ComboBox compositionCombo = null!;
    private ComboBox deviceCombo = null!;
    private Button prepareButton = null!;
    private TrackBar levelTrack = null!;
    private Label simulatorValue = null!;
    private ComboBox portCombo = null!;
    private Button serialButton = null!;
    private Label serialStatus = null!;
    private Label calibrationProfileStatus = null!;
    private Button calibrationButton = null!;
    private AtmosphericButton telemetryButton = null!;
    private Label telemetryStatus = null!;
    private TreeLayerVisualization treeLayerVisualization = null!;
    private TextBox logBox = null!;
    private ToolStripStatusLabel footerStatus = null!;
    private bool isFullScreen;
    private bool technicalPanelWasVisibleBeforeFullScreen;
    private FormBorderStyle previousBorderStyle;
    private FormWindowState previousWindowState;
    private CompositionCatalogItem? selectedComposition;
    private ValidatedAudioAssets? selectedAssets;
    private bool changingComposition;
    private bool updatingInputUi;
    private bool serialConnectionExpected;
    private bool heartbeatTimedOut;
    private bool calibrationActive;
    private bool telemetryOperationInProgress;
    private bool uiTickInProgress;
    private bool isClosing;
    private Guid? currentSynapseId;
    private readonly string? calibrationLoadWarning;
    private readonly string? participationLoadWarning;
    private ParticipationTotals participationTotals = ParticipationTotals.Empty;

    public MainForm(AppOptions options)
    {
        this.options = options;
        telemetryRecorder = new SensorTelemetryRecorder(options.RuntimeDataDirectory);
        TouchCalibrationProfile calibrationProfile;
        try
        {
            calibrationProfile = TouchCalibrationProfiles.LoadActiveOrDefault(options.CalibrationDirectory);
        }
        catch (Exception exception)
        {
            calibrationProfile = TouchCalibrationProfiles.CreateDefault();
            calibrationLoadWarning = exception.Message;
        }
        touchClassifier = new TouchLevelClassifier(calibrationProfile);
        try
        {
            participationStore = new ParticipationEventStore(options.RuntimeDataDirectory);
            var loadResult = participationStore.Load();
            participationTotals = loadResult.Totals;
            if (loadResult.IgnoredInvalidLines > 0)
            {
                participationLoadWarning =
                    $"Ignored {loadResult.IgnoredInvalidLines} invalid participation event lines.";
            }
        }
        catch (Exception exception)
        {
            participationStore = null;
            participationLoadWarning = exception.Message;
        }
        InitializeWindow();
        BuildInterface();
        if (options.PreviewPath is not null &&
            options.PreviewWidth is int previewWidth &&
            options.PreviewHeight is int previewHeight)
        {
            ClientSize = new Size(previewWidth, previewHeight);
        }

        audioEngine.Faulted += AudioEngineOnFaulted;
        serialClient.MessageReceived += SerialClientOnMessageReceived;
        serialClient.ConnectionFaulted += SerialClientOnConnectionFaulted;
        serialClient.InvalidLineReceived += SerialClientOnInvalidLineReceived;
        telemetryRecorder.Faulted += TelemetryRecorderOnFaulted;
        uiTimer.Tick += UiTimerOnTick;
        FormClosing += MainFormOnFormClosing;
        Shown += MainFormOnShown;
    }

    private void InitializeWindow()
    {
        Text = "Synapse";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1366, 768);
        MinimumSize = new Size(1100, 700);
        BackColor = WindowBackground;
        ForeColor = PrimaryText;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void BuildInterface()
    {
        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(12, 6, 12, 6),
            BackColor = CardBackground,
            Renderer = new AtmosphericToolStripRenderer(),
            ForeColor = PrimaryText
        };
        var title = new ToolStripLabel("SYNAPSE")
        {
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 18, 0)
        };
        var subtitle = new ToolStripLabel("Η ζωή του δέντρου μέσα από την αφή και τον ήχο")
        {
            ForeColor = SecondaryText
        };
        resetButton = new ToolStripButton("Επαναφορά") { Enabled = false, ForeColor = PrimaryText };
        resetButton.Alignment = ToolStripItemAlignment.Right;
        resetButton.Click += async (_, _) =>
            await ExecuteCommandsAsync(session.Reset(DateTime.UtcNow));
        fullScreenButton = new ToolStripButton("Πλήρης οθόνη")
        {
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = PrimaryText
        };
        fullScreenButton.Click += (_, _) => ToggleFullScreen();
        technicalPanelButton = new ToolStripButton("Προβολή εγκατάστασης")
        {
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = PrimaryText
        };
        technicalPanelButton.Click += (_, _) => SetTechnicalPanelVisibility(!technicalPanel.Visible);
        muteButton = new ToolStripButton("Σίγαση μουσικής")
        {
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = PrimaryText,
            Enabled = false,
            ToolTipText = "Ο ήχος σβήνει ομαλά, ενώ καταγραφή, Συνάψεις και LEDs συνεχίζουν."
        };
        muteButton.Click += (_, _) => ToggleAudioMute();
        toolStrip.Items.Add(title);
        toolStrip.Items.Add(subtitle);
        toolStrip.Items.Add(fullScreenButton);
        toolStrip.Items.Add(technicalPanelButton);
        toolStrip.Items.Add(muteButton);
        toolStrip.Items.Add(resetButton);
        Controls.Add(toolStrip);

        var statusStrip = new StatusStrip
        {
            SizingGrip = false,
            BackColor = CardBackground,
            ForeColor = SecondaryText
        };
        footerStatus = new ToolStripStatusLabel("Εκκίνηση εφαρμογής...")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusStrip.Items.Add(footerStatus);
        statusStrip.Items.Add(new ToolStripStatusLabel("Synapse v2 • Τοπική λειτουργία"));
        Controls.Add(statusStrip);

        workspacePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = WindowBackground,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 1
        };
        workspacePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        workspacePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        workspacePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(workspacePanel);
        workspacePanel.BringToFront();

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 9, 0),
            RowCount = 2,
            ColumnCount = 1
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspacePanel.Controls.Add(left, 0, 0);

        technicalPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(9, 0, 0, 0),
            RowCount = 3,
            ColumnCount = 1
        };
        technicalPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        technicalPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        technicalPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspacePanel.Controls.Add(technicalPanel, 1, 0);

        left.Controls.Add(CreateSessionCard(), 0, 0);
        left.Controls.Add(CreateLayersCard(), 0, 1);
        technicalPanel.Controls.Add(CreateSystemCard(), 0, 0);
        technicalPanel.Controls.Add(CreateSimulatorCard(), 0, 1);
        technicalPanel.Controls.Add(CreateLogCard(), 0, 2);
        ApplyTechnicalTheme(technicalPanel);
    }

    private Control CreateSessionCard()
    {
        var card = CreateCard();
        phaseLabel = new Label
        {
            Text = "ΤΟ ΔΕΝΤΡΟ ΠΕΡΙΜΕΝΕΙ ΤΟ ΑΓΓΙΓΜΑ",
            Location = new Point(22, 20),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            ForeColor = Accent
        };
        card.Controls.Add(phaseLabel);

        participationValueLabel = new Label
        {
            Text = "0 / 7",
            Location = new Point(24, 62),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold),
            ForeColor = PrimaryText
        };
        card.Controls.Add(participationValueLabel);

        levelLabel = new Label
        {
            Text = "ΑΓΓΙΓΜΑΤΑ ΜΑΖΙ  •  Μουσική σε αναμονή",
            Location = new Point(120, 75),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = SecondaryText
        };
        card.Controls.Add(levelLabel);

        elapsedLabel = new Label
        {
            Text = "00:00 / 03:00",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = PrimaryText
        };
        elapsedLabel.Location = new Point(600, 75);
        card.Controls.Add(elapsedLabel);

        sessionProgress = new AtmosphericProgressBar
        {
            Minimum = 0,
            Maximum = 180_000,
            Value = 0,
            Location = new Point(24, 112),
            Size = new Size(700, 14),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            FillStartColor = Accent,
            FillEndColor = Success,
            TrackColor = ControlBackground,
            BorderColor = CardBorder
        };
        card.Controls.Add(sessionProgress);

        participationCounterLabel = new Label
        {
            Text = "Αγγίγματα τώρα: 0  •  Συμμετοχές από την αρχή: 0  •  Συνάψεις: 0  •  Περισσότερα μαζί: 0",
            Location = new Point(24, 151),
            Size = new Size(700, 20),
            AutoSize = false,
            AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 9.5F),
            ForeColor = Accent
        };
        card.Controls.Add(participationCounterLabel);

        participationCounterResetButton = new AtmosphericButton
        {
            Text = "Μηδενισμός μετρητή",
            Size = new Size(155, 30),
            Location = new Point(560, 143),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Enabled = participationStore is not null,
            UseVisualStyleBackColor = false
        };
        participationCounterResetButton.Click += ParticipationCounterResetButtonOnClick;
        card.Controls.Add(participationCounterResetButton);

        synapseTimingLabel = new Label
        {
            Text = "Τρέχουσα σύναψη: 00:00  •  Συνολική επαφή: 00:00:00  •  Μεγαλύτερη σύναψη: 00:00",
            Location = new Point(24, 190),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = SecondaryText
        };
        card.Controls.Add(synapseTimingLabel);

        card.Resize += (_, _) =>
        {
            sessionProgress.Width = Math.Max(200, card.ClientSize.Width - 48);
            elapsedLabel.Left = card.ClientSize.Width - elapsedLabel.Width - 24;
            participationCounterResetButton.Left =
                card.ClientSize.Width - participationCounterResetButton.Width - 24;
            participationCounterLabel.Width = Math.Max(
                160,
                participationCounterResetButton.Left - participationCounterLabel.Left - 12);
        };
        return card;
    }

    private Control CreateLayersCard()
    {
        var card = CreateCard();
        var heading = CreateHeading("Η μουσική ζωή του δέντρου");
        heading.Location = new Point(22, 18);
        card.Controls.Add(heading);

        treeLayerVisualization = new TreeLayerVisualization
        {
            Location = new Point(0, 50),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI Semibold", 10F)
        };
        card.Controls.Add(treeLayerVisualization);
        card.Resize += (_, _) =>
        {
            treeLayerVisualization.Size = new Size(
                card.ClientSize.Width - 44,
                Math.Max(100, card.ClientSize.Height - 72));
            treeLayerVisualization.Location = new Point(22, 50);
        };
        return card;
    }

    private Control CreateSystemCard()
    {
        var card = CreateCard();
        var heading = CreateHeading("A. Εφαρμογή");
        heading.Location = new Point(22, 18);
        card.Controls.Add(heading);

        var compositionLabel = new Label
        {
            Text = "Μουσική σύνθεση",
            Location = new Point(22, 42),
            AutoSize = true,
            ForeColor = SecondaryText
        };
        card.Controls.Add(compositionLabel);

        compositionCombo = new AtmosphericComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(22, 58),
            Width = 410,
            Enabled = false
        };
        compositionCombo.SelectedIndexChanged += CompositionComboOnSelectedIndexChanged;
        card.Controls.Add(compositionCombo);

        sampleStatus = CreateStatusLabel("Samples", "Έλεγχος...");
        sampleStatus.Location = new Point(22, 90);
        card.Controls.Add(sampleStatus);
        audioStatus = CreateStatusLabel("Audio engine", "Δεν είναι έτοιμη");
        audioStatus.Location = new Point(182, 90);
        card.Controls.Add(audioStatus);
        deviceStatus = CreateStatusLabel("Έξοδος ήχου", "Αναζήτηση...");
        deviceStatus.Location = new Point(22, 113);
        card.Controls.Add(deviceStatus);

        deviceCombo = new AtmosphericComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(22, 136),
            Width = 210,
            Enabled = false
        };
        card.Controls.Add(deviceCombo);

        prepareButton = new AtmosphericButton
        {
            Text = "Προετοιμασία ήχου",
            Location = new Point(242, 134),
            Size = new Size(140, 30),
            Enabled = false,
            UseVisualStyleBackColor = false
        };
        prepareButton.Click += PrepareButtonOnClick;
        card.Controls.Add(prepareButton);

        card.Resize += (_, _) =>
        {
            compositionCombo.Width = Math.Max(160, card.ClientSize.Width - 44);
            prepareButton.Left = Math.Max(242, card.ClientSize.Width - prepareButton.Width - 22);
            deviceCombo.Width = Math.Max(140, prepareButton.Left - deviceCombo.Left - 10);
        };
        return card;
    }

    private Control CreateSimulatorCard()
    {
        var card = CreateCard();
        var heading = CreateHeading("B. ESP32");
        heading.Location = new Point(22, 18);
        card.Controls.Add(heading);

        calibrationProfileStatus = CreateStatusLabel("Profile", touchClassifier.Profile.Name);
        calibrationProfileStatus.Location = new Point(22, 38);
        card.Controls.Add(calibrationProfileStatus);

        serialStatus = CreateStatusLabel("Σύνδεση", "Αποσυνδεδεμένο");
        serialStatus.Location = new Point(22, 58);
        card.Controls.Add(serialStatus);

        portCombo = new AtmosphericComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(22, 78),
            Width = 250,
            Enabled = false
        };
        portCombo.DropDown += (_, _) => RefreshSerialPorts();
        card.Controls.Add(portCombo);

        serialButton = new AtmosphericButton
        {
            Text = "Σύνδεση",
            Location = new Point(208, 76),
            Size = new Size(100, 30),
            Enabled = false,
            UseVisualStyleBackColor = false
        };
        serialButton.Click += SerialButtonOnClick;
        card.Controls.Add(serialButton);

        calibrationButton = new AtmosphericButton
        {
            Text = "Βαθμονόμηση",
            Location = new Point(318, 76),
            Size = new Size(122, 30),
            Enabled = false,
            UseVisualStyleBackColor = false
        };
        calibrationButton.Click += async (_, _) => await RunCalibrationAsync();
        card.Controls.Add(calibrationButton);

        levelTrack = new TrackBar
        {
            Minimum = 0,
            Maximum = 7,
            TickFrequency = 1,
            LargeChange = 1,
            SmallChange = 1,
            Location = new Point(18, 110),
            Width = 390,
            Enabled = false
        };
        levelTrack.ValueChanged += (_, _) =>
        {
            if (updatingInputUi)
            {
                return;
            }
            var layers = ParticipationLevelMapper.ToLayerCount(levelTrack.Value);
            simulatorValue.Text = $"Συμμετοχή {levelTrack.Value} / 7  →  {layers} μουσικά layers";
            session.SetParticipationLevel(levelTrack.Value, DateTime.UtcNow);
            AddLog($"Simulator participation: {levelTrack.Value}; layers: {layers}");
        };
        card.Controls.Add(levelTrack);

        simulatorValue = new Label
        {
            Text = "Συμμετοχή 0 / 7  →  0 μουσικά layers",
            Location = new Point(22, 150),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = Accent
        };
        card.Controls.Add(simulatorValue);

        telemetryButton = new AtmosphericButton
        {
            Text = "Έναρξη τεχνικής καταγραφής",
            Location = new Point(22, 174),
            Size = new Size(230, 30),
            Enabled = false,
            UseVisualStyleBackColor = false,
            TabStop = true
        };
        telemetryButton.Click += async (_, _) => await ToggleTelemetryAsync();
        card.Controls.Add(telemetryButton);

        telemetryStatus = new Label
        {
            Text = "Προαιρετική • Ανενεργή",
            Location = new Point(262, 174),
            AutoSize = false,
            Size = new Size(148, 30),
            ForeColor = SecondaryText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        telemetryStatus.AutoEllipsis = true;
        telemetryStatus.Cursor = Cursors.Help;
        var telemetryToolTip = new ToolTip();
        telemetryToolTip.SetToolTip(telemetryStatus, telemetryRecorder.DatabasePath);
        card.Disposed += (_, _) => telemetryToolTip.Dispose();
        card.Controls.Add(telemetryStatus);
        card.Resize += (_, _) =>
        {
            calibrationButton.Left = Math.Max(318, card.ClientSize.Width - calibrationButton.Width - 22);
            serialButton.Left = Math.Max(208, calibrationButton.Left - serialButton.Width - 10);
            portCombo.Width = Math.Max(120, serialButton.Left - portCombo.Left - 10);
            levelTrack.Width = Math.Max(160, card.ClientSize.Width - 40);
            telemetryStatus.Left = telemetryButton.Right + 10;
            telemetryStatus.Width = Math.Max(80, card.ClientSize.Width - telemetryStatus.Left - 22);
        };
        return card;
    }

    private Control CreateLogCard()
    {
        var card = CreateCard();
        var heading = CreateHeading("Καταγραφή");
        heading.Location = new Point(22, 14);
        card.Controls.Add(heading);

        logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(9, 20, 15),
            ForeColor = PrimaryText,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F),
            Location = new Point(22, 42),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Size = new Size(388, 160)
        };
        card.Controls.Add(logBox);
        card.Resize += (_, _) =>
        {
            var availableHeight = card.ClientSize.Height - logBox.Top - 14;
            logBox.Size = new Size(
                Math.Max(120, card.ClientSize.Width - 44),
                Math.Max(1, availableHeight));
            logBox.Visible = availableHeight >= 20;
        };
        return card;
    }

    private static Panel CreateCard()
    {
        return new AtmosphericCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            BackColor = CardBackground,
            BorderStyle = BorderStyle.None,
            BorderColor = CardBorder
        };
    }

    private static void ApplyTechnicalTheme(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case Button button:
                    button.UseVisualStyleBackColor = false;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = CardBorder;
                    button.BackColor = ControlBackground;
                    button.ForeColor = PrimaryText;
                    break;
                case ComboBox comboBox:
                    comboBox.FlatStyle = FlatStyle.Flat;
                    comboBox.BackColor = ControlBackground;
                    comboBox.ForeColor = PrimaryText;
                    break;
                case TextBox textBox:
                    textBox.BackColor = Color.FromArgb(9, 20, 15);
                    textBox.ForeColor = PrimaryText;
                    break;
            }

            if (control.HasChildren)
            {
                ApplyTechnicalTheme(control);
            }
        }
    }

    private static Label CreateHeading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
        ForeColor = PrimaryText
    };

    private static Label CreateStatusLabel(string label, string value) => new()
    {
        Text = $"{label}: {value}",
        AutoSize = true,
        ForeColor = SecondaryText
    };

    private async void MainFormOnShown(object? sender, EventArgs e)
    {
        if (options.PreviewPath is not null)
        {
            PopulatePreviewState();
            BeginInvoke(RenderPreviewAndClose);
            return;
        }

        if (calibrationLoadWarning is not null)
        {
            AddLog($"WARNING: Active calibration was rejected; default profile loaded: {calibrationLoadWarning}");
        }
        if (participationLoadWarning is not null)
        {
            AddLog($"WARNING: Participation history issue: {participationLoadWarning}");
        }
        await RunStartupPreflightAsync();
        uiTimer.Start();
    }

    private async Task RunStartupPreflightAsync()
    {
        footerStatus.Text = "Αναζήτηση μουσικών συνθέσεων...";
        try
        {
            var catalog = await Task.Run(() => CompositionCatalog.Discover(options.CompositionRoot));
            var compositions = catalog.Compositions;
            changingComposition = true;
            compositionCombo.Items.Clear();
            foreach (var composition in compositions)
            {
                compositionCombo.Items.Add(composition);
            }
            compositionCombo.SelectedIndex = 0;
            changingComposition = false;

            if (!await SelectCompositionAsync(compositions[0], closeExistingEngine: false))
            {
                throw new InvalidDataException("The initial composition did not pass validation.");
            }
            foreach (var issue in catalog.Issues)
            {
                AddLog($"WARNING: Ignored composition folder {Path.GetFileName(issue.Directory)}: {issue.Message}");
            }
            AddLog($"Composition library: {compositions.Count} available.");

            footerStatus.Text = "Έλεγχος συσκευών ήχου...";
            var result = AudioDevicePreflight.Run();
            deviceCombo.Items.Clear();
            foreach (var device in result.ActiveRenderDevices)
            {
                deviceCombo.Items.Add(new DeviceItem(device));
            }
            var defaultIndex = result.ActiveRenderDevices
                .Select((device, index) => (device, index))
                .First(pair => pair.device.IsDefault).index;
            deviceCombo.SelectedIndex = defaultIndex;
            deviceCombo.Enabled = true;
            compositionCombo.Enabled = true;
            prepareButton.Enabled = selectedAssets is not null;
            SetStatus(deviceStatus, "Έξοδος ήχου", result.SelectedDevice.Name, Success);
            SetStatus(audioStatus, "Audio engine", "Preflight επιτυχές", Warning);
            foreach (var warning in result.Warnings)
            {
                AddLog($"Warning: {warning}");
            }
            RefreshSerialPorts();
            footerStatus.Text = "Preflight επιτυχές • Επιλέξτε Προετοιμασία ήχου";
        }
        catch (Exception exception)
        {
            HandleFault("Αποτυχία preflight", exception);
        }
    }

    private async void PrepareButtonOnClick(object? sender, EventArgs e)
    {
        if (deviceCombo.SelectedItem is not DeviceItem selected || selectedAssets is null)
        {
            return;
        }

        prepareButton.Enabled = false;
        deviceCombo.Enabled = false;
        footerStatus.Text = "Προετοιμασία audio engine...";
        try
        {
            var assets = selectedAssets;
            await Task.Run(() => audioEngine.Open(assets, selected.Info.Id));
            SetStatus(audioStatus, "Audio engine", "Έτοιμη", Success);
            SetStatus(deviceStatus, "Έξοδος ήχου", selected.Info.Name, Success);
            levelTrack.Enabled = true;
            portCombo.Enabled = portCombo.Items.Count > 0;
            serialButton.Enabled = portCombo.SelectedItem is not null;
            resetButton.Enabled = true;
            footerStatus.Text = "Έτοιμο • Ο ήχος ξεκινά με συμμετοχή 1–7";
            AddLog("Audio engine ready. No audio has played yet.");
        }
        catch (Exception exception)
        {
            deviceCombo.Enabled = true;
            prepareButton.Enabled = true;
            HandleFault("Αποτυχία προετοιμασίας ήχου", exception);
        }
    }

    private async void CompositionComboOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (changingComposition || compositionCombo.SelectedItem is not CompositionCatalogItem composition ||
            composition == selectedComposition)
        {
            return;
        }

        if (session.Snapshot().Phase != PresentationPhase.Ready)
        {
            AddLog("Composition change rejected while a session is active.");
            RestoreSelectedComposition();
            return;
        }

        if (!await SelectCompositionAsync(composition, closeExistingEngine: true))
        {
            RestoreSelectedComposition();
        }
    }

    private async Task<bool> SelectCompositionAsync(
        CompositionCatalogItem composition,
        bool closeExistingEngine)
    {
        compositionCombo.Enabled = false;
        prepareButton.Enabled = false;
        footerStatus.Text = $"Έλεγχος σύνθεσης: {composition.Name}...";
        try
        {
            var assets = await Task.Run(() => AudioManifestValidator.Validate(composition.Directory));
            if (closeExistingEngine && audioEngine.State is not SynapseAudioState.Closed)
            {
                await DisconnectSerialInputAsync();
                audioEngine.Close();
            }

            selectedComposition = composition;
            selectedAssets = assets;
            session.Configure(
                assets.Manifest.InteractionDurationSeconds,
                assets.Manifest.AudioFormat.DurationSeconds,
                assets.Manifest.AudioFormat.DurationToleranceSeconds);
            ApplyCompositionTiming(assets.Manifest);
            levelTrack.Value = 0;
            levelTrack.Enabled = false;
            resetButton.Enabled = false;
            SetStatus(sampleStatus, "Samples", $"{assets.LayerPaths.Count} έγκυρα layers", Success);
            SetStatus(audioStatus, "Audio engine", "Αναμονή προετοιμασίας", Warning);
            AddLog($"Audio assets verified: {assets.Manifest.Composition}");
            footerStatus.Text = $"Η σύνθεση «{composition.Name}» είναι έτοιμη για προετοιμασία";
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(sampleStatus, "Samples", "Αποτυχία ελέγχου", Error);
            footerStatus.Text = $"Μη έγκυρη σύνθεση: {composition.Name}";
            AddLog($"ERROR: Composition validation failed: {exception.Message}");
            return false;
        }
        finally
        {
            compositionCombo.Enabled = true;
            prepareButton.Enabled = selectedAssets is not null && deviceCombo.Items.Count > 0;
        }
    }

    private void RestoreSelectedComposition()
    {
        if (selectedComposition is null)
        {
            return;
        }

        changingComposition = true;
        compositionCombo.SelectedItem = selectedComposition;
        changingComposition = false;
    }

    private void ApplyCompositionTiming(AudioManifest manifest)
    {
        sessionProgress.Maximum = Math.Max(1,
            (int)Math.Round(manifest.InteractionDurationSeconds * 1000));
        UpdateDisplay(session.Snapshot());
    }

    private async void UiTimerOnTick(object? sender, EventArgs e)
    {
        if (uiTickInProgress || isClosing)
        {
            return;
        }

        uiTickInProgress = true;
        try
        {
            var now = DateTime.UtcNow;
            if (SerialHeartbeatPolicy.HasExpired(
                    serialConnectionExpected,
                    heartbeatTimedOut,
                    serialClient.LastValidMessageUtc,
                    now))
            {
                heartbeatTimedOut = true;
                CompleteContactCycle(participationAccumulator.HandleInputLoss(now));
                await ExecuteCommandsAsync(session.HandleInputLoss(now));
                await StopTelemetryAsync("απώλεια heartbeat");
                DisconnectSerialTransport();
                SetStatus(serialStatus, "Σύνδεση", "Χάθηκε το heartbeat • ασφαλής σίγαση", Error);
                AddLog("ERROR: ESP32 heartbeat timeout; audio faded and paused.");
            }

            CompleteContactCycle(participationAccumulator.Tick(now));

            var position = audioEngine.State is SynapseAudioState.Playing or SynapseAudioState.Paused
                ? audioEngine.CurrentPosition.TotalSeconds
                : 0;
            var commands = session.Tick(now, position);
            await ExecuteCommandsAsync(commands);
            UpdateDisplay(session.Snapshot(position));
            UpdateTelemetryUi();
        }
        catch (ObjectDisposedException) when (isClosing)
        {
            // Expected only when a pending fade completes during application shutdown.
        }
        catch (Exception exception)
        {
            if (!isClosing)
            {
                HandleFault("Σφάλμα κύκλου εφαρμογής", exception);
            }
        }
        finally
        {
            uiTickInProgress = false;
        }
    }

    private async Task ExecuteCommandsAsync(IReadOnlyList<AudioCommand> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        await commandGate.WaitAsync();
        try
        {
            foreach (var command in commands)
            {
                switch (command.Type)
                {
                    case AudioCommandType.Start:
                        audioEngine.Start();
                        AddLog("Session started.");
                        break;
                    case AudioCommandType.Resume:
                        audioEngine.Resume();
                        AddLog("Session resumed.");
                        break;
                    case AudioCommandType.SetLevel:
                        audioEngine.SetLevel(command.Level);
                        AddLog($"Active layers: {command.Level}");
                        break;
                    case AudioCommandType.PauseAfterFade:
                        await audioEngine.PauseAfterFadeAsync();
                        AddLog("Session paused after fade.");
                        break;
                    case AudioCommandType.BeginTail:
                        audioEngine.BeginTail();
                        AddLog("Final tail started.");
                        break;
                    case AudioCommandType.Stop:
                        if (audioEngine.State is not (SynapseAudioState.Closed or SynapseAudioState.Disposed))
                        {
                            audioEngine.Stop();
                        }
                        levelTrack.Value = 0;
                        AddLog("Session reset.");
                        break;
                }
            }
        }
        catch (Exception exception)
        {
            HandleFault("Σφάλμα audio state", exception);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private void UpdateDisplay(SessionSnapshot snapshot)
    {
        phaseLabel.Text = snapshot.Phase switch
        {
            PresentationPhase.Ready => "ΤΟ ΔΕΝΤΡΟ ΠΕΡΙΜΕΝΕΙ ΤΟ ΑΓΓΙΓΜΑ",
            PresentationPhase.Building => "ΤΟ ΔΕΝΤΡΟ ΖΩΝΤΑΝΕΥΕΙ",
            PresentationPhase.Paused => "ΑΓΓΙΞΤΕ ΓΙΑ ΝΑ ΣΥΝΕΧΙΣΕΙ",
            PresentationPhase.Climax => "ΤΟ ΔΕΝΤΡΟ ΣΕ ΠΛΗΡΗ ΖΩΗ",
            PresentationPhase.Ending => "Η ΖΩΗ ΤΟΥ ΔΕΝΤΡΟΥ ΑΝΤΗΧΕΙ",
            PresentationPhase.Fault => "Η ΕΜΠΕΙΡΙΑ ΧΡΕΙΑΖΕΤΑΙ ΕΛΕΓΧΟ",
            _ => snapshot.Phase.ToString().ToUpperInvariant()
        };
        phaseLabel.ForeColor = snapshot.Phase == PresentationPhase.Fault ? Error : Accent;
        compositionCombo.Enabled = !changingComposition &&
            snapshot.Phase == PresentationPhase.Ready &&
            !telemetryRecorder.IsRecording;
        levelTrack.Enabled = !serialConnectionExpected &&
            snapshot.Phase != PresentationPhase.Fault &&
            audioEngine.State is SynapseAudioState.Ready or
                SynapseAudioState.Playing or SynapseAudioState.Paused;
        serialButton.Enabled = serialConnectionExpected ||
            (snapshot.Phase == PresentationPhase.Ready &&
             audioEngine.State == SynapseAudioState.Ready &&
             portCombo.SelectedItem is not null);
        calibrationButton.Enabled = serialConnectionExpected &&
            snapshot.Phase == PresentationPhase.Ready &&
            audioEngine.State == SynapseAudioState.Ready &&
            !calibrationActive;
        UpdateAudioMuteUi();
        participationValueLabel.Text = $"{snapshot.ParticipationLevel} / 7";
        levelLabel.Text =
            $"ΑΓΓΙΓΜΑΤΑ ΜΑΖΙ  •  Μουσική: {GetMusicalStageName(snapshot.EffectiveLayerCount)}";
        UpdateParticipationCounterDisplay();
        UpdateSynapseTimingDisplay();
        sessionProgress.Value = (int)Math.Clamp(
            snapshot.ContactMilliseconds,
            sessionProgress.Minimum,
            sessionProgress.Maximum);
        var elapsedSeconds = Math.Min(
            snapshot.InteractionDurationMilliseconds / 1000,
            snapshot.ContactMilliseconds / 1000);
        var interactionSeconds = snapshot.InteractionDurationMilliseconds / 1000;
        elapsedLabel.Text = snapshot.TailActive
            ? $"{FormatTime(interactionSeconds)} • ουρά {FormatTime(snapshot.AudioPositionSeconds, true)}"
            : $"{FormatTime(elapsedSeconds)} / {FormatTime(interactionSeconds)}";

        treeLayerVisualization.ActiveLayerCount = snapshot.EffectiveLayerCount;
        treeLayerVisualization.GrowingLayerIndex =
            snapshot.Phase == PresentationPhase.Building && snapshot.EffectiveLayerCount > 0
                ? snapshot.EffectiveLayerCount - 1
                : -1;
    }

    private static string GetMusicalStageName(int effectiveLayerCount) =>
        effectiveLayerCount <= 0
            ? "Σε αναμονή"
            : MusicalStageNames[Math.Min(effectiveLayerCount, MusicalStageNames.Length) - 1];

    private void ToggleAudioMute()
    {
        try
        {
            var muted = !audioEngine.IsMuted;
            audioEngine.SetMuted(muted);
            UpdateAudioMuteUi();
            footerStatus.Text = muted
                ? "Η μουσική είναι σε σίγαση • Καταγραφή και LEDs συνεχίζουν"
                : "Η μουσική ενεργοποιήθηκε";
            AddLog(muted
                ? "Music muted; session tracking and LEDs continue."
                : "Music unmuted.");
        }
        catch (Exception exception)
        {
            HandleFault("Αποτυχία αλλαγής σίγασης", exception);
        }
    }

    private void UpdateAudioMuteUi()
    {
        muteButton.Enabled = audioEngine.State is
            SynapseAudioState.Ready or SynapseAudioState.Playing or SynapseAudioState.Paused;
        muteButton.Checked = audioEngine.IsMuted;
        muteButton.Text = audioEngine.IsMuted
            ? "Ενεργοποίηση μουσικής"
            : "Σίγαση μουσικής";
    }

    private static string FormatTime(double seconds, bool includeMilliseconds = false)
    {
        var totalMilliseconds = Math.Max(0, (long)Math.Round(seconds * 1000));
        var minutes = totalMilliseconds / 60_000;
        var remainingSeconds = totalMilliseconds / 1000 % 60;
        if (!includeMilliseconds)
        {
            return $"{minutes:00}:{remainingSeconds:00}";
        }

        var milliseconds = totalMilliseconds % 1000;
        return $"{minutes:00}:{remainingSeconds:00},{milliseconds:000}";
    }

    private void AudioEngineOnFaulted(object? sender, AudioEngineFaultEventArgs e)
    {
        if (DispatchToUiIfRequired(() => AudioEngineOnFaulted(sender, e)))
        {
            return;
        }
        session.SetFault();
        DisconnectSerialTransport();
        HandleFault("Απώλεια συσκευής ήχου", e.Exception);
    }

    private void RefreshSerialPorts()
    {
        var selected = portCombo.SelectedItem as string;
        var ports = SerialTouchClient.GetAvailablePorts();
        portCombo.Items.Clear();
        foreach (var port in ports)
        {
            portCombo.Items.Add(port);
        }

        if (selected is not null && ports.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            portCombo.SelectedItem = selected;
        }
        else if (portCombo.Items.Count > 0)
        {
            portCombo.SelectedIndex = 0;
        }
        portCombo.Enabled = !serialConnectionExpected && portCombo.Items.Count > 0;
    }

    private async void SerialButtonOnClick(object? sender, EventArgs e)
    {
        if (serialConnectionExpected)
        {
            await DisconnectSerialInputAsync();
            return;
        }
        if (portCombo.SelectedItem is not string portName || audioEngine.State != SynapseAudioState.Ready)
        {
            return;
        }

        try
        {
            serialClient.Connect(portName);
            serialConnectionExpected = true;
            participationAccumulator.Reset();
            currentSynapseId = null;
            inputArmer.Reset();
            touchClassifier.Reset();
            heartbeatTimedOut = false;
            portCombo.Enabled = false;
            levelTrack.Enabled = false;
            serialButton.Text = "Αποσύνδεση";
            SetStatus(serialStatus, "Σύνδεση", $"Συνδεδεμένο στο {portName} • αναμονή heartbeat", Warning);
            AddLog($"ESP32 connected: {portName}");
        }
        catch (Exception exception)
        {
            SetStatus(serialStatus, "Σύνδεση", "Αποτυχία σύνδεσης", Error);
            AddLog($"ERROR: ESP32 connection failed: {exception.Message}");
        }
    }

    private async Task ToggleTelemetryAsync()
    {
        if (telemetryOperationInProgress)
        {
            return;
        }

        telemetryOperationInProgress = true;
        UpdateTelemetryUi();
        try
        {
            if (telemetryRecorder.IsRecording)
            {
                await StopTelemetryAsync("χειροκίνητη διακοπή");
                return;
            }
            if (!serialConnectionExpected || portCombo.SelectedItem is not string portName)
            {
                return;
            }

            var context = new SensorTelemetrySession(
                Guid.NewGuid(),
                DateTime.UtcNow,
                portName,
                touchClassifier.Profile.Name,
                selectedComposition?.Name ?? "unknown-composition",
                Application.ProductVersion);
            await telemetryRecorder.StartAsync(context);
            AddLog($"Sensor telemetry started: {Path.GetFileName(telemetryRecorder.DatabasePath)}");
        }
        catch (Exception exception)
        {
            AddLog($"ERROR: Sensor telemetry could not start: {exception.Message}");
            MessageBox.Show(
                this,
                "Η τεχνική καταγραφή δεν ξεκίνησε. Η μουσική, τα LEDs και οι Συνάψεις " +
                "συνεχίζουν κανονικά.\r\n\r\n" + exception.Message,
                "Αποτυχία τεχνικής καταγραφής",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            telemetryOperationInProgress = false;
            UpdateTelemetryUi();
        }
    }

    private async Task StopTelemetryAsync(string reason)
    {
        var wasRecording = telemetryRecorder.IsRecording;
        await telemetryRecorder.StopAsync();
        if (wasRecording)
        {
            AddLog($"Sensor telemetry stopped: {reason}; " +
                $"dropped samples {telemetryRecorder.DroppedSamples}.");
        }
        UpdateTelemetryUi();
    }

    private async void TelemetryRecorderOnFaulted(Exception exception)
    {
        if (DispatchToUiIfRequired(() => TelemetryRecorderOnFaulted(exception)))
        {
            return;
        }

        AddLog($"ERROR: Sensor telemetry stopped safely: {exception.Message}");
        try
        {
            await telemetryRecorder.StopAsync();
        }
        catch (Exception cleanupException)
        {
            AddLog($"WARNING: Telemetry cleanup reported: {cleanupException.Message}");
        }
        telemetryOperationInProgress = false;
        UpdateTelemetryUi();
    }

    private void UpdateTelemetryUi()
    {
        if (telemetryButton is null || telemetryStatus is null)
        {
            return;
        }

        var recording = telemetryRecorder.IsRecording;
        telemetryButton.Text = recording
            ? "Διακοπή τεχνικής καταγραφής"
            : "Έναρξη τεχνικής καταγραφής";
        telemetryButton.Enabled = !telemetryOperationInProgress &&
            (recording || serialConnectionExpected);
        if (recording)
        {
            var duration = telemetryRecorder.Elapsed;
            telemetryStatus.Text =
                $"● Ενεργή — {(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
            telemetryStatus.ForeColor = Success;
        }
        else
        {
            telemetryStatus.Text = telemetryOperationInProgress
                ? "Ολοκλήρωση..."
                : "Προαιρετική • Ανενεργή";
            telemetryStatus.ForeColor = telemetryOperationInProgress ? Warning : SecondaryText;
        }
    }

    private void SerialClientOnMessageReceived(TouchStateMessage message)
    {
        if (DispatchToUiIfRequired(() => SerialClientOnMessageReceived(message)))
        {
            return;
        }

        heartbeatTimedOut = false;
        var now = DateTime.UtcNow;
        var classifiedLevel = touchClassifier.Update(message.SmoothValue);
        if (calibrationActive)
        {
            RecordTelemetry(message, classifiedLevel, "calibration", null, now);
            SetStatus(serialStatus, "Σύνδεση",
                $"Calibration • raw {message.RawValue} • smooth {message.SmoothValue}", Warning);
            return;
        }
        if (!inputArmer.TryAccept(classifiedLevel, out var acceptedLevel))
        {
            RecordTelemetry(message, classifiedLevel, "stabilizing", null, now);
            SetStatus(serialStatus, "Σύνδεση",
                $"Σταθεροποίηση • raw {message.RawValue} • smooth {message.SmoothValue}", Warning);
            return;
        }

        if (acceptedLevel > 0)
        {
            currentSynapseId ??= Guid.NewGuid();
        }
        session.SetParticipationLevel(acceptedLevel, now);
        participationAccumulator.ObserveLevel(acceptedLevel, now);
        var phase = acceptedLevel > 0
            ? "active"
            : currentSynapseId is not null ? "release-delay" : "idle";
        RecordTelemetry(message, acceptedLevel, phase, currentSynapseId, now);
        updatingInputUi = true;
        levelTrack.Value = acceptedLevel;
        updatingInputUi = false;
        var layers = ParticipationLevelMapper.ToLayerCount(acceptedLevel);
        simulatorValue.Text =
            $"Συμμετοχή {acceptedLevel} / 7  →  {layers} μουσικά layers";
        SetStatus(serialStatus, "Σύνδεση",
            $"Online • raw {message.RawValue} • smooth {message.SmoothValue} • fw {message.ParticipationLevel}",
            Success);
    }

    private void RecordTelemetry(
        TouchStateMessage message,
        int appLevel,
        string phase,
        Guid? synapseId,
        DateTime recordedUtc)
    {
        telemetryRecorder.TryRecord(new SensorTelemetrySample(
            recordedUtc,
            synapseId,
            phase,
            message.RawValue,
            message.SmoothValue,
            message.ParticipationLevel,
            appLevel,
            message.Sequence,
            message.UptimeMilliseconds));
    }

    private void SerialClientOnInvalidLineReceived(string line)
    {
        if (DispatchToUiIfRequired(() => SerialClientOnInvalidLineReceived(line)))
        {
            return;
        }
        AddLog($"WARNING: Invalid ESP32 message ignored: {line}");
    }

    private async void SerialClientOnConnectionFaulted(Exception exception)
    {
        if (DispatchToUiIfRequired(() => SerialClientOnConnectionFaulted(exception)))
        {
            return;
        }
        AddLog($"ERROR: ESP32 serial fault: {exception.Message}");
        await DisconnectSerialInputAsync();
    }

    private bool DispatchToUiIfRequired(Action callback)
    {
        if (isClosing || IsDisposed || Disposing)
        {
            return true;
        }
        if (!InvokeRequired)
        {
            return false;
        }
        if (!IsHandleCreated)
        {
            return true;
        }

        try
        {
            BeginInvoke(callback);
        }
        catch (InvalidOperationException) when (isClosing || IsDisposed || Disposing)
        {
            // A device callback raced with orderly window shutdown.
        }
        return true;
    }

    private async Task DisconnectSerialInputAsync()
    {
        await StopTelemetryAsync("αποσύνδεση ESP32");
        serialClient.Disconnect();
        inputArmer.Reset();
        heartbeatTimedOut = false;
        CompleteContactCycle(participationAccumulator.HandleInputLoss(DateTime.UtcNow));
        await ExecuteCommandsAsync(session.HandleInputLoss(DateTime.UtcNow));
        DisconnectSerialTransport();
        AddLog("ESP32 disconnected; audio faded and paused.");
    }

    private void DisconnectSerialTransport()
    {
        serialConnectionExpected = false;
        try
        {
            serialClient.Disconnect();
        }
        catch (Exception exception)
        {
            AddLog($"WARNING: Serial cleanup reported: {exception.Message}");
        }
        inputArmer.Reset();
        touchClassifier.Reset();
        heartbeatTimedOut = false;
        updatingInputUi = true;
        levelTrack.Value = 0;
        updatingInputUi = false;
        simulatorValue.Text = "Συμμετοχή 0 / 7  →  0 μουσικά layers";
        serialButton.Text = "Σύνδεση";
        SetStatus(serialStatus, "Σύνδεση", "Αποσυνδεδεμένο", SecondaryText);
        RefreshSerialPorts();
        levelTrack.Enabled = audioEngine.State is SynapseAudioState.Ready or
            SynapseAudioState.Playing or SynapseAudioState.Paused;
    }

    private async Task RunCalibrationAsync()
    {
        if (!serialConnectionExpected || session.Snapshot().Phase != PresentationPhase.Ready ||
            audioEngine.State != SynapseAudioState.Ready)
        {
            return;
        }

        await ExecuteCommandsAsync(session.HandleInputLoss(DateTime.UtcNow));
        calibrationActive = true;
        calibrationButton.Enabled = false;
        inputArmer.Reset();
        touchClassifier.Reset();
        try
        {
            using var form = new UnifiedCalibrationForm(
                serialClient,
                touchClassifier.Profile,
                options.CalibrationDirectory);
            if (form.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var resultProfile = form.ResultProfile;
            if (resultProfile is not null)
            {
                if (telemetryRecorder.IsRecording)
                {
                    await StopTelemetryAsync("αλλαγή calibration profile");
                }
                touchClassifier.ReplaceProfile(resultProfile);
                SetStatus(calibrationProfileStatus, "Profile", resultProfile.Name, Success);
                AddLog(
                    $"Calibration activated: {resultProfile.Name}; " +
                    $"thresholds={string.Join(',', resultProfile.Thresholds)}; " +
                    $"hysteresis={resultProfile.Hysteresis}.");
            }
        }
        catch (Exception exception)
        {
            AddLog($"ERROR: Calibration failed: {exception.Message}");
            SetStatus(calibrationProfileStatus, "Profile", "Αποτυχία βαθμονόμησης", Error);
        }
        finally
        {
            calibrationActive = false;
            inputArmer.Reset();
            touchClassifier.Reset();
            UpdateDisplay(session.Snapshot());
        }
    }

    private void CompleteContactCycle(CompletedContactCycle? cycle)
    {
        if (cycle is null || cycle.PeakLevel <= 0)
        {
            return;
        }

        var eventId = currentSynapseId ?? Guid.NewGuid();
        currentSynapseId = null;
        var updatedTotals = participationTotals.Add(cycle);
        try
        {
            participationStore?.AppendContact(
                eventId,
                cycle,
                touchClassifier.Profile.Name,
                selectedComposition?.Name ?? "unknown-composition",
                Application.ProductVersion);
        }
        catch (Exception exception)
        {
            AddLog($"ERROR: Participation event was not persisted: {exception.Message}");
        }
        participationTotals = updatedTotals;
        UpdateParticipationCounterDisplay();
        AddLog(
            $"Synapse completed: peak {cycle.PeakLevel}; " +
            $"active {cycle.ActiveContactMilliseconds} ms; " +
            $"span {cycle.GroupSpanMilliseconds} ms; reason {cycle.EndReason}.");
    }

    private void ParticipationCounterResetButtonOnClick(object? sender, EventArgs e)
    {
        var confirmation = MessageBox.Show(
            this,
            "Να μηδενιστούν οι εμφανιζόμενες συνολικές συμμετοχές;\r\n\r\n" +
            "Το ιστορικό δεν θα διαγραφεί. Θα προστεθεί εγγραφή reset.",
            "Μηδενισμός μετρητή",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var now = DateTime.UtcNow;
        try
        {
            participationStore?.AppendReset(now);
        }
        catch (Exception exception)
        {
            AddLog($"ERROR: Participation counter reset was not persisted: {exception.Message}");
            MessageBox.Show(
                this,
                "Ο μηδενισμός δεν ολοκληρώθηκε επειδή δεν αποθηκεύτηκε με ασφάλεια.",
                "Αποτυχία μηδενισμού",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        participationAccumulator.Reset();
        currentSynapseId = null;
        participationTotals = new ParticipationTotals(0, 0, 0, 0, 0, now);
        UpdateParticipationCounterDisplay();
        UpdateSynapseTimingDisplay();
        AddLog("Participation counter reset; history retained.");
    }

    private void UpdateParticipationCounterDisplay()
    {
        participationCounterLabel.Text =
            $"Αγγίγματα τώρα: {participationAccumulator.CurrentLevel}  •  " +
            $"Συμμετοχές από την αρχή: {participationTotals.EstimatedParticipations}  •  " +
            $"Συνάψεις: {participationTotals.Synapses}  •  " +
            $"Περισσότερα μαζί: {participationTotals.MaximumSimultaneous}";
    }

    private void UpdateSynapseTimingDisplay()
    {
        synapseTimingLabel.Text =
            $"Τρέχουσα σύναψη: {FormatDuration(participationAccumulator.CurrentActiveDurationMilliseconds)}  •  " +
            $"Συνολική επαφή: {FormatDuration(participationTotals.TotalActiveContactMilliseconds, true)}  •  " +
            $"Μεγαλύτερη σύναψη: {FormatDuration(participationTotals.LongestActiveContactMilliseconds)}";
    }

    private static string FormatDuration(long milliseconds, bool includeHours = false)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return includeHours || duration.TotalHours >= 1
            ? $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(long)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private void HandleFault(string title, Exception exception)
    {
        session.SetFault();
        levelTrack.Enabled = false;
        prepareButton.Enabled = true;
        deviceCombo.Enabled = true;
        SetStatus(audioStatus, "Audio engine", title, Error);
        footerStatus.Text = title;
        AddLog($"ERROR: {title}: {exception.Message}");
        UpdateDisplay(session.Snapshot());
    }

    private void AddLog(string text)
    {
        logLines.Enqueue($"{DateTime.Now:HH:mm:ss}  {text}");
        while (logLines.Count > 200)
        {
            logLines.Dequeue();
        }
        logBox.Text = string.Join(Environment.NewLine, logLines);
        logBox.SelectionStart = logBox.TextLength;
        logBox.ScrollToCaret();
    }

    private static void SetStatus(Label target, string label, string value, Color color)
    {
        target.Text = $"{label}: {value}";
        target.ForeColor = color;
    }

    private void ToggleFullScreen()
    {
        if (!isFullScreen)
        {
            technicalPanelWasVisibleBeforeFullScreen = technicalPanel.Visible;
            SetTechnicalPanelVisibility(false);
            technicalPanelButton.Visible = false;
            previousBorderStyle = FormBorderStyle;
            previousWindowState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            fullScreenButton.Text = "Έξοδος πλήρους οθόνης";
        }
        else
        {
            FormBorderStyle = previousBorderStyle;
            WindowState = previousWindowState;
            fullScreenButton.Text = "Πλήρης οθόνη";
            technicalPanelButton.Visible = true;
            SetTechnicalPanelVisibility(technicalPanelWasVisibleBeforeFullScreen);
        }
        isFullScreen = !isFullScreen;
    }

    private void SetTechnicalPanelVisibility(bool visible)
    {
        technicalPanel.Visible = visible;
        workspacePanel.ColumnStyles[0].SizeType = SizeType.Percent;
        workspacePanel.ColumnStyles[0].Width = visible ? 64 : 100;
        workspacePanel.ColumnStyles[1].SizeType = SizeType.Percent;
        workspacePanel.ColumnStyles[1].Width = visible ? 36 : 0;
        resetButton.Visible = visible;
        participationCounterResetButton.Visible = visible;
        technicalPanelButton.Text = visible ? "Προβολή εγκατάστασης" : "Τεχνικός έλεγχος";
    }

    private void PopulatePreviewState()
    {
        SetTechnicalPanelVisibility(options.TechnicalPreview);
        compositionCombo.Items.Add("Tree Installation 1");
        compositionCombo.SelectedIndex = 0;
        compositionCombo.Enabled = true;
        SetStatus(sampleStatus, "Samples", "5 έγκυρα layers", Success);
        SetStatus(audioStatus, "Audio engine", "Έτοιμη", Success);
        SetStatus(deviceStatus, "Έξοδος ήχου", "Windows default output", Success);
        deviceCombo.Items.Add("Speakers • Windows default");
        deviceCombo.SelectedIndex = 0;
        prepareButton.Enabled = true;
        portCombo.Items.Add("COM4");
        portCombo.SelectedIndex = 0;
        portCombo.Enabled = true;
        serialButton.Enabled = true;
        serialButton.Text = "Αποσύνδεση";
        SetStatus(serialStatus, "Σύνδεση", "Online • raw 470000 • smooth 465200", Success);
        levelTrack.Enabled = true;
        levelTrack.Value = 7;
        phaseLabel.Text = "ΤΟ ΔΕΝΤΡΟ ΣΕ ΠΛΗΡΗ ΖΩΗ";
        participationValueLabel.Text = "7 / 7";
        levelLabel.Text = "ΑΓΓΙΓΜΑΤΑ ΜΑΖΙ  •  Μουσική: Πλήρης ζωή";
        sessionProgress.Value = 72_000;
        elapsedLabel.Text = "01:12 / 03:00";
        treeLayerVisualization.ActiveLayerCount = 5;
        treeLayerVisualization.GrowingLayerIndex = -1;
        participationCounterLabel.Text =
            "Αγγίγματα τώρα: 3  •  Συμμετοχές από την αρχή: 18  •  " +
            "Συνάψεις: 6  •  Περισσότερα μαζί: 4";
        synapseTimingLabel.Text =
            "Τρέχουσα σύναψη: 01:12  •  Συνολική επαφή: 00:08:46  •  Μεγαλύτερη σύναψη: 02:05";
        AddLog("Audio assets verified: tree instalation1");
        AddLog("Audio engine ready. No audio has played yet.");
        AddLog("Simulator participation: 7; layers: 5");
        footerStatus.Text = "Έτοιμο • Λειτουργία προσομοίωσης";
    }

    private void RenderPreviewAndClose()
    {
        if (options.PreviewPath is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(options.PreviewPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bitmap, ClientRectangle);
        bitmap.Save(options.PreviewPath, System.Drawing.Imaging.ImageFormat.Png);
        Close();
    }

    private void MainFormOnFormClosing(object? sender, FormClosingEventArgs e)
    {
        isClosing = true;
        uiTimer.Stop();
        audioEngine.Faulted -= AudioEngineOnFaulted;
        serialClient.MessageReceived -= SerialClientOnMessageReceived;
        serialClient.ConnectionFaulted -= SerialClientOnConnectionFaulted;
        serialClient.InvalidLineReceived -= SerialClientOnInvalidLineReceived;
        telemetryRecorder.Faulted -= TelemetryRecorderOnFaulted;
        CompleteContactCycle(participationAccumulator.HandleInputLoss(DateTime.UtcNow));
        try
        {
            telemetryRecorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            AddLog($"WARNING: Telemetry shutdown reported: {exception.Message}");
        }
        serialClient.Dispose();
        audioEngine.Dispose();
    }

    private sealed record DeviceItem(AudioDeviceInfo Info)
    {
        public override string ToString() => Info.IsDefault ? $"{Info.Name} (προεπιλογή)" : Info.Name;
    }
}
