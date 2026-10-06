using System.Diagnostics;
using NyaIme;

var timer = Stopwatch.StartNew();
using var stream = File.OpenRead(args[0]);
var engine = new OfflineKanaKanjiEngine(stream);
Console.WriteLine($"Load={timer.ElapsedMilliseconds}ms; heap={GC.GetTotalMemory(true) / 1048576}MiB");
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    passed++; Console.WriteLine($"PASS: {name}");
}
foreach (string reading in new[] { "にほん", "かんじ", "きょうはいいてんきです", "わたしはがっこうにいきます", "とうきょう", "はし", "たべました", "ニホン", "あした、とうきょうにいきます。", "にほん https://example.com/a 😀 123", "ゔゔゔゔ", "こんにちは\nせかい" })
{
    timer.Restart();
    var candidates = engine.GetCandidates(reading);
    Console.WriteLine($"{reading} => {string.Join(" | ", candidates)} ({timer.ElapsedMilliseconds}ms)");
    Check(candidates.Contains(reading), $"raw preserved: {reading}");
    Check(candidates.Distinct().Count() == candidates.Count && candidates.Count <= 8, "unique bounded candidates");
}
Check(engine.GetCandidates("にほん").Contains("日本"), "Japan conversion");
Check(engine.GetCandidates("かんじ").Contains("漢字"), "kanji conversion");
Check(engine.GetCandidates("にほん https://example.com/a 😀 123").Any(x => x.StartsWith("日本 ") && x.EndsWith("https://example.com/a 😀 123")), "mixed content preserved");
var before = engine.GetCandidates("かんし");
var after = engine.GetCandidates("かんじ");
Check(after.Contains("漢字") && !before.SequenceEqual(after), "backspace/retype recalculates candidates");
Check(engine.GetCandidates("").Count == 0, "empty input");
string longInput = new('あ', 257);
Check(engine.GetCandidates(longInput).SequenceEqual(new[] { longInput }), "long input bounded and lossless");
var predictions = engine.Predict("とうきょ");
Console.WriteLine($"Prediction: {string.Join(" | ", predictions)}");
Check(predictions.Contains("東京"), "prefix prediction");
using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
try { engine.GetCandidates("にほん", cancellation.Token); throw new Exception("FAIL: cancellation"); }
catch (OperationCanceledException) { Check(true, "cancellation"); }
// Selecting any candidate replaces precisely the source; unselected input stays untouched.
string input = "prefix かんじ suffix";
string selected = after.First(x => x == "漢字");
Check(input[..7] + selected + input[10..] == "prefix 漢字 suffix", "candidate replacement range");
int networkCalls = 0;
Task<IReadOnlyList<string>> FakeNetwork(Func<IReadOnlyList<string>, bool> callback, CancellationToken token)
{
    networkCalls++;
    callback(new[] { "オンライン候補" });
    return Task.FromResult<IReadOnlyList<string>>(new[] { "オンライン候補" });
}
var local = engine.GetCandidates("かんじ");
var offline = await ConversionRouting.ConvertAsync("かんじ", true, local, _ => true, FakeNetwork, CancellationToken.None);
Check(networkCalls == 0 && offline.SequenceEqual(local), "offline route never invokes network");
var streamed = new List<IReadOnlyList<string>>();
var online = await ConversionRouting.ConvertAsync("かんじ", false, local,
    candidates => { streamed.Add(candidates); return true; }, FakeNetwork, CancellationToken.None);
Check(networkCalls == 1 && online.Contains("オンライン候補") && online.Contains("かんじ"), "existing online route and raw fallback");
Check(streamed[0].Contains("漢字") && streamed[^1].Contains("かんじ"), "local candidates precede online stream");
var fallback = await ConversionRouting.ConvertAsync("かんじ", false, local, _ => true,
    (_, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()), CancellationToken.None);
