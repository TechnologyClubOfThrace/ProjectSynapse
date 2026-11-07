namespace Synapse.Input;

public sealed class TouchLevelClassifier
{
    private TouchCalibrationProfile profile;
    private int currentLevel;

    public TouchLevelClassifier(TouchCalibrationProfile profile)
    {
        TouchCalibrationProfiles.Validate(profile);
        this.profile = profile;
    }

    public TouchCalibrationProfile Profile => profile;
    public int CurrentLevel => currentLevel;

    public int Update(uint smoothValue)
    {
        while (currentLevel < profile.Thresholds.Count &&
               smoothValue >= profile.Thresholds[currentLevel] + profile.Hysteresis)
        {
            currentLevel++;
        }
        while (currentLevel > 0 &&
               smoothValue < profile.Thresholds[currentLevel - 1] - profile.Hysteresis)
        {
            currentLevel--;
        }
        return currentLevel;
    }

    public void ReplaceProfile(TouchCalibrationProfile replacement)
    {
        TouchCalibrationProfiles.Validate(replacement);
        profile = replacement;
        currentLevel = 0;
    }

    public void Reset() => currentLevel = 0;
}
