namespace NuraLib.Devices;

/// <summary>
/// Describes the supported user-facing immersion slider positions.
/// </summary>
public enum NuraImmersionLevel {
    Negative2 = -2,
    Negative1 = -1,
    Neutral = 0,
    Positive1 = 1,
    Positive2 = 2,
    Positive3 = 3,
    Positive4 = 4
}

/// <summary>
/// Provides conversion helpers between user-facing immersion levels and the raw protocol index.
/// </summary>
public static class NuraImmersionLevelExtensions {
    /// <summary>
    /// Converts a user-facing immersion level to the raw protocol index used by the headset.
    /// </summary>
    public static int ToRawIndex(this NuraImmersionLevel level) => (int)level + 2;

    /// <summary>
    /// Attempts to map a raw protocol immersion index to a user-facing level.
    /// </summary>
    public static bool TryFromRawIndex(int rawIndex, out NuraImmersionLevel level) {
        switch (rawIndex) {
            case 0:
                level = NuraImmersionLevel.Negative2;
                return true;
            case 1:
                level = NuraImmersionLevel.Negative1;
                return true;
            case 2:
                level = NuraImmersionLevel.Neutral;
                return true;
            case 3:
                level = NuraImmersionLevel.Positive1;
                return true;
            case 4:
                level = NuraImmersionLevel.Positive2;
                return true;
            case 5:
                level = NuraImmersionLevel.Positive3;
                return true;
            case 6:
                level = NuraImmersionLevel.Positive4;
                return true;
            default:
                level = default;
                return false;
        }
    }

    /// <summary>
    /// Converts a raw protocol immersion index to a user-facing level.
    /// </summary>
    public static NuraImmersionLevel FromRawIndex(int rawIndex) {
        if (!TryFromRawIndex(rawIndex, out var level)) {
            throw new ArgumentOutOfRangeException(nameof(rawIndex), rawIndex, "Immersion level raw index must be between 0 and 6.");
        }

        return level;
    }
}
