namespace NyaIme;

// A pending conversion belongs to a text range, not to the current caret's left side.
internal sealed class CompositionRange
{
    public sealed record Span(int Start, int End);
    private readonly List<Span> _confirmed = new(); // Offsets within Text, excluded from composing.
    private Span? _continuation;
    public int Start { get; private set; } = -1;
    public string Text { get; private set; } = "";
    public int End => Start < 0 ? -1 : Start + Text.Length;
    public int SelectionStart { get; private set; } = -1;
    public int SelectionEnd { get; private set; } = -1;
    public bool Active => Start >= 0 && PendingSpans().Any();
    public bool CursorInside => Active && SelectionStart == SelectionEnd && SelectionStart >= Start && SelectionStart <= End;
    public int LeftLength => CursorInside ? SelectionStart - Start : 0;
    public int RightLength => CursorInside ? End - SelectionStart : 0;
    public int SelectionRevision { get; private set; }
    public int RangeRevision { get; private set; }
    public bool SelectionWithin => Active && Math.Min(SelectionStart, SelectionEnd) >= Start && Math.Max(SelectionStart, SelectionEnd) <= End;

    public sealed record Target(int Start, int End, string Source, int SelectionRevision, int RangeRevision, bool Selected);
    public Target? GetTarget()
    {
        if (!SelectionWithin) return null;
        bool selected = SelectionStart != SelectionEnd;
        var span = _continuation ?? PendingSpans().FirstOrDefault(x => SelectionStart >= x.Start && SelectionStart <= x.End);
        if (!selected && span is null) return null;
        int first = selected ? Math.Min(SelectionStart, SelectionEnd) : span!.Start;
        int last = selected ? Math.Max(SelectionStart, SelectionEnd) : _continuation?.End ?? SelectionStart;
        if (first == last) return null;
        return new(first, last, Text[(first - Start)..(last - Start)], SelectionRevision, RangeRevision, selected);
    }
    public bool IsCurrent(Target target) => GetTarget() == target;
    public Span? NativeComposingSpan => _continuation ?? PendingSpans().FirstOrDefault(x => SelectionStart >= x.Start && SelectionStart <= x.End)
        ?? PendingSpans().FirstOrDefault(x => x.Start >= SelectionStart) ?? PendingSpans().LastOrDefault();

    public IEnumerable<Span> PendingSpans()
    {
        if (Start < 0 || Text.Length == 0) yield break;
        int offset = 0;
        foreach (var gap in _confirmed.OrderBy(x => x.Start))
        {
            if (gap.Start > offset) yield return new(Start + offset, Start + gap.Start);
            offset = Math.Max(offset, gap.End);
        }
        if (offset < Text.Length) yield return new(Start + offset, End);
    }
    public void Confirm(Target target, string candidate)
    {
        if (!IsCurrent(target)) throw new InvalidOperationException("Conversion target changed");
        RangeRevision++;
        int cursor = target.Start + candidate.Length;
        if (target.Start == Start)
        {
            int cut = target.End - Start;
            string remaining = Text[(target.End - Start)..];
            if (remaining.Length == 0) Reset(cursor, cursor);
            else
            {
                var gaps = _confirmed.Where(x => x.End > cut).Select(x => new Span(Math.Max(0, x.Start - cut), x.End - cut)).ToArray();
                _confirmed.Clear(); _confirmed.AddRange(gaps);
                Start = cursor; Text = remaining; MoveSelection(cursor, cursor);
            }
        }
        else
        {
            ApplyEdit(target.Start, target.End, candidate, cursor);
            if (candidate.Length > 0) _confirmed.Add(new(target.Start - Start, target.Start - Start + candidate.Length));
            NormalizeConfirmed();
        }
        if (!Active) Reset(cursor, cursor);
        else _continuation = PendingSpans().FirstOrDefault(x => x.Start >= cursor) ?? PendingSpans().LastOrDefault();
    }

    public void MoveSelection(int start, int end)
    {
        if (SelectionStart != start || SelectionEnd != end) { SelectionRevision++; _continuation = null; }
        SelectionStart = start; SelectionEnd = end;
    }
    public void Reset(int selectionStart = -1, int selectionEnd = -1)
    {
        RangeRevision++;
        _confirmed.Clear(); _continuation = null;
        Start = -1; Text = ""; MoveSelection(selectionStart, selectionEnd);
    }
    public void ApplyEdit(int first, int last, string inserted, int cursor)
    {
        int start = Math.Min(first, last), end = Math.Max(first, last);
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(first));
        RangeRevision++;
        _continuation = null;
        if (Active && start >= Start && end <= End)
        {
            int firstOffset = start - Start, lastOffset = end - Start, delta = inserted.Length - (end - start);
            var gaps = _confirmed.Where(x => x.End <= firstOffset || x.Start >= lastOffset)
                .Select(x => x.Start >= lastOffset ? new Span(x.Start + delta, x.End + delta) : x).ToArray();
            _confirmed.Clear(); _confirmed.AddRange(gaps);
            Text = Text[..(start - Start)] + inserted + Text[(end - Start)..];
        }
        else if (Active && inserted.Length == 0 && end <= Start)
            Start -= end - start; // Deletion immediately before the pending range shifts it.
        else if (!(Active && inserted.Length == 0 && start >= End))
        {
            Start = start; Text = inserted; _confirmed.Clear();
        }
        MoveSelection(cursor, cursor);
        if (Text.Length == 0) Start = -1;
    }
    public bool Matches(string source) => CursorInside && source == Text;

    private void NormalizeConfirmed()
    {
        var gaps = _confirmed.OrderBy(x => x.Start).ToArray();
        _confirmed.Clear();
        foreach (var gap in gaps)
        {
            if (_confirmed.Count > 0 && _confirmed[^1].End >= gap.Start)
                _confirmed[^1] = new(_confirmed[^1].Start, Math.Max(_confirmed[^1].End, gap.End));
            else _confirmed.Add(gap);
        }
    }
}
