namespace NyaIme;

internal static class ConversionRouting
{
    internal enum Stage { Local, OnlinePending, Online, LocalFallback }
    // The offline branch never invokes the network delegate.
    public static async Task<IReadOnlyList<string>> ConvertAsync(
        string source, bool offline, IReadOnlyList<string> local,
        Func<IReadOnlyList<string>, bool> changed,
        Func<Func<IReadOnlyList<string>, bool>, CancellationToken, Task<IReadOnlyList<string>>> online,
        CancellationToken token, Action<Stage>? phase = null)
    {
        token.ThrowIfCancellationRequested();
        phase?.Invoke(offline ? Stage.Local : Stage.OnlinePending);
        if (!changed(local) || offline) return local;
        token.ThrowIfCancellationRequested();
        var results = await online(candidates =>
        {
            if (token.IsCancellationRequested) return false;
            phase?.Invoke(Stage.Online);
            return changed(WithOriginal(candidates, source));
        }, token);
        token.ThrowIfCancellationRequested();
        phase?.Invoke(results.Count == 0 ? Stage.LocalFallback : Stage.Online);
        return results.Count == 0 ? local : WithOriginal(results, source);
    }
    private static IReadOnlyList<string> WithOriginal(IReadOnlyList<string> candidates, string source) =>
        candidates.Concat(new[] { source }).Distinct(StringComparer.Ordinal).ToArray();
}