Check(fallback.SequenceEqual(local), "network failure retains offline candidates");
int beforeCalls = networkCalls;
await ConversionRouting.ConvertAsync("かんじ", false, local, _ => false, FakeNetwork, CancellationToken.None);
Check(networkCalls == beforeCalls, "obsolete request stops before network");
var range = new CompositionRange();
range.Reset(7, 7); range.ApplyEdit(7, 7, "にほん", 10);
foreach (int caret in new[] { 9, 8, 7, 8, 9, 10 })
{
    range.MoveSelection(caret, caret);
    Check(range.Matches("にほん") && range.LeftLength + range.RightLength == 3,
        $"arrow/tap movement retains full conversion at {caret}");
    Check(engine.GetCandidates(range.Text).Contains("日本"), "candidates survive caret movement");
}
range.MoveSelection(6, 6);
Check(range.Active && !range.CursorInside && range.Text == "にほん", "outside left suspends without losing text");
range.MoveSelection(11, 11);
Check(range.Active && !range.CursorInside, "outside right suspends without finalizing");
range.MoveSelection(7, 7);
Check(range.Matches("にほん"), "return to beginning resumes full conversion");
range.MoveSelection(8, 8); range.ApplyEdit(8, 8, "っ", 9);
Check(range.Text == "にっほん" && range.End == 11 && range.RightLength == 2, "middle insertion keeps right suffix");
range.ApplyEdit(8, 9, "", 8);
Check(range.Matches("にほん") && range.End == 10, "middle deletion restores entire reading");
range.MoveSelection(10, 8);
Check(range.Active && !range.CursorInside, "reversed selected range pauses candidates");
range.ApplyEdit(10, 8, "っぽん", 11);
Check(range.Text == "にっぽん" && range.End == 11, "replace reversed selection tracks range");
range.MoveSelection(8, 10); range.ApplyEdit(8, 10, "", 8);
Check(range.Text == "にん", "selected deletion removes selected text only");
range.Reset(7, 7); range.ApplyEdit(7, 7, "にほん", 10);
range.ApplyEdit(6, 7, "", 6);
Check(range.Start == 6 && range.Text == "にほん" && range.CursorInside, "backspace at start shifts pending range");
range.MoveSelection(9, 9); range.ApplyEdit(9, 9, "ご", 10);
Check(range.Text == "にほんご", "insertion at end extends range");
range.MoveSelection(7, 7);
string document = "prefixにほんご suffix";
// Range starts at 6; whole-range selection is independent of the caret.
string committed = document[..range.Start] + "日本語" + document[range.End..];
Check(committed == "prefix日本語 suffix", "range reconstruction preserves external suffix");
range.Reset(range.Start + 3, range.Start + 3);
Check(!range.Active, "explicit candidate confirmation ends conversion");
range.ApplyEdit(9, 9, "k", 10); range.ApplyEdit(9, 10, "ky", 11); range.ApplyEdit(9, 11, "きょ", 11);
Check(range.Text == "きょ", "romaji draft replacement does not duplicate input");
range.ApplyEdit(9, 11, "", 9);
Check(!range.Active, "deleting entire draft clears empty range");
range.ApplyEdit(9, 9, "かんじ", 12);
range.MoveSelection(10, 10);
Check(!range.Matches("かんし"), "edited source rejects stale conversion");
range.Reset(0, 0);
Check(!range.Active && !range.Matches("かんじ"), "switching editors invalidates old range");
var mode = new ConversionModeState(true);
int firstGeneration = mode.Generation;
Check(mode.IsCurrent(firstGeneration, true), "offline initial mode matches dispatch");
mode.Set(false);
Check(!mode.Offline && !mode.IsCurrent(firstGeneration, true), "offline to online changes generation");
int onlineGeneration = mode.Generation;
mode.Set(true);
Check(mode.Offline && !mode.IsCurrent(onlineGeneration, false), "online to offline invalidates online request");
mode.Set(false);
Check(!mode.IsCurrent(onlineGeneration, false), "two-way switch cannot revive an old online request");
var phases = new List<ConversionRouting.Stage>();
await ConversionRouting.ConvertAsync("かんじ", false, local, _ => true,
    (_, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()), CancellationToken.None, phases.Add);
Check(phases.SequenceEqual(new[] { ConversionRouting.Stage.OnlinePending, ConversionRouting.Stage.LocalFallback }),
    "online failure explicitly identifies local fallback");
