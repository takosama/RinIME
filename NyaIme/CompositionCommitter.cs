namespace NyaIme;

internal interface ICompositionEditor
{
    bool BeginBatchEdit();
    bool EndBatchEdit();
    bool FinishComposingText();
    bool SetSelection(int start, int end);
    bool CommitText(string text);
    bool SetComposingRegion(int start, int end);
}

internal static class CompositionCommitter
{
    public enum EnterAction { Failed, ConfirmedRaw, SendEnter }
    public static EnterAction FinishRaw(CompositionRange range, ICompositionEditor editor, bool hadDraft)
    {
        bool pending = range.Active || hadDraft;
        // The text is already in the editor. Finalize the marker without replacing
        // text or moving the caret/selection, including after partial confirmation.
        if (!editor.FinishComposingText()) return EnterAction.Failed;
        range.Reset();
        return pending ? EnterAction.ConfirmedRaw : EnterAction.SendEnter;
    }
    public sealed record Result(bool Committed, bool ComposingRestored);
    public static bool Restore(CompositionRange range, ICompositionEditor editor)
    {
        var span = range.NativeComposingSpan;
        return span is null ? editor.FinishComposingText() : editor.SetComposingRegion(span.Start, span.End);
    }
    public static Result Commit(CompositionRange range, CompositionRange.Target target, string candidate, ICompositionEditor editor)
    {
        if (!range.IsCurrent(target)) return new(false, false);
        int selectionStart = range.SelectionStart, selectionEnd = range.SelectionEnd;
        editor.BeginBatchEdit();
        try
        {
            // commitText replaces the composing region even when a smaller selection exists.
            // Detach it first, replace only the target, then mark the remaining reading again.
            if (!editor.FinishComposingText()) return new(false, false);
            if (!editor.SetSelection(target.Start, target.End)) return new(false, Restore(range, editor));
            if (!editor.CommitText(candidate))
            {
                editor.SetSelection(selectionStart, selectionEnd);
                return new(false, Restore(range, editor));
            }
            range.Confirm(target, candidate);
            bool restored = Restore(range, editor);
            editor.SetSelection(range.SelectionStart, range.SelectionEnd);
            return new(true, restored);
        }
        finally { editor.EndBatchEdit(); }
    }
}
