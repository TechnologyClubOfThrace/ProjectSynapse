using Synapse.Input;

namespace Synapse.App;

internal sealed class CalibrationForm : Form
{
    private static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(12);
    private readonly SerialTouchClient serialClient;
    private readonly TouchCalibrationProfile templateProfile;
    private readonly string calibrationDirectory;
    private readonly List<uint> baselineSamples = [];
    private readonly List<uint> contactSamples = [];
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly Label instructionLabel = new();
    private readonly Label liveValueLabel = new();
    private readonly Label resultLabel = new();
    private readonly ProgressBar progress = new();
    private readonly Button baselineButton = new();
    private readonly Button contactButton = new();
    private readonly Button saveButton = new();
    private readonly TextBox nameBox = new();
    private DateTime captureEndsAt;
    private CaptureStage captureStage;

    public CalibrationForm(
        SerialTouchClient serialClient,
        TouchCalibrationProfile templateProfile,
        string calibrationDirectory)
    {
        this.serialClient = serialClient;
        this.templateProfile = templateProfile;
        this.calibrationDirectory = calibrationDirectory;
        BuildInterface();
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
        Text = "Βαθμονόμηση αφής Synapse";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 430);
        Font = new Font("Segoe UI", 9F);

        var heading = new Label
        {
            Text = "Βαθμονόμηση δοκιμαστικής επιφάνειας",
            Location = new Point(24, 20),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold)
        };
        Controls.Add(heading);

        instructionLabel.Text =
            "Ο ήχος παραμένει απενεργοποιημένος. Καταγράψτε πρώτα χωρίς επαφή και μετά με σταθερή επαφή.";
        instructionLabel.Location = new Point(26, 62);
        instructionLabel.Size = new Size(565, 42);
        Controls.Add(instructionLabel);

        liveValueLabel.Text = "Τρέχουσα smooth τιμή: αναμονή...";
        liveValueLabel.Location = new Point(26, 110);
        liveValueLabel.AutoSize = true;
        Controls.Add(liveValueLabel);

        progress.Location = new Point(26, 140);
        progress.Size = new Size(565, 18);
        progress.Maximum = (int)CaptureDuration.TotalMilliseconds;
        Controls.Add(progress);

        baselineButton.Text = "1. Καταγραφή χωρίς επαφή";
        baselineButton.Location = new Point(26, 177);
        baselineButton.Size = new Size(260, 34);
        baselineButton.Click += (_, _) => StartCapture(CaptureStage.Baseline);
        Controls.Add(baselineButton);

        contactButton.Text = "2. Καταγραφή με επαφή";
        contactButton.Location = new Point(331, 177);
        contactButton.Size = new Size(260, 34);
        contactButton.Enabled = false;
        contactButton.Click += (_, _) => StartCapture(CaptureStage.Contact);
        Controls.Add(contactButton);

        resultLabel.Text = "Δεν υπάρχουν ακόμη αρκετά δεδομένα.";
        resultLabel.Location = new Point(26, 228);
        resultLabel.Size = new Size(565, 72);
        Controls.Add(resultLabel);

        var nameLabel = new Label
        {
            Text = "Όνομα profile",
            Location = new Point(26, 310),
            AutoSize = true
        };
        Controls.Add(nameLabel);
        nameBox.Text = "test-aluminium-foil";
        nameBox.Location = new Point(26, 332);
        nameBox.Size = new Size(360, 25);
        Controls.Add(nameBox);

        saveButton.Text = "Αποθήκευση και ενεργοποίηση";
        saveButton.Location = new Point(400, 329);
        saveButton.Size = new Size(191, 32);
        saveButton.Enabled = false;
        saveButton.Click += SaveButtonOnClick;
        Controls.Add(saveButton);

        var cancelButton = new Button
        {
            Text = "Ακύρωση",
            Location = new Point(481, 382),
            Size = new Size(110, 30),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(cancelButton);
        CancelButton = cancelButton;
    }

    private void StartCapture(CaptureStage stage)
    {
        captureStage = stage;
        if (stage == CaptureStage.Baseline)
        {
            baselineSamples.Clear();
            instructionLabel.Text = "Μην αγγίζετε την επιφάνεια μέχρι να ολοκληρωθεί η μπάρα.";
        }
        else
        {
            contactSamples.Clear();
            instructionLabel.Text = "Κρατήστε σταθερή επαφή μέχρι να ολοκληρωθεί η μπάρα.";
        }
        progress.Value = 0;
        captureEndsAt = DateTime.UtcNow + CaptureDuration;
        baselineButton.Enabled = false;
        contactButton.Enabled = false;
        saveButton.Enabled = false;
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
            $"Τρέχουσα smooth τιμή: {message.SmoothValue:N0}  •  firmware level: {message.ParticipationLevel}";
        if (captureStage == CaptureStage.Baseline)
        {
            baselineSamples.Add(message.SmoothValue);
        }
        else if (captureStage == CaptureStage.Contact)
        {
            contactSamples.Add(message.SmoothValue);
        }
    }

    private void TimerOnTick(object? sender, EventArgs e)
    {
        var remaining = captureEndsAt - DateTime.UtcNow;
        progress.Value = Math.Clamp(
            progress.Maximum - (int)Math.Max(0, remaining.TotalMilliseconds),
            progress.Minimum,
            progress.Maximum);
        if (remaining > TimeSpan.Zero)
        {
            return;
        }

        timer.Stop();
        var completedStage = captureStage;
        captureStage = CaptureStage.None;
        if (completedStage == CaptureStage.Baseline)
        {
            contactButton.Enabled = baselineSamples.Count >= 10;
            baselineButton.Enabled = true;
            instructionLabel.Text = baselineSamples.Count >= 10
                ? "Η αδρανής κατάσταση καταγράφηκε. Αγγίξτε σταθερά και ξεκινήστε το βήμα 2."
                : "Λήφθηκαν λίγα packets. Επαναλάβετε την καταγραφή χωρίς επαφή.";
        }
        else
        {
            baselineButton.Enabled = true;
            contactButton.Enabled = true;
            TryCalculatePreview();
        }
    }

    private void TryCalculatePreview()
    {
        try
        {
            var profile = TouchCalibrationCalculator.CreateTwoPointProfile(
                nameBox.Text,
                baselineSamples,
                contactSamples,
                templateProfile);
            ResultProfile = profile;
            resultLabel.Text =
                $"Baseline median: {profile.Baseline!.Median:N0}\r\n" +
                $"Contact median: {profile.Contact!.Median:N0}\r\n" +
                $"Νέο threshold 1: {profile.Thresholds[0]:N0}  •  hysteresis: {profile.Hysteresis:N0}";
            saveButton.Enabled = true;
            instructionLabel.Text = "Η βαθμονόμηση είναι έγκυρη. Ελέγξτε τα αποτελέσματα και αποθηκεύστε.";
        }
        catch (Exception exception)
        {
            ResultProfile = null;
            saveButton.Enabled = false;
            resultLabel.Text = $"Η βαθμονόμηση απορρίφθηκε: {exception.Message}";
        }
    }

    private void SaveButtonOnClick(object? sender, EventArgs e)
    {
        if (ResultProfile is null)
        {
            return;
        }
        ResultProfile = TouchCalibrationCalculator.CreateTwoPointProfile(
            nameBox.Text,
            baselineSamples,
            contactSamples,
            templateProfile);
        TouchCalibrationProfiles.SaveAndActivate(calibrationDirectory, ResultProfile);
        DialogResult = DialogResult.OK;
        Close();
    }

    private enum CaptureStage
    {
        None,
        Baseline,
        Contact
    }
}