using var oldRequest = new CancellationTokenSource();
var delayed = new TaskCompletionSource<IReadOnlyList<string>>();
var pending = ConversionRouting.ConvertAsync("かんじ", false, local, _ => true, (_, _) => delayed.Task, oldRequest.Token);
oldRequest.Cancel(); mode.Set(true);
delayed.SetResult(new[] { "古いAI候補" });
try { await pending; throw new Exception("FAIL: delayed online response accepted"); }
catch (OperationCanceledException) { Check(true, "online-to-offline rejects delayed online result"); }
int callsBeforeOffline = networkCalls;
await ConversionRouting.ConvertAsync("かんじ", mode.Offline, local, _ => true, FakeNetwork, CancellationToken.None);
Check(networkCalls == callsBeforeOffline, "switching to offline performs zero sends");
mode.Set(false);
await ConversionRouting.ConvertAsync("かんじ", mode.Offline, local, _ => true, FakeNetwork, CancellationToken.None);
Check(networkCalls == callsBeforeOffline + 1, "switching back to online dispatches once");
var restored = new ConversionModeState(mode.Offline);
Check(restored.Offline == mode.Offline, "mode restored from saved value without inversion");
int restoredGeneration = restored.Generation;
restored.Set(restored.Offline);
Check(restored.Generation == restoredGeneration, "redisplay does not toggle mode");
using var obsoleteLocal = new CancellationTokenSource();
int oldOfflineGeneration = mode.Generation;
mode.Set(true); oldOfflineGeneration = mode.Generation;
mode.Set(false); obsoleteLocal.Cancel();
Check(!mode.IsCurrent(oldOfflineGeneration, true), "offline-to-online rejects delayed local generation");
int callsBeforeCanceled = networkCalls;
try
{
    await ConversionRouting.ConvertAsync("かんじ", false, local, _ => true, FakeNetwork, obsoleteLocal.Token);
    throw new Exception("FAIL: obsolete local request sent");
}
catch (OperationCanceledException) { Check(networkCalls == callsBeforeCanceled, "canceled local request never sends after mode switch"); }
Console.WriteLine($"{passed} assertions passed. No HTTP client or Android runtime is linked to this test executable.");

