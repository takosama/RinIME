using NyaIme;

// Models Android's important rule: commitText prefers a composing span over selection.
internal sealed class NativeEditorMock(string text, int start, int end) : ICompositionEditor
{
    public string Text { get; private set; } = text;
    public int SelectionStart { get; private set; } = start;
    public int SelectionEnd { get; private set; } = end;
    public int ComposingStart { get; private set; } = -1;
    public int ComposingEnd { get; private set; } = -1;
    public int BatchDepth { get; private set; }
    public bool RejectRegion { get; set; }
    public bool RejectCommit { get; set; }
    public List<string> Operations { get; } = new();
    public bool BeginBatchEdit() { BatchDepth++; Operations.Add("begin"); return true; }
    public bool EndBatchEdit() { BatchDepth--; Operations.Add("end"); return true; }
    public bool FinishComposingText() { ComposingStart = ComposingEnd = -1; Operations.Add("finish"); return true; }
    public bool SetSelection(int first, int last)
    {
        if (first < 0 || last < 0 || first > Text.Length || last > Text.Length) return false;
        SelectionStart = first; SelectionEnd = last; Operations.Add($"select:{first}:{last}"); return true;
    }
    public bool CommitText(string value)
    {
        if (RejectCommit) return false;
        int first = ComposingStart >= 0 ? ComposingStart : Math.Min(SelectionStart, SelectionEnd);
        int last = ComposingEnd >= 0 ? ComposingEnd : Math.Max(SelectionStart, SelectionEnd);
        Text = Text[..first] + value + Text[last..];
        SelectionStart = SelectionEnd = first + value.Length;
        ComposingStart = ComposingEnd = -1;
        Operations.Add($"commit:{first}:{last}:{value}"); return true;
    }
    public bool SetComposingRegion(int first, int last)
    {
        if (RejectRegion || first < 0 || last > Text.Length || first >= last) return false;
        ComposingStart = first; ComposingEnd = last;
        Operations.Add($"compose:{first}:{last}"); return true;
    }
}
