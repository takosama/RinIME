using NyaIme;

internal static class KeyboardRegressionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var shift = new AlphabetShiftState();
        check(shift.Mode == AlphabetShiftState.ShiftMode.Off, "keyboard starts in lowercase");
        check(shift.Display(null) is null && shift.Display("") == "", "empty flick stays empty");
        shift.Cycle();
        check(shift.Mode == AlphabetShiftState.ShiftMode.Once, "first Shift enables next capital");
        string entered = "";
        bool Commit(string text) { entered += text; return true; }
        check(shift.TryInput("K", Commit) && entered == "K" && shift.Mode == AlphabetShiftState.ShiftMode.Off,
            "one Shift commits one capital then restores lowercase");
        shift.TryInput("A", Commit);
        check(entered == "Ka", "next letter is lowercase without another Shift");
        shift.Cycle(); shift.Cycle();
        check(shift.Mode == AlphabetShiftState.ShiftMode.CapsLock, "two Shift taps enable Caps Lock");
        shift.TryInput("R", Commit); shift.TryInput("I", Commit); shift.TryInput("N", Commit);
        check(entered == "KaRIN" && shift.Mode == AlphabetShiftState.ShiftMode.CapsLock,
            "Caps Lock persists across consecutive letters");
        shift.Cycle();
        check(shift.Mode == AlphabetShiftState.ShiftMode.Off && shift.Display("A") == "a", "third Shift tap unlocks");
        shift.Cycle();
        check(!shift.TryInput("A", _ => false) && shift.Mode == AlphabetShiftState.ShiftMode.Once,
            "rejected editor commit retains one-letter Shift");
        foreach (string symbol in new[] { "@", "（", "！", ")", "？", "ー", "1", " ", "\n" })
        {
            string accepted = "";
            shift.TryInput(symbol, text => { accepted = text; return true; });
            check(accepted == symbol && shift.Mode == AlphabetShiftState.ShiftMode.Once,
                $"symbol {symbol.Trim()} preserves spelling and pending Shift");
        }
        shift.Reset();
        check(shift.Mode == AlphabetShiftState.ShiftMode.Off, "mode/session reset clears pending Shift");
        shift.Cycle(); shift.Cycle(); shift.Reset();
        check(shift.Mode == AlphabetShiftState.ShiftMode.Off, "mode/session reset also clears Caps Lock");

        var keys = AlphabetKeyboardLayout.Rows.SelectMany(row => row).ToArray();
        check(AlphabetKeyboardLayout.Rows.Count == 4 && AlphabetKeyboardLayout.Rows.All(row => row.Count == 3),
            "existing 4 by 3 alphabet layout retained");
        var directions = new[] { (X: 0f, Y: 0f), (X: 0f, Y: -40f), (X: -40f, Y: 0f), (X: 40f, Y: 0f), (X: 0f, Y: 40f) };
        var letters = keys.SelectMany(key => directions.Select(d => key.Resolve(d.X, d.Y, 28)))
            .Where(text => text is { Length: 1 } && text[0] is >= 'A' and <= 'Z').ToArray();
        check(string.Concat(letters.Order()) == "ABCDEFGHIJKLMNOPQRSTUVWXYZ", "all 26 letters remain assigned exactly once");
        foreach (var mode in Enum.GetValues<AlphabetShiftState.ShiftMode>())
        foreach (string letter in letters!)
        {
            shift.Reset();
            for (int tap = 0; tap < (int)mode; tap++) shift.Cycle();
            string label = shift.Display(letter)!;
            string committed = "";
            shift.TryInput(letter, text => { committed = text; return true; });
            string expected = mode == AlphabetShiftState.ShiftMode.Off ? letter.ToLowerInvariant() : letter;
            check(label == expected && committed == label, $"{mode}: label/preview/commit agree for {letter}");
            check(shift.Mode == (mode == AlphabetShiftState.ShiftMode.Once ? AlphabetShiftState.ShiftMode.Off : mode),
                $"{mode}: expected state after {letter}");
        }
        var oKey = keys.Single(key => key.Center == "O");
        check(oKey.Resolve(0, 40, 28) == "@", "O/お down flick inputs half-width @");
        check(oKey.Resolve(0, 0, 28) == "O" && oKey.Resolve(-40, 0, 28) == "？" && oKey.Resolve(40, 0, 28) == "ー",
            "O center and existing side symbols retained");
        check(oKey.Resolve(0, -40, 28) is null, "unused O up flick stays unused");
        check(oKey.Resolve(27, 27, 28) == "O" && oKey.Resolve(0, 28, 28) == "@",
            "existing flick threshold retained");
        check(oKey.Resolve(40, 40, 28) == "@" && oKey.Resolve(41, 40, 28) == "ー",
            "existing diagonal direction precedence retained");
    }
}