foreach (bool useOffline in new[] { true, false })
{
    var caretRange = new CompositionRange();
    caretRange.ApplyEdit(3, 3, "にほんごをべんきょう", 13);
    caretRange.MoveSelection(7, 7);
    var prefixTarget = caretRange.GetTarget()!;
    Check(prefixTarget.Source == "にほんご" && prefixTarget.Start == 3 && prefixTarget.End == 7,
        $"caret determines prefix target ({useOffline})");
    string? sentSource = null;
    var candidates = await ConversionRouting.ConvertAsync(prefixTarget.Source, useOffline,
        engine.GetCandidates(prefixTarget.Source), _ => true,
        (_, _) => { sentSource = prefixTarget.Source; return Task.FromResult<IReadOnlyList<string>>(new[] { "日本語" }); },
        CancellationToken.None);
    Check(candidates.Contains("日本語") && (useOffline ? sentSource is null : sentSource == "にほんご"),
        $"both routes use cursor target only ({useOffline})");
    string editor = "前文 " + caretRange.Text + " 後文";
    string result = editor[..prefixTarget.Start] + "日本語" + editor[prefixTarget.End..];
    Check(result == "前文 日本語をべんきょう 後文", $"commit prefix at original position preserves right side ({useOffline})");
    caretRange.Confirm(prefixTarget, "日本語");
    Check(caretRange.Text == "をべんきょう" && caretRange.Start == 6 && caretRange.SelectionStart == 6,
        $"prefix confirm retains suffix and caret at replacement end ({useOffline})");
    Check(caretRange.GetTarget()!.Source == "をべんきょう" && caretRange.Active, "partial confirmation immediately offers remaining reading at its start");
    caretRange.MoveSelection(12, 12);
    Check(caretRange.GetTarget()!.Source == "をべんきょう", "rightward move resumes remaining range");
    var oldTarget = caretRange.GetTarget()!;
    caretRange.MoveSelection(7, 7);
    Check(!caretRange.IsCurrent(oldTarget), "caret movement invalidates delayed target");
    caretRange.MoveSelection(12, 12);
    Check(!caretRange.IsCurrent(oldTarget), "move away and back does not revive old target");
    caretRange.MoveSelection(12, 7);
    var selectionTarget = caretRange.GetTarget()!;
    Check(selectionTarget.Selected && selectionTarget.Source == "べんきょう" && selectionTarget.Start == 7,
        "reverse selection converts selected portion");
    string afterSelection = result[..selectionTarget.Start] + "勉強" + result[selectionTarget.End..];
    Check(afterSelection == "前文 日本語を勉強 後文", "selected candidate preserves text on both sides");
    caretRange.Confirm(selectionTarget, "勉強");
    Check(caretRange.Text == "を勉強" && caretRange.SelectionStart == 9, "selected confirmation adjusts length and caret");
    caretRange.ApplyEdit(7, 7, "お", 8);
    Check(caretRange.GetTarget()!.Source == "をお" && caretRange.Text == "をお勉強", "middle input changes cursor target but keeps suffix");
    caretRange.ApplyEdit(7, 8, "", 7);
    Check(caretRange.GetTarget()!.Source == "を" && caretRange.Text == "を勉強", "middle deletion restores cursor prefix");
    caretRange.MoveSelection(2, 2);
    Check(caretRange.GetTarget() is null && caretRange.Active, "outside target suspends without finalizing");
}
var duplicate = new CompositionRange();
duplicate.ApplyEdit(0, 0, "にほんにほん", 6);
duplicate.MoveSelection(0, 3);
var firstOccurrence = duplicate.GetTarget()!;
duplicate.MoveSelection(3, 6);
Check(duplicate.GetTarget()!.Source == firstOccurrence.Source && !duplicate.IsCurrent(firstOccurrence),
    "identical readings at different positions cannot share an old candidate");
var beforeEdit = duplicate.GetTarget()!;
duplicate.ApplyEdit(3, 6, "にほん", 6);
duplicate.MoveSelection(3, 6);
Check(!duplicate.IsCurrent(beforeEdit), "same text after an edit still invalidates old generation");
var beforeEditorSwitch = duplicate.GetTarget()!;
duplicate.Reset(0, 0); duplicate.ApplyEdit(0, 0, "にほんにほん", 6); duplicate.MoveSelection(3, 6);
Check(!duplicate.IsCurrent(beforeEditorSwitch), "same coordinates in another editor cannot revive old candidates");
var androidRule = new NativeEditorMock("にほんごをべんきょう", 0, 4);
androidRule.SetComposingRegion(0, 10); androidRule.CommitText("日本語");
Check(androidRule.Text == "日本語", "native mock reproduces composing-region priority over smaller selection");
foreach (bool offlineRoute in new[] { true, false })
for (int repeat = 0; repeat < 2; repeat++)
{
    var session = new CompositionRange();
    string reading = "にほんごをべんきょうします";
    string prefix = "prefix ", suffix = " suffix";
    session.ApplyEdit(prefix.Length, prefix.Length, reading, prefix.Length + reading.Length);
    var editor = new NativeEditorMock(prefix + reading + suffix, session.SelectionStart, session.SelectionEnd);
    CompositionCommitter.Restore(session, editor);
    Check(editor.ComposingStart == session.Start && editor.ComposingEnd == session.End, "initial pending reading is native composing");
    foreach (var segment in new[] { (Reading: "にほんご", Candidate: "日本語"), (Reading: "をべんきょう", Candidate: "を勉強"), (Reading: "します", Candidate: "します") })
    {
        if (segment.Reading != "します")
        {
            int caret = session.Start + segment.Reading.Length;
            session.MoveSelection(caret, caret); editor.SetSelection(caret, caret);
        }
        var target = session.GetTarget()!;
        Check(target.Source == segment.Reading, $"remaining target {segment.Reading}, route={offlineRoute}, repeat={repeat}");
        var values = await ConversionRouting.ConvertAsync(target.Source, offlineRoute, engine.GetCandidates(target.Source), _ => true,
            (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { segment.Candidate }), CancellationToken.None);
        Check(values.Contains(segment.Candidate), "next candidate exists without external network");
        var result = CompositionCommitter.Commit(session, target, segment.Candidate, editor);
        Check(result.Committed && result.ComposingRestored && editor.BatchDepth == 0, "production committer completes balanced native transaction");
        Check(editor.Text.StartsWith(prefix) && editor.Text.EndsWith(suffix), "confirmation preserves unrelated text on both sides");
        if (session.Active)
        {
            var nativeSpan = session.NativeComposingSpan!;
            Check(editor.ComposingStart == nativeSpan.Start && editor.ComposingEnd == nativeSpan.End,
                "remaining reading retains actual native composing marker");
            Check(editor.SelectionStart == session.SelectionStart && session.GetTarget() is not null,
                "caret stays after confirmed segment and remaining candidate is available immediately");
            Console.WriteLine($"TRACE route={offlineRoute}: text={editor.Text}; composing=[{editor.ComposingStart},{editor.ComposingEnd}); next={session.GetTarget()!.Source}");
        }
        else Check(editor.ComposingStart == -1 && editor.ComposingEnd == -1, "only final confirmation removes native composing marker");
    }
    Check(editor.Text == "prefix 日本語を勉強します suffix", "three successive confirmations produce exact final text");
}
var split = new CompositionRange(); split.ApplyEdit(0, 0, "あにほんごい", 6); split.MoveSelection(1, 5);
var splitEditor = new NativeEditorMock(split.Text, 1, 5); CompositionCommitter.Restore(split, splitEditor);
var splitTarget = split.GetTarget()!;
CompositionCommitter.Commit(split, splitTarget, "日本語", splitEditor);
Check(splitEditor.Text == "あ日本語い" && splitEditor.ComposingStart == 4 && splitEditor.ComposingEnd == 5,
    "middle selected confirmation excludes confirmed word from native composing");
