namespace Synapse.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var options = AppOptions.Parse(args);
        if (options.CalibrationPreviewPath is not null)
        {
            RenderCalibrationPreview(options);
            return;
        }
        using var form = new MainForm(options);
        Application.Run(form);
    }

    private static void RenderCalibrationPreview(AppOptions options)
    {
        using var serialClient = new Synapse.Input.SerialTouchClient();
        var profile = Synapse.Input.TouchCalibrationProfiles.LoadActiveOrDefault(
            options.CalibrationDirectory);
        using var form = new UnifiedCalibrationForm(
            serialClient,
            profile,
            options.CalibrationDirectory);
        form.Show();
        Application.DoEvents();
        var directory = Path.GetDirectoryName(options.CalibrationPreviewPath!);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        form.DrawToBitmap(bitmap, form.ClientRectangle);
        bitmap.Save(options.CalibrationPreviewPath!, System.Drawing.Imaging.ImageFormat.Png);
        form.Close();
    }
}

internal sealed record AppOptions(
    string CompositionRoot,
    string CalibrationDirectory,
    string RuntimeDataDirectory,
    string? PreviewPath,
    string? CalibrationPreviewPath,
    bool TechnicalPreview,
    int? PreviewWidth,
    int? PreviewHeight)
{
    public static AppOptions Parse(string[] args)
    {
        string? compositionRoot = null;
        string? calibrationDirectory = null;
        string? runtimeDataDirectory = null;
        string? previewPath = null;
        string? calibrationPreviewPath = null;
        var technicalPreview = false;
        int? previewWidth = null;
        int? previewHeight = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--compositions" when index + 1 < args.Length:
                    compositionRoot = args[++index];
                    break;
                case "--samples" when index + 1 < args.Length:
                    // Backward-compatible single-composition mode.
                    compositionRoot = args[++index];
                    break;
                case "--render-preview" when index + 1 < args.Length:
                    previewPath = args[++index];
                    break;
                case "--calibrations" when index + 1 < args.Length:
                    calibrationDirectory = args[++index];
                    break;
                case "--render-calibration-preview" when index + 1 < args.Length:
                    calibrationPreviewPath = args[++index];
                    break;
                case "--data" when index + 1 < args.Length:
                case "--runtime" when index + 1 < args.Length:
                    runtimeDataDirectory = args[++index];
                    break;
                case "--technical-preview":
                    technicalPreview = true;
                    break;
                case "--preview-width" when index + 1 < args.Length &&
                    int.TryParse(args[index + 1], out var parsedWidth):
                    previewWidth = parsedWidth;
                    index++;
                    break;
                case "--preview-height" when index + 1 < args.Length &&
                    int.TryParse(args[index + 1], out var parsedHeight):
                    previewHeight = parsedHeight;
                    index++;
                    break;
            }
        }

        if (previewWidth is not null && previewWidth is < 1100 or > 3840)
        {
            throw new ArgumentOutOfRangeException(nameof(previewWidth));
        }
        if (previewHeight is not null && previewHeight is < 700 or > 2160)
        {
            throw new ArgumentOutOfRangeException(nameof(previewHeight));
        }

        compositionRoot ??= Path.Combine(AppContext.BaseDirectory, "compositions");
        calibrationDirectory ??= Path.Combine(AppContext.BaseDirectory, "data", "calibrations");
        runtimeDataDirectory ??= Path.Combine(AppContext.BaseDirectory, "data", "runtime");
        return new AppOptions(
            Path.GetFullPath(compositionRoot),
            Path.GetFullPath(calibrationDirectory),
            Path.GetFullPath(runtimeDataDirectory),
            previewPath is null ? null : Path.GetFullPath(previewPath),
            calibrationPreviewPath is null ? null : Path.GetFullPath(calibrationPreviewPath),
            technicalPreview,
            previewWidth,
            previewHeight);
    }
}

