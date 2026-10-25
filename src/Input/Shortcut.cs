namespace SteppedOut;

// The keys held with the shortcut's key. The values are the ones RegisterHotKey takes.
[Flags]
enum ShortcutModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
}

// The lock shortcut, such as Ctrl+Alt+L: Ctrl, Alt or both, maybe Shift, and one letter, number or
// F key. The Windows key is never part of it, and a key on its own or with only Shift is not a
// shortcut. It is written the same way in every language, in English and left to right.
readonly record struct Shortcut(ShortcutModifiers Modifiers, Keys Key)
{
    // The one the Extras list offers by name. A new install has no shortcut until one is picked.
    public static Shortcut Default { get; } = new(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, Keys.L);

    // Shortcut text in the order Windows writes it: Ctrl, Alt, Shift, then the key.
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(ShortcutModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    // The text for a label. The marks around it keep it reading left to right inside Arabic text.
    public string Display => Ltr + ToString() + Ltr;

    const string Ltr = "‎";

    public uint VirtualKey => (uint)Key;

    public bool HasCtrl => Modifiers.HasFlag(ShortcutModifiers.Ctrl);

    public bool HasAlt => Modifiers.HasFlag(ShortcutModifiers.Alt);

    public bool HasShift => Modifiers.HasFlag(ShortcutModifiers.Shift);

    public bool IsValid => IsAllowed(Modifiers, Key);

    // Reads text such as "Ctrl+Alt+L", in any case and with spaces around the parts. Gives false for
    // anything that is not a valid shortcut, and for "" (no shortcut).
    public static bool TryParse(string? text, out Shortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Split('+');
        var modifiers = ShortcutModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            ShortcutModifiers modifier = ModifierNamed(parts[i].Trim());
            if (modifier == ShortcutModifiers.None || modifiers.HasFlag(modifier)) return false;
            modifiers |= modifier;
        }
        if (KeyNamed(parts[^1].Trim()) is not Keys key) return false;
        if (!IsAllowed(modifiers, key)) return false;
        shortcut = new Shortcut(modifiers, key);
        return true;
    }

    public static Shortcut? Parse(string? text) => TryParse(text, out Shortcut s) ? s : null;

    // The shortcut text as it is saved: in the usual form, or "" (off) when it is not a valid
    // shortcut.
    public static string Normalize(string? text) => TryParse(text, out Shortcut s) ? s.ToString() : "";

    // The shortcut a key press makes, from KeyEventArgs.KeyData, for a window that asks the person
    // to press one. winHeld says whether a Windows key was down, which WinForms does not report in
    // KeyData. Gives false while only modifiers are down, and for anything that is not a shortcut.
    public static bool TryFromKeys(Keys keyData, out Shortcut shortcut, bool winHeld = false)
    {
        shortcut = default;
        if (winHeld) return false;
        var modifiers = ShortcutModifiers.None;
        if ((keyData & Keys.Control) != 0) modifiers |= ShortcutModifiers.Ctrl;
        if ((keyData & Keys.Alt) != 0) modifiers |= ShortcutModifiers.Alt;
        if ((keyData & Keys.Shift) != 0) modifiers |= ShortcutModifiers.Shift;
        Keys key = keyData & Keys.KeyCode;
        if (!IsAllowed(modifiers, key)) return false;
        shortcut = new Shortcut(modifiers, key);
        return true;
    }

    // Ctrl or Alt must be held: a letter with nothing or only Shift would stop that letter being
    // typed anywhere. Alt+F4 is how Windows closes a window, so it is never taken.
    public static bool IsAllowed(ShortcutModifiers modifiers, Keys key)
    {
        if ((modifiers & ~(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt | ShortcutModifiers.Shift)) != 0) return false;
        if ((modifiers & (ShortcutModifiers.Ctrl | ShortcutModifiers.Alt)) == 0) return false;
        if (!IsAllowedKey(key)) return false;
        return !(modifiers == ShortcutModifiers.Alt && key == Keys.F4);
    }

    // A letter, a number on the main row, or F1 to F24.
    public static bool IsAllowedKey(Keys key) =>
        key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or >= Keys.F1 and <= Keys.F24;

    static ShortcutModifiers ModifierNamed(string name) => name.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => ShortcutModifiers.Ctrl,
        "ALT" => ShortcutModifiers.Alt,
        "SHIFT" => ShortcutModifiers.Shift,
        _ => ShortcutModifiers.None,
    };

    static Keys? KeyNamed(string name)
    {
        string upper = name.ToUpperInvariant();
        if (upper.Length == 1)
        {
            char c = upper[0];
            if (c is >= 'A' and <= 'Z') return Keys.A + (c - 'A');
            if (c is >= '0' and <= '9') return Keys.D0 + (c - '0');
            return null;
        }
        if (upper.Length is 2 or 3 && upper[0] == 'F' && upper[1] != '0'
            && int.TryParse(upper.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
            && n is >= 1 and <= 24)
        {
            return Keys.F1 + (n - 1);
        }
        return null;
    }

    static string KeyName(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => ((char)('A' + (key - Keys.A))).ToString(),
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.F1 and <= Keys.F24 => "F" + (key - Keys.F1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString(),
    };
}