Check(split.PendingSpans().Select(x => split.Text[(x.Start - split.Start)..(x.End - split.Start)]).SequenceEqual(new[] { "あ", "い" }),
    "both unconverted sides remain tracked around a confirmed selection");
split.MoveSelection(1, 1); splitEditor.SetSelection(1, 1); CompositionCommitter.Restore(split, splitEditor);
Check(split.GetTarget()!.Source == "あ" && splitEditor.ComposingStart == 0 && splitEditor.ComposingEnd == 1,
    "moving back to left reading restores composing without re-marking confirmed word");
var stale = split.GetTarget()!; split.MoveSelection(5, 5);
string unchanged = splitEditor.Text;
Check(!CompositionCommitter.Commit(split, stale, "誤置換", splitEditor).Committed && splitEditor.Text == unchanged,
    "stale selected target cannot mutate native editor");
var unsupported = new CompositionRange(); unsupported.ApplyEdit(0, 0, "にほんごい", 5); unsupported.MoveSelection(4, 4);
var unsupportedEditor = new NativeEditorMock(unsupported.Text, 4, 4) { RejectRegion = true };
var unsupportedResult = CompositionCommitter.Commit(unsupported, unsupported.GetTarget()!, "日本語", unsupportedEditor);
Check(unsupportedResult.Committed && !unsupportedResult.ComposingRestored && unsupported.Text == "い" && unsupported.GetTarget() is not null,
    "unsupported native span reports failure without discarding internal remaining reading");
