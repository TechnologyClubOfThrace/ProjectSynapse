using Synapse.Input;

namespace Synapse.App;

internal sealed class HigherLevelsCalibrationForm : Form
{
    private static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(12);
    private readonly SerialTouchClient serialClient;
    private readonly TouchCalibrationProfile existingProfile;
    private readonly string calibrationDirectory;
    private readonly List<List<uint>> samplesByLevel = Enumerable.Range(0, 6)
        .Select(_ => new List<uint>())
        .ToList();
    private readonly List<TouchCalibrationLevelSummary> acceptedSummaries = [];
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly Label instructionLabel = new();
    private readonly Label liveValueLabel = new();
    private readonly Label resultLabel = new();
    private readonly ProgressBar progress = new();
    private readonly Button captureButton = new();
    private readonly Button saveButton = new();
    private readonly TextBox nameBox = new();
    private DateTime captureEndsAt;
    private int currentLevel = 2;
    private bool capturing;

    public HigherLevelsCalibrationForm(
        SerialTouchClient serialClient,
        TouchCalibrationProfile existingProfile,
        string calibrationDirectory)
    {
        if (existingProfile.Contact is null)
        {
            throw new InvalidOperationException(
                "Complete the 0–1 calibration before calibrating levels 2–7.");
        }

        this.serialClient = serialClient;
        this.existingProfile = existingProfile;
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
        Text = "Βαθμονόμηση επιπέδων 2–7";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(680, 480);
        Font = new Font("Segoe UI", 9F);

        Controls.Add(new Label
        {
            Text = "Πλήρης βαθμονόμηση συμμετοχής",
            Location = new Point(24, 20),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold)
        });

        instructionLabel.Text =
            "Το εγκεκριμένο όριο 0→1 δεν αλλάζει. Προετοιμάστε δύο σταθερές, " +
            "ταυτόχρονες επαφές για το επίπεδο 2.";
        instructionLabel.Location = new Point(26, 62);
        instructionLabel.Size = new Size(625, 54);
        Controls.Add(instructionLabel);

        liveValueLabel.Text = "Τρέχουσα smooth τιμή: αναμονή...";
        liveValueLabel.Location = new Point(26, 120);
        liveValueLabel.AutoSize = true;
        Controls.Add(liveValueLabel);

        progress.Location = new Point(26, 151);
        progress.Size = new Size(625, 18);
        progress.Maximum = (int)CaptureDuration.TotalMilliseconds;
        Controls.Add(progress);

        captureButton.Text = "Καταγραφή επιπέδου 2";
        captureButton.Location = new Point(26, 190);
        captureButton.Size = new Size(625, 38);
        captureButton.Click += (_, _) => StartCapture();
        Controls.Add(captureButton);

        resultLabel.Text =
            $"Ενεργό threshold 0→1: {existingProfile.Thresholds[0]:N0}  •  " +
            $"hysteresis: {existingProfile.Hysteresis:N0}\r\n" +
            "Δεν έχει καταγραφεί ακόμη ανώτερο επίπεδο.";
        resultLabel.Location = new Point(26, 249);
        resultLabel.Size = new Size(625, 92);
        Controls.Add(resultLabel);

        Controls.Add(new Label
        {
            Text = "Όνομα νέου πλήρους profile",
            Location = new Point(26, 353),
            AutoSize = true
        });
        nameBox.Text = $"{existingProfile.Name}-levels-2-7";
        nameBox.Location = new Point(26, 375);
        nameBox.Size = new Size(390, 25);
        Controls.Add(nameBox);

        saveButton.Text = "Αποθήκευση και ενεργοποίηση";
        saveButton.Location = new Point(430, 372);
        saveButton.Size = new Size(221, 32);
        saveButton.Enabled = false;
        saveButton.Click += SaveButtonOnClick;
        Controls.Add(saveButton);

        var cancelButton = new Button
        {
            Text = "Ακύρωση",
            Location = new Point(541, 430),
            Size = new Size(110, 30),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(cancelButton);
        CancelButton = cancelButton;
    }

    private void StartCapture()
    {
        var samples = samplesByLevel[currentLevel - 2];
        samples.Clear();
        capturing = true;
        captureEndsAt = DateTime.UtcNow + CaptureDuration;
        progress.Value = 0;
        captureButton.Enabled = false;
        saveButton.Enabled = false;
        instructionLabel.Text =
            $"Κρατήστε σταθερές {currentLevel} αντιπροσωπευτικές επαφές " +
            "μέχρι να ολοκληρωθεί η μπάρα.";
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
        if (capturing)
        {
            samplesByLevel[currentLevel - 2].Add(message.SmoothValue);
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
        capturing = false;
        ValidateCompletedStage();
    }

    private void ValidateCompletedStage()
    {
        try
        {
            var samples = samplesByLevel[currentLevel - 2];
            var summary = TouchCalibrationCalculator.SummarizeLevel(currentLevel, samples);
            var previousUpperBoundary = acceptedSummaries.Count == 0
                ? existingProfile.Contact!.Maximum
                : acceptedSummaries[^1].UpperBoundary;
            TouchCalibrationCalculator.ValidateHigherLevelTransition(
                previousUpperBoundary,
                summary,
                existingProfile.Hysteresis);
            acceptedSummaries.Add(summary);

            resultLabel.Text =
                $"Επίπεδο {summary.Level}: median {summary.Median:N0}  •  " +
                $"ασφαλές εύρος {summary.LowerBoundary:N0}–{summary.UpperBoundary:N0}\r\n" +
                $"Καταγράφηκαν {acceptedSummaries.Count} από 6 ανώτερα επίπεδα.";

            if (currentLevel == 7)
            {
                ResultProfile = CreateResultProfile();
                captureButton.Text = "Η καταγραφή ολοκληρώθηκε";
                captureButton.Enabled = false;
                saveButton.Enabled = true;
                instructionLabel.Text =
                    "Και τα έξι επίπεδα διαχωρίζονται με ασφάλεια. " +
                    "Το ενεργό profile δεν αλλάζει μέχρι να πατήσετε αποθήκευση.";
                return;
            }

            currentLevel++;
            captureButton.Text = $"Καταγραφή επιπέδου {currentLevel}";
            captureButton.Enabled = true;
            instructionLabel.Text =
                $"Το επίπεδο {currentLevel - 1} έγινε δεκτό. Προετοιμάστε " +
                $"{currentLevel} σταθερές, ταυτόχρονες επαφές.";
        }
        catch (Exception exception)
        {
            samplesByLevel[currentLevel - 2].Clear();
            captureButton.Text = $"Επανάληψη επιπέδου {currentLevel}";
            captureButton.Enabled = true;
            resultLabel.Text = $"Το επίπεδο {currentLevel} απορρίφθηκε: {exception.Message}";
            instructionLabel.Text =
                "Η μέτρηση δεν άλλαξε το ενεργό profile. " +
                "Βελτιώστε τη διάκριση της επαφής και επαναλάβετε.";
        }
    }

    private TouchCalibrationProfile CreateResultProfile() =>
        TouchCalibrationCalculator.CreateHigherLevelProfile(
            nameBox.Text,
            existingProfile,
            samplesByLevel.Select(samples => (IReadOnlyCollection<uint>)samples).ToArray());

    private void SaveButtonOnClick(object? sender, EventArgs e)
    {
        ResultProfile = CreateResultProfile();
        TouchCalibrationProfiles.SaveAndActivate(calibrationDirectory, ResultProfile);
        DialogResult = DialogResult.OK;
        Close();
    }
}
