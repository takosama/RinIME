namespace NyaIme;

internal sealed record FlickKeyData(string Center, string? Up, string? Left, string? Right, string? Down)
{
    public string? Resolve(float dx, float dy, float threshold)
    {
        float ax = Math.Abs(dx), ay = Math.Abs(dy);
        if (ax < threshold && ay < threshold) return Center;
        if (ax > ay) return dx < 0 ? Left : Right;
        return dy < 0 ? Up : Down;
    }
}

internal static class AlphabetKeyboardLayout
{
    // O is the vowel key for お in romaji mode. Its previously empty down flick is @.
    public static IReadOnlyList<IReadOnlyList<FlickKeyData>> Rows { get; } = new FlickKeyData[][]
    {
        new[] { new FlickKeyData("A", "X", "C", null, null), new("K", "F", null, "G", null), new("H", "P", null, "B", null) },
        new[] { new FlickKeyData("Y", null, "L", null, null), new("S", null, null, "Z", null), new("T", null, null, "D", null) },
        new[] { new FlickKeyData("I", null, "Q", null, null), new("N", null, null, "M", null), new("R", "J", null, "W", null) },
        new[] { new FlickKeyData("U", null, "V", null, "（"), new("E", null, "！", null, ")"), new("O", null, "？", "ー", "@") }
    };
}