foreach (bool offlineEnter in new[] { true, false })
foreach (int cursor in new[] { 0, 4, 6 })
foreach (bool selectedEnter in new[] { false, true })
{
    var rawRange = new CompositionRange();
    rawRange.ApplyEdit(0, 0, "にほんごです", 6);
    int end = selectedEnter ? 4 : cursor;
    rawRange.MoveSelection(cursor, end);
    var rawEditor = new NativeEditorMock(rawRange.Text, cursor, end);
    CompositionCommitter.Restore(rawRange, rawEditor);
    var obsoleteTarget = rawRange.GetTarget();
    var shown = offlineEnter ? engine.GetCandidates("にほんご") : new[] { "日本語" };
    Check(shown.Contains("日本語"), "Enter test includes a visible converted candidate in both modes");
    var action = CompositionCommitter.FinishRaw(rawRange, rawEditor, false);
    Check(action == CompositionCommitter.EnterAction.ConfirmedRaw && rawEditor.Text == "にほんごです",
        "Enter confirms raw full reading regardless of candidates, caret or selection");
    Check(rawEditor.SelectionStart == cursor && rawEditor.SelectionEnd == end && rawEditor.ComposingStart == -1,
        "raw Enter preserves selection and removes native composing only");
    Check(!rawRange.Active && (obsoleteTarget is null || !rawRange.IsCurrent(obsoleteTarget)),
        "raw Enter invalidates delayed candidate target");
    Check(!rawEditor.Operations.Any(x => x.StartsWith("commit:")), "raw Enter never replaces text with a candidate");
    Check(CompositionCommitter.FinishRaw(rawRange, rawEditor, false) == CompositionCommitter.EnterAction.SendEnter,
        "next Enter sends normal editor key after raw confirmation");
}
foreach (bool offlineEnter in new[] { true, false })
{
    var partialEnter = new CompositionRange(); partialEnter.ApplyEdit(0, 0, "にほんごです", 6); partialEnter.MoveSelection(4, 4);
    var partialEditor = new NativeEditorMock(partialEnter.Text, 4, 4);
    Check(CompositionCommitter.Commit(partialEnter, partialEnter.GetTarget()!, "日本語", partialEditor).Committed,
        "explicit candidate confirmation remains available before Enter");
    partialEnter.MoveSelection(4, 4); partialEditor.SetSelection(4, 4);
    var lateTarget = partialEnter.GetTarget()!;
    using var lateCancellation = new CancellationTokenSource();
    var lateSource = new TaskCompletionSource<IReadOnlyList<string>>();
    async Task<IReadOnlyList<string>> LateCandidates()
    {
        if (offlineEnter) await lateSource.Task; // simulate delayed local dictionary loading
        return await ConversionRouting.ConvertAsync(lateTarget.Source, offlineEnter,
            new[] { lateTarget.Source }, _ => true, async (_, _) => await lateSource.Task, lateCancellation.Token);
    }
    var lateRequest = LateCandidates();
    Check(CompositionCommitter.FinishRaw(partialEnter, partialEditor, false) == CompositionCommitter.EnterAction.ConfirmedRaw &&
        partialEditor.Text == "日本語です", "Enter preserves converted prefix and raw suffix after cursor movement");
    lateCancellation.Cancel(); lateSource.SetResult(new[] { "遅い候補" });
    try { await lateRequest; throw new Exception("FAIL: Enter accepted delayed result"); }
    catch (OperationCanceledException) { Check(true, "Enter cancellation rejects delayed candidate response in both modes"); }
    Check(!CompositionCommitter.Commit(partialEnter, lateTarget, "遅い候補", partialEditor).Committed && partialEditor.Text == "日本語です",
        "delayed candidate cannot overwrite raw-confirmed text");
}
var emptyEnter = new CompositionRange(); var emptyEditor = new NativeEditorMock("", 0, 0);
Check(CompositionCommitter.FinishRaw(emptyEnter, emptyEditor, false) == CompositionCommitter.EnterAction.SendEnter,
    "empty input retains normal Enter");
Check(CompositionCommitter.FinishRaw(emptyEnter, emptyEditor, true) == CompositionCommitter.EnterAction.ConfirmedRaw,
    "flushed roman draft consumes confirmation Enter");
using (var noKeyClient = new KanaKanjiClient(() => ""))
{
    bool notified = false;
    var noKeyCandidates = await noKeyClient.GetCandidatesStreamingAsync("test", _ => { notified = true; return true; }, CancellationToken.None);
    Check(noKeyCandidates.Count == 0 && !notified, "missing credentials returns fallback without HTTP or callback");
}
KeyboardRegressionChecks.Run(Check);
Console.WriteLine($"FINAL: {passed} assertions passed, including keyboard Shift/flick and credential-free publication behavior.");
