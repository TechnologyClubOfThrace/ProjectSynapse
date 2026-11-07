using Synapse.Input;

namespace Synapse.App;

internal sealed class UnifiedCalibrationForm : Form
{
    private static readonly TimeSpan StabilizationDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(12);
    private readonly SerialTouchClient serialClient;
    private readonly string calibrationDirectory;
    private readonly List<List<uint>> samplesByLevel = Enumerable.Range(0, 8)
        .Select(_ => new List<uint>())
        .ToList();
    private readonly List<uint> pendingSamples = [];
    private readonly TouchCalibrationLevelSummary?[] summariesByLevel =
        new TouchCalibrationLevelSummary?[8];
    private readonly HashSet<int> changedLevels = [];
    private readonly Button[] captureButtons = new Button[8];
    private readonly Button[] resetButtons = new Button[8];
    private readonly Label[] rangeLabels = new Label[8];
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly Label instructionLabel = new();
    private readonly Label liveValueLabel = new();
    private readonly Label validationLabel = new();
    private readonly ProgressBar progress = new();
    private readonly Button cancelCaptureButton = new();
    private readonly Button saveButton = new();
    private readonly TextBox nameBox = new();
    private readonly ComboBox savedProfileCombo = new();
    private readonly Button openProfileButton = new();
    private TouchCalibrationProfile workingProfile;
    private DateTime captureStartsAt;
    private DateTime captureEndsAt;
    private int? capturingLevel;
    private bool collectionStarted;
    private bool updatingProfileUi;
    private bool openedSavedProfile;
    private int activeHighestRecordedLevel = -1;

    public UnifiedCalibrationForm(
        SerialTouchClient serialClient,
        TouchCalibrationProfile existingProfile,
        string calibrationDirectory)
    {
        this.serialClient = serialClient;
        workingProfile = existingProfile;
        this.calibrationDirectory = calibrationDirectory;
        BuildInterface();
        LoadProfile(existingProfile, openedFromLibrary: false);
        RefreshSavedProfiles();
        serialClient.MessageReceived += SerialClientOnMessageReceived;
        timer.Tick += TimerOnTick;
        FormClosed += (_, _) =>
        {
            timer.Stop();
            serialClient.MessageReceived -= SerialClientOnMessageReceived;
        };
    }

    public TouchCalibrationProfile? ResultProfile { get; private set; }

    private void BuildInterface()
    {
        Text = "Ενιαία βαθμονόμηση αφής Synapse";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(780, 700);
        Font = new Font("Segoe UI", 9F);

        Controls.Add(new Label
        {
            Text = "Βαθμονόμηση περιοχών συμμετοχής 0–7",
            Location = new Point(24, 18),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold)
        });

        instructionLabel.Text =
            "Καταγράψτε κάθε κατάσταση ανεξάρτητα. Το ενεργό profile δεν αλλάζει πριν από την τελική αποθήκευση.";
        instructionLabel.Location = new Point(26, 58);
        instructionLabel.Size = new Size(728, 42);
        Controls.Add(instructionLabel);

        liveValueLabel.Text = "Τρέχουσα smooth τιμή: αναμονή...";
        liveValueLabel.Location = new Point(26, 105);
        liveValueLabel.AutoSize = true;
        Controls.Add(liveValueLabel);

        progress.Location = new Point(26, 134);
        progress.Size = new Size(550, 18);
        progress.Maximum = (int)(StabilizationDuration + CaptureDuration).TotalMilliseconds;
        Controls.Add(progress);

        cancelCaptureButton.Text = "Ακύρωση μέτρησης";
        cancelCaptureButton.Location = new Point(590, 128);
        cancelCaptureButton.Size = new Size(164, 30);
        cancelCaptureButton.Enabled = false;
        cancelCaptureButton.Click += (_, _) => CancelCurrentCapture();
        Controls.Add(cancelCaptureButton);

