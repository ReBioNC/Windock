namespace Windock.Core.Input;

/// Values match Win32 MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN.
[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// A global hotkey such as "Ctrl+Alt+Shift+W": modifiers (at least minModifiers) plus A–Z, 0–9, F1–F24 or Space.
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly (HotkeyModifiers Flag, string Name)[] CanonicalOrder =
        { (HotkeyModifiers.Ctrl, "Ctrl"), (HotkeyModifiers.Alt, "Alt"), (HotkeyModifiers.Shift, "Shift"), (HotkeyModifiers.Win, "Win") };

    public const int VkSpace = 0x20;

    public static bool TryParse(string text, out HotkeyGesture gesture, int minModifiers = 2)
    {
        gesture = default;
        var mods = HotkeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim().ToLowerInvariant();
            var mod = part switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (mod != HotkeyModifiers.None)
            {
                if (mods.HasFlag(mod)) return false;
                mods |= mod;
            }
            else if (key is null && ParseKey(part) is { } vk)
            {
                key = vk;
            }
            else
            {
                return false;
            }
        }
        if (key is null || CountBits((int)mods) < Math.Max(1, minModifiers)) return false;
        gesture = new HotkeyGesture(mods, key.Value);
        return true;
    }

    public override string ToString()
    {
        var mods = Modifiers;
        var parts = CanonicalOrder.Where(m => mods.HasFlag(m.Flag)).Select(m => m.Name).ToList();
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    private static int? ParseKey(string part)
    {
        if (part.Length == 1 && part[0] is >= 'a' and <= 'z') return 0x41 + (part[0] - 'a');
        if (part.Length == 1 && part[0] is >= '0' and <= '9') return 0x30 + (part[0] - '0');
        if (part == "space") return VkSpace;
        if (part.Length >= 2 && part[0] == 'f' && int.TryParse(part[1..], out var n) && n is >= 1 and <= 24) return 0x70 + n - 1;
        return null;
    }

    private static string KeyName(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1),
        VkSpace => "Space",
        _ => "0x" + vk.ToString("X2"),
    };

    private static int CountBits(int v) => System.Numerics.BitOperations.PopCount((uint)v);
}
