namespace NyaIme;

internal sealed class AlphabetShiftState
{
    public enum ShiftMode { Off, Once, CapsLock }
    public ShiftMode Mode { get; private set; }

    public void Cycle() => Mode = Mode switch
    {
        ShiftMode.Off => ShiftMode.Once,
        ShiftMode.Once => ShiftMode.CapsLock,
        _ => ShiftMode.Off
    };

    public void Reset() => Mode = ShiftMode.Off;

    // The same mapping drives key labels, flick previews and committed text.
    // Symbols retain their existing spelling and do not use the one-letter shift.
    public string? Display(string? text)
    {
        if (text is null) return null;
        return string.Concat(text.Select(ch => IsLetter(ch)
            ? Mode == ShiftMode.Off ? char.ToLowerInvariant(ch) : char.ToUpperInvariant(ch)
            : ch));
    }

    public bool TryInput(string text, Func<string, bool> commit)
    {
        if (!commit(Display(text)!)) return false;
        if (Mode == ShiftMode.Once && text.Any(IsLetter)) Reset();
        return true;
    }

    private static bool IsLetter(char ch) => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