        var table = new TableLayoutPanel
        {
            Location = new Point(26, 174),
            Size = new Size(728, 336),
            ColumnCount = 3,
            RowCount = 8,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        for (var level = 0; level <= 7; level++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
            var capturedLevel = level;
            captureButtons[level] = new Button
            {
                Text = level == 0 ? "Χωρίς επαφή" : $"Επαφή επιπέδου {level}",
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                UseVisualStyleBackColor = true
            };
            captureButtons[level].Click += (_, _) => StartCapture(capturedLevel);
            rangeLabels[level] = new Label
            {
                Text = "Δεν έχει καταγραφεί",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(96, 96, 96),
                Padding = new Padding(8, 0, 0, 0)
            };
            resetButtons[level] = new Button
            {
                Text = "Μηδενισμός",
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                Enabled = false,
                UseVisualStyleBackColor = true
            };
            resetButtons[level].Click += (_, _) => ResetLevel(capturedLevel);
            table.Controls.Add(captureButtons[level], 0, level);
            table.Controls.Add(rangeLabels[level], 1, level);
            table.Controls.Add(resetButtons[level], 2, level);
        }
        Controls.Add(table);

        validationLabel.Text =
            "Απαιτούνται τα επίπεδα 0 και 1. Τα επόμενα είναι προαιρετικά αλλά πρέπει να είναι συνεχόμενα.";
        validationLabel.Location = new Point(26, 525);
        validationLabel.Size = new Size(728, 50);
        Controls.Add(validationLabel);

        Controls.Add(new Label
        {
            Text = "Όνομα profile",
            Location = new Point(26, 583),
            AutoSize = true
        });
        nameBox.Text = "full-touch-profile";
        nameBox.Location = new Point(26, 602);
        nameBox.Size = new Size(340, 25);
        nameBox.TextChanged += (_, _) =>
        {
            if (updatingProfileUi)
            {
                return;
            }
            openedSavedProfile = true;
            ValidateCompleteProfile();
        };
        Controls.Add(nameBox);

        Controls.Add(new Label
        {
            Text = "Αποθηκευμένο profile",
            Location = new Point(390, 583),
            AutoSize = true
        });
        savedProfileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        savedProfileCombo.Location = new Point(390, 602);
        savedProfileCombo.Size = new Size(248, 25);
        savedProfileCombo.SelectedIndexChanged += (_, _) =>
            openProfileButton.Enabled = savedProfileCombo.SelectedItem is SavedProfileItem;
        Controls.Add(savedProfileCombo);

        openProfileButton.Text = "Άνοιγμα";
        openProfileButton.Location = new Point(648, 600);
        openProfileButton.Size = new Size(106, 30);
        openProfileButton.Enabled = false;
        openProfileButton.Click += (_, _) => OpenSelectedProfile();
        Controls.Add(openProfileButton);

        saveButton.Text = "Αποθήκευση";
        saveButton.Location = new Point(520, 635);
        saveButton.Size = new Size(125, 32);
        saveButton.Enabled = false;
        saveButton.Click += SaveButtonOnClick;
        Controls.Add(saveButton);

        var cancelButton = new Button
        {
            Text = "Ακύρωση",
            Location = new Point(660, 636),
            Size = new Size(94, 30),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(cancelButton);
        CancelButton = cancelButton;
    }

    private void StartCapture(int level)
    {
        capturingLevel = level;
        pendingSamples.Clear();
        progress.Value = 0;
        captureStartsAt = DateTime.UtcNow + StabilizationDuration;
        captureEndsAt = captureStartsAt + CaptureDuration;
        collectionStarted = false;
        SetCaptureButtonsEnabled(false);
        SetResetButtonsEnabled(false);
        cancelCaptureButton.Enabled = true;
        saveButton.Enabled = false;
        instructionLabel.Text = level == 0
            ? "Μην αγγίζετε καμία επιφάνεια. Σταθεροποίηση 2 s και μετά καταγραφή 12 s."
            : $"Κρατήστε σταθερή την επαφή επιπέδου {level}. Σταθεροποίηση 2 s και μετά καταγραφή 12 s.";
        timer.Start();
    }

    private void SerialClientOnMessageReceived(TouchStateMessage message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SerialClientOnMessageReceived(message));
            return;
        }

