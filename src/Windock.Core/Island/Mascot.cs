namespace Windock.Core.Island;

/// How Wado, the island's whale, looks right now.
public enum WadoMood { Idle, Music, Happy, Focus, Sleep }

public static class WadoRules
{
    /// Wado sleeps from 23:00 to 06:00 unless something plays.
    public const int SleepFrom = 23, WakeAt = 6;

    /// happy: a charger was plugged in or a device connected a moment ago. Focus beats everything; sleep only
    /// happens when nothing else is going on.
    public static WadoMood Mood(bool musicPlaying, bool focusRunning, bool happy, DateTime now)
    {
        if (focusRunning) return WadoMood.Focus;
        if (happy) return WadoMood.Happy;
        if (musicPlaying) return WadoMood.Music;
        return now.Hour >= SleepFrom || now.Hour < WakeAt ? WadoMood.Sleep : WadoMood.Idle;
    }
}