        liveValueLabel.Text =
            $"Τρέχουσα smooth τιμή: {message.SmoothValue:N0}  •  " +
            $"firmware level: {message.ParticipationLevel}";
        if (capturingLevel is int level && DateTime.UtcNow >= captureStartsAt)
        {
            pendingSamples.Add(message.SmoothValue);
        }
    }

    private void TimerOnTick(object? sender, EventArgs e)
    {
        var remaining = captureEndsAt - DateTime.UtcNow;
        progress.Value = Math.Clamp(
            progress.Maximum - (int)Math.Max(0, remaining.TotalMilliseconds),
            progress.Minimum,
            progress.Maximum);
        if (!collectionStarted && DateTime.UtcNow >= captureStartsAt)
        {
            collectionStarted = true;
            instructionLabel.Text = capturingLevel == 0
                ? "Καταγραφή χωρίς επαφή σε εξέλιξη — παραμείνετε μακριά από την επιφάνεια."
                : $"Καταγραφή επιπέδου {capturingLevel} σε εξέλιξη — διατηρήστε σταθερή επαφή.";
        }
        if (remaining > TimeSpan.Zero)
        {
            return;
        }

        timer.Stop();
        if (capturingLevel is not int completedLevel)
        {
            return;
        }
        capturingLevel = null;
        cancelCaptureButton.Enabled = false;
        SetCaptureButtonsEnabled(true);
        SetResetButtonsEnabled(true);
        ShowStageResult(completedLevel);
        ValidateCompleteProfile();
    }

    private void ShowStageResult(int level)
    {
        try
        {
            var summary = TouchCalibrationCalculator.SummarizeAnyLevel(
                level,
                pendingSamples);
            samplesByLevel[level].Clear();
            samplesByLevel[level].AddRange(pendingSamples);
            summariesByLevel[level] = summary;
            changedLevels.Add(level);
            rangeLabels[level].Text =
                $"min {summary.Minimum:N0}  •  median {summary.Median:N0}  •  max {summary.Maximum:N0}";
            rangeLabels[level].ForeColor = Color.FromArgb(16, 124, 65);
            resetButtons[level].Enabled = true;
            instructionLabel.Text =
                $"Το επίπεδο {level} καταγράφηκε. Μπορείτε να συνεχίσετε ή να επαναλάβετε οποιαδήποτε μέτρηση.";
        }
        catch (Exception exception)
        {
            if (summariesByLevel[level] is { } previous)
            {
                rangeLabels[level].Text =
                    $"min {previous.Minimum:N0} • median {previous.Median:N0} • max {previous.Maximum:N0} (διατηρήθηκε)";
                rangeLabels[level].ForeColor = Color.FromArgb(157, 93, 0);
            }
            else
            {
                rangeLabels[level].Text = $"Απορρίφθηκε: {exception.Message}";
                rangeLabels[level].ForeColor = Color.FromArgb(196, 43, 28);
            }
            instructionLabel.Text =
                "Η αποτυχημένη μέτρηση δεν αντικατέστησε προηγούμενα δεδομένα. Επαναλάβετε όταν είστε έτοιμοι.";
        }
        finally
        {
            pendingSamples.Clear();
        }
    }

    private void CancelCurrentCapture()
    {
        if (capturingLevel is not int level)
        {
            return;
        }

        timer.Stop();
        capturingLevel = null;
        collectionStarted = false;
        pendingSamples.Clear();
        progress.Value = 0;
        cancelCaptureButton.Enabled = false;
        SetCaptureButtonsEnabled(true);
        SetResetButtonsEnabled(true);
        instructionLabel.Text =
            $"Η νέα μέτρηση επιπέδου {level} ακυρώθηκε. Τυχόν προηγούμενη έγκυρη μέτρηση διατηρήθηκε.";
        ValidateCompleteProfile();
    }

    private void ResetLevel(int level)
    {
        samplesByLevel[level].Clear();
        summariesByLevel[level] = null;
        changedLevels.Add(level);
        rangeLabels[level].Text = "Δεν έχει καταγραφεί";
        rangeLabels[level].ForeColor = Color.FromArgb(96, 96, 96);
        resetButtons[level].Enabled = false;
        instructionLabel.Text = $"Η καταγραφή επιπέδου {level} μηδενίστηκε.";
        ValidateCompleteProfile();
    }

    private void ValidateCompleteProfile()
    {
        if (summariesByLevel[0] is null || summariesByLevel[1] is null)
        {
            ResultProfile = null;
            saveButton.Enabled = false;
            validationLabel.Text = "Απαιτούνται τουλάχιστον οι καταγραφές 0 και 1.";
            validationLabel.ForeColor = Color.FromArgb(96, 96, 96);
            return;
        }

        var highestCapturedLevel = Array.FindLastIndex(
            summariesByLevel,
            summary => summary is not null);
        var firstGap = summariesByLevel
            .Take(highestCapturedLevel + 1)
            .Select((summary, level) => (summary, level))
            .FirstOrDefault(item => item.summary is null);
        if (firstGap.summary is null && firstGap.level > 0)
        {
            ResultProfile = null;
            saveButton.Enabled = false;
            validationLabel.Text =
                $"Λείπει το επίπεδο {firstGap.level}. Οι καταγραφές πρέπει να είναι συνεχόμενες από το 0.";
            validationLabel.ForeColor = Color.FromArgb(196, 43, 28);
            return;
        }

        try
        {
            ResultProfile = CreateResultProfile(highestCapturedLevel);
            var hasChanges = changedLevels.Count > 0 || openedSavedProfile;
            validationLabel.Text = hasChanges
                ? $"Έγκυρο draft επιπέδων 0–{highestCapturedLevel} • thresholds: " +
                  $"{string.Join(" / ", ResultProfile.Thresholds.Select(value => value.ToString("N0")))}\r\n" +
                  $"Hysteresis: {ResultProfile.Hysteresis:N0} • επίπεδα {highestCapturedLevel + 1}–7 ανενεργά"
                : $"Φορτώθηκε το αποθηκευμένο profile επιπέδων 0–{highestCapturedLevel}. " +
                  "Δεν υπάρχουν μη αποθηκευμένες αλλαγές.";
            validationLabel.ForeColor = Color.FromArgb(16, 124, 65);
            saveButton.Enabled = hasChanges;
        }
        catch (Exception exception)
        {
            ResultProfile = null;
            saveButton.Enabled = false;
            validationLabel.Text = $"Το profile δεν είναι ασφαλές: {exception.Message}";
            validationLabel.ForeColor = Color.FromArgb(196, 43, 28);
        }
    }

    private TouchCalibrationProfile CreateResultProfile(int highestCapturedLevel) =>
        TouchCalibrationCalculator.CreateProfileFromSummaries(
            nameBox.Text,
            summariesByLevel.Take(highestCapturedLevel + 1)
                .Select(summary => summary!)
                .ToArray(),
            workingProfile,
            changedLevels);

    private void SaveButtonOnClick(object? sender, EventArgs e)
    {
        var highestCapturedLevel = Array.FindLastIndex(
            summariesByLevel,
            summary => summary is not null);
        ResultProfile = CreateResultProfile(highestCapturedLevel);
        if (highestCapturedLevel < activeHighestRecordedLevel)
        {
            var confirmation = MessageBox.Show(
                this,
                $"Η αποθήκευση θα απενεργοποιήσει τα επίπεδα " +
                $"{highestCapturedLevel + 1}–{activeHighestRecordedLevel}.\r\n\r\n" +
                "Θέλετε να συνεχίσετε;",
                "Μείωση ενεργών επιπέδων",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }
        }
        TouchCalibrationProfiles.SaveAndActivate(calibrationDirectory, ResultProfile);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetCaptureButtonsEnabled(bool enabled)
    {
        foreach (var button in captureButtons)
        {
            button.Enabled = enabled;
        }
    }

    private void SetResetButtonsEnabled(bool enabled)
    {
        for (var level = 0; level < resetButtons.Length; level++)
        {
            resetButtons[level].Enabled = enabled && summariesByLevel[level] is not null;
        }
    }

    private void LoadProfile(TouchCalibrationProfile profile, bool openedFromLibrary)
    {
        updatingProfileUi = true;
        foreach (var samples in samplesByLevel)
        {
            samples.Clear();
        }
        Array.Clear(summariesByLevel);
        changedLevels.Clear();
        workingProfile = profile;
        openedSavedProfile = openedFromLibrary;
        for (var level = 0; level <= 7; level++)
        {
            rangeLabels[level].Text = "Δεν έχει καταγραφεί";
            rangeLabels[level].ForeColor = Color.FromArgb(96, 96, 96);
            resetButtons[level].Enabled = false;
        }

        var summaries = TouchCalibrationCalculator.GetRecordedLevelSummaries(profile);
        foreach (var summary in summaries.Where(summary => summary.Level is >= 0 and <= 7))
        {
            summariesByLevel[summary.Level] = summary;
            rangeLabels[summary.Level].Text =
                $"Αποθηκευμένο • min {summary.Minimum:N0} • median {summary.Median:N0} • max {summary.Maximum:N0}";
            rangeLabels[summary.Level].ForeColor = Color.FromArgb(16, 124, 65);
            resetButtons[summary.Level].Enabled = true;
        }
        var highestRecordedLevel = summaries.Count == 0 ? -1 : summaries.Max(summary => summary.Level);
        if (!openedFromLibrary)
        {
            activeHighestRecordedLevel = highestRecordedLevel;
        }
        nameBox.Text = profile.Name;
        updatingProfileUi = false;
        instructionLabel.Text = summaries.Count == 0
            ? "Δεν υπάρχουν αποθηκευμένες περιοχές. Ξεκινήστε από το επίπεδο 0."
            : $"Φορτώθηκαν οι αποθηκευμένες περιοχές 0–{highestRecordedLevel}. " +
              "Μπορείτε να επεκτείνετε ή να επαναλάβετε συγκεκριμένη μέτρηση.";
        ValidateCompleteProfile();
    }

    private void RefreshSavedProfiles()
    {
        savedProfileCombo.Items.Clear();
        var catalog = TouchCalibrationProfiles.DiscoverSaved(calibrationDirectory);
        foreach (var saved in catalog.Profiles)
        {
            savedProfileCombo.Items.Add(new SavedProfileItem(saved));
        }
        if (savedProfileCombo.Items.Count > 0)
        {
            savedProfileCombo.SelectedIndex = 0;
        }
        openProfileButton.Enabled = savedProfileCombo.SelectedItem is SavedProfileItem;
        if (catalog.Issues.Count > 0)
        {
            validationLabel.Text =
                $"Αγνοήθηκαν {catalog.Issues.Count} μη έγκυρα αρχεία βαθμονόμησης.";
            validationLabel.ForeColor = Color.FromArgb(157, 93, 0);
        }
    }

    private void OpenSelectedProfile()
    {
        if (savedProfileCombo.SelectedItem is not SavedProfileItem selected)
        {
            return;
        }

        LoadProfile(selected.Saved.Profile, openedFromLibrary: true);
        instructionLabel.Text =
            $"Άνοιξε το profile «{selected.Saved.Profile.Name}». " +
            "Το ενεργό profile δεν αλλάζει πριν πατήσετε Αποθήκευση.";
    }

    private sealed record SavedProfileItem(SavedTouchCalibrationProfile Saved)
    {
        public override string ToString() =>
            $"{Saved.Profile.Name} • {Saved.Profile.CreatedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
    }
}
