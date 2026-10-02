using System;
using System.Collections.Generic;

using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.InputMethodServices;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Handler = Android.OS.Handler;
using Keycode = Android.Views.Keycode;
using Looper = Android.OS.Looper;

namespace NyaIme
{
    [Service(
        Label = "RinIME オフライン",
        Permission = "android.permission.BIND_INPUT_METHOD",
        Exported = true)]
    [IntentFilter(new[] { "android.view.InputMethod" })]
    [MetaData("android.view.im", Resource = "@xml/method")]
    public class NyaImeService : InputMethodService
    {
        // =========================================================
        // レイアウト
        //
        // 横幅比
        //
        // 左 : A列 : K列 : H列 : 右
        // 0.65 : 1 : 1 : 1 : 0.65
        //
        // =========================================================

        private const int KeyboardBodyHeightDp = 292;
        private const int ToolbarHeightDp = 104;
        private const int ExpandedToolbarHeightDp = 144;
        private const int MaximumToolbarHeightDp = 280;
        private const int CandidateRowMinimumHeightDp = 50;
        private const int LongCandidateTextLength = 12;
        private const int ConversionIntervalMilliseconds = 500;
        private const int MaximumEmojiHistoryCount = 64;
        private const string EmojiHistoryPreferences =
            "emoji_history";
        private const string EmojiHistoryKey =
            "recent";

        // Insetsを取得できない端末向けの最小余白
        private const int MinimumBottomSpacerDp = 30;

        private const int FlickThresholdDp = 28;

        private const float SideWeight = 0.65f;
        private const float CenterWeight = 3.0f;

        private TextView _flickPreview;

        private readonly KanaKanjiClient _kanaKanjiClient =
            new KanaKanjiClient();
        private Task<OfflineKanaKanjiEngine>? _offlineEngine;
        private CancellationTokenSource? _conversionCancellation;
        private readonly ConversionModeState _conversionMode = new(true);
        private bool _offlineMode { get => _conversionMode.Offline; set => _conversionMode.Set(value); }
        private int _conversionRequest;
        private string? _candidateSource;
        private CompositionRange.Target? _candidateTarget;
        private CompositionRange.Span? _lastNativeSpan;
        private int _nativeRestoreSelectionRevision = -1;
        private int _candidateCompositionVersion;
        private TextView? _conversionModeStatus;
        private string _conversionPhase = "入力後に候補を表示";
        private readonly HashSet<string> _predictions = new(StringComparer.Ordinal);
        private Handler? _mainHandler;

        private LinearLayout _candidateBar;
        private ScrollView _candidateScroll;
        private FrameLayout _candidatePanel;
        private LinearLayout _emojiPanel;
        private GridView _emojiGrid;
        private Button _emojiHistoryButton;
        private Button _emojiAllButton;
        private IReadOnlyList<string> _emojiCandidates =
            Array.Empty<string>();
        private IReadOnlyList<string> _visibleEmojiCandidates =
            Array.Empty<string>();
        private IReadOnlyDictionary<string, IReadOnlyList<string>>
            _emojiVariants =
                new Dictionary<string, IReadOnlyList<string>>();
        private readonly List<string> _emojiHistory =
            new List<string>();
        private bool _emojiHistoryLoaded;
        private bool _showEmojiHistory;
        private IReadOnlyList<string> _candidates =
            Array.Empty<string>();
        private readonly CompositionRange _composition = new();
        private int _romajiDraftStart = -1;
        private int _romajiDraftLength;
        private int _lastSelectionPosition = -1;
        private bool _suppressSelectionUpdates;
        private int _compositionVersion;
        private int _autoConversionVersion;
        private int _candidateSourceLength;
        private bool _destroyed;
        private bool _numericMode;
        private bool _emojiMode;
        private bool _halfWidthAlphaMode;

        // 未確定ローマ字
        private string _romajiBuffer = "";
        private bool _pendingSecondN;

        // =========================================================
        // ローマ字 → かな辞書
        // =========================================================

        private static readonly Dictionary<string, string> RomajiMap =
            new Dictionary<string, string>
            {
                // 母音
                ["a"] = "あ",
                ["i"] = "い",
                ["u"] = "う",
                ["e"] = "え",
                ["o"] = "お",

                // K
                ["ka"] = "か",
                ["ki"] = "き",
                ["ku"] = "く",
                ["ke"] = "け",
                ["ko"] = "こ",

                // S
                ["sa"] = "さ",
                ["shi"] = "し",
                ["si"] = "し",
                ["she"] = "しぇ",
                ["su"] = "す",
                ["se"] = "せ",
                ["so"] = "そ",

                // T
                ["ta"] = "た",
                ["chi"] = "ち",
                ["ti"] = "ち",
                ["tsu"] = "つ",
                ["tu"] = "つ",
                ["te"] = "て",
                ["to"] = "と",
                ["tsa"] = "つぁ",
                ["tsi"] = "つぃ",
                ["tse"] = "つぇ",
                ["tso"] = "つぉ",
                ["thi"] = "てぃ",
                ["thu"] = "てゅ",
                ["the"] = "てぇ",
                ["tho"] = "てょ",

                // N
                ["na"] = "な",
                ["ni"] = "に",
                ["nu"] = "ぬ",
                ["ne"] = "ね",
                ["no"] = "の",

                // H
                ["ha"] = "は",
                ["hi"] = "ひ",
                ["fu"] = "ふ",
                ["hu"] = "ふ",
                ["he"] = "へ",
                ["ho"] = "ほ",

                // M
                ["ma"] = "ま",
                ["mi"] = "み",
                ["mu"] = "む",
                ["me"] = "め",
                ["mo"] = "も",

                // Y
                ["ya"] = "や",
                ["yu"] = "ゆ",
                ["yo"] = "よ",
                ["ye"] = "いぇ",

                // R
                ["ra"] = "ら",
                ["ri"] = "り",
                ["ru"] = "る",
                ["re"] = "れ",
                ["ro"] = "ろ",

                // W
                ["wa"] = "わ",
                ["wo"] = "を",
                ["wi"] = "うぃ",
                ["we"] = "うぇ",
                ["wu"] = "う",

                // G
                ["ga"] = "が",
                ["gi"] = "ぎ",
                ["gu"] = "ぐ",
                ["ge"] = "げ",
                ["go"] = "ご",
                ["gwa"] = "ぐぁ",
                ["gwi"] = "ぐぃ",
                ["gwu"] = "ぐぅ",
                ["gwe"] = "ぐぇ",
                ["gwo"] = "ぐぉ",

                // Z / J
                ["za"] = "ざ",
                ["j"] = "じ",
                ["ji"] = "じ",
                ["zi"] = "じ",
                ["zu"] = "ず",
                ["ze"] = "ぜ",
                ["zo"] = "ぞ",

                // D
                ["da"] = "だ",
                ["di"] = "ぢ",
                ["du"] = "づ",
                ["de"] = "で",
                ["do"] = "ど",
                ["dhi"] = "でぃ",
                ["dhu"] = "でゅ",
                ["dhe"] = "でぇ",
                ["dho"] = "でょ",

                // B
                ["ba"] = "ば",
                ["bi"] = "び",
                ["bu"] = "ぶ",
                ["be"] = "べ",
                ["bo"] = "ぼ",

                // P
                ["pa"] = "ぱ",
                ["pi"] = "ぴ",
                ["pu"] = "ぷ",
                ["pe"] = "ぺ",
                ["po"] = "ぽ",

                // K拗音
                ["kya"] = "きゃ",
                ["kyu"] = "きゅ",
                ["kyo"] = "きょ",
                ["kye"] = "きぇ",
                ["kwa"] = "くぁ",
                ["kwi"] = "くぃ",
                ["kwu"] = "くぅ",
                ["kwe"] = "くぇ",
                ["kwo"] = "くぉ",

                // SH
                ["sha"] = "しゃ",
                ["shu"] = "しゅ",
                ["sho"] = "しょ",

                ["sya"] = "しゃ",
                ["syu"] = "しゅ",
                ["syo"] = "しょ",

                // CH
                ["cha"] = "ちゃ",
                ["chu"] = "ちゅ",
                ["cho"] = "ちょ",
                ["che"] = "ちぇ",
                ["cya"] = "ちゃ",
                ["cyu"] = "ちゅ",
                ["cyo"] = "ちょ",

                ["tya"] = "ちゃ",
                ["tyu"] = "ちゅ",
                ["tyo"] = "ちょ",
                ["tyi"] = "てぃ",
                ["tye"] = "ちぇ",

                // NY
                ["nya"] = "にゃ",
                ["nyu"] = "にゅ",
                ["nyo"] = "にょ",
                ["nye"] = "にぇ",

                // HY
                ["hya"] = "ひゃ",
                ["hyu"] = "ひゅ",
                ["hyo"] = "ひょ",
                ["hye"] = "ひぇ",

                // MY
                ["mya"] = "みゃ",
                ["myu"] = "みゅ",
                ["myo"] = "みょ",
                ["mye"] = "みぇ",

                // RY
                ["rya"] = "りゃ",
                ["ryu"] = "りゅ",
                ["ryo"] = "りょ",
                ["rye"] = "りぇ",

                // GY
                ["gya"] = "ぎゃ",
                ["gyu"] = "ぎゅ",
                ["gyo"] = "ぎょ",
                ["gye"] = "ぎぇ",

                // J
                ["ja"] = "じゃ",
                ["ju"] = "じゅ",
                ["jo"] = "じょ",
                ["je"] = "じぇ",

                ["jya"] = "じゃ",
                ["jyu"] = "じゅ",
                ["jyo"] = "じょ",

                // BY
                ["bya"] = "びゃ",
                ["byu"] = "びゅ",
                ["byo"] = "びょ",
                ["bye"] = "びぇ",

                // PY
                ["pya"] = "ぴゃ",
                ["pyu"] = "ぴゅ",
                ["pyo"] = "ぴょ",
                ["pye"] = "ぴぇ",

                // D拗音
                ["dya"] = "ぢゃ",
                ["dyu"] = "ぢゅ",
                ["dyo"] = "ぢょ",

                // F
                ["fa"] = "ふぁ",
                ["fi"] = "ふぃ",
                ["fe"] = "ふぇ",
                ["fo"] = "ふぉ",
                ["fya"] = "ふゃ",
                ["fyu"] = "ふゅ",
                ["fyo"] = "ふょ",

                // V
                // 画像の辞書どおり
                ["va"] = "ゔぁ",
                ["vi"] = "ゔぃ",
                ["vu"] = "ゔ",
                ["ve"] = "ゔぇ",
                ["vo"] = "ゔぉ",
                ["vya"] = "ゔゃ",
                ["vyu"] = "ゔゅ",
                ["vyo"] = "ゔょ",

                // Q（く系の外来音）
                ["qa"] = "くぁ",
                ["qi"] = "くぃ",
                ["qu"] = "く",
                ["qe"] = "くぇ",
                ["qo"] = "くぉ",
                ["qya"] = "くゃ",
                ["qyu"] = "くゅ",
                ["qyo"] = "くょ",

                // 小母音
                ["xa"] = "ぁ",
                ["xi"] = "ぃ",
                ["xu"] = "ぅ",
                ["xe"] = "ぇ",
                ["xo"] = "ぉ",

                ["la"] = "ぁ",
                ["li"] = "ぃ",
                ["lu"] = "ぅ",
                ["le"] = "ぇ",
                ["lo"] = "ぉ",

                // 小ゃゅょ
                ["xya"] = "ゃ",
                ["xyu"] = "ゅ",
                ["xyo"] = "ょ",

                ["lya"] = "ゃ",
                ["lyu"] = "ゅ",
                ["lyo"] = "ょ",

                // 小っ
                ["xtu"] = "っ",
                ["xtsu"] = "っ",
                ["ltu"] = "っ",
                ["ltsu"] = "っ",

                // 小ゎ
                ["xwa"] = "ゎ",
                ["lwa"] = "ゎ",

                ["xka"] = "ヵ",
                ["xke"] = "ヶ"
            };

        // =========================================================
        // IME
        // =========================================================

        public override bool OnEvaluateInputViewShown()
        {
            return true;
        }

        public override void OnCreate()
        {
            base.OnCreate();
            _offlineMode = GetSharedPreferences("conversion", FileCreationMode.Private)!.GetBoolean("offline", true);
            _offlineEngine = Task.Run(() =>
            {
                using var stream = Assets!.Open("offline-dictionary.gz");
                return new OfflineKanaKanjiEngine(stream);
            });
            _mainHandler =
                new Handler(Looper.MainLooper!);
        }

        public override void OnStartInput(
            EditorInfo attribute,
            bool restarting)
        {
            base.OnStartInput(
                attribute,
                restarting);
            _lastNativeSpan = null; _nativeRestoreSelectionRevision = -1;

            if (restarting && _composition.Active)
            {
                _nativeRestoreSelectionRevision = -1;
                int previousRevision = _composition.SelectionRevision;
                _composition.MoveSelection(attribute.InitialSelStart, attribute.InitialSelEnd);
                SyncSelectionPosition();
                if (CurrentInputConnection is not null) RestoreNativeComposition(CurrentInputConnection);
                if (previousRevision != _composition.SelectionRevision || !_composition.SelectionWithin) ClearCandidates();
                if (_composition.GetTarget() is not null && _candidates.Count == 0) ScheduleAutoConversion();
                return;
            }

            _romajiBuffer = "";
            _pendingSecondN = false;
            _composition.Reset();
            _romajiDraftStart = -1; _romajiDraftLength = 0;
            _lastSelectionPosition =
                attribute.InitialSelStart;
            _composition.MoveSelection(attribute.InitialSelStart, attribute.InitialSelEnd);
            _compositionVersion++;
            _autoConversionVersion++;
            ClearCandidates();
        }

        public override void OnFinishInput()
        {
            if (CurrentInputConnection is not null) FinishNativeComposition(CurrentInputConnection);
            _romajiBuffer = "";
            _pendingSecondN = false;
            _composition.Reset();
            _romajiDraftStart = -1; _romajiDraftLength = 0;
            _lastSelectionPosition = -1;
            _compositionVersion++;
            _autoConversionVersion++;
            ClearCandidates();

            base.OnFinishInput();
        }

        public override void OnUpdateSelection(int oldSelStart, int oldSelEnd, int newSelStart, int newSelEnd,
            int candidatesStart, int candidatesEnd)
        {
            base.OnUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd);
            if (_suppressSelectionUpdates) return;
            var ic = CurrentInputConnection;
            if (ic is null) return;
            var actual = GetEditorSelection(ic);
            // Batched IME edits can report intermediate selections after a newer edit has completed.
            if (actual.Verified && (actual.Start != newSelStart || actual.End != newSelEnd)) return;
            if (_romajiDraftStart >= 0 &&
                (newSelStart != _composition.SelectionStart || newSelEnd != _composition.SelectionEnd))
            {
                FinishNativeComposition(ic); // Keep the visible partial romaji when the user taps elsewhere.
                _romajiDraftStart = -1; _romajiDraftLength = 0;
                _romajiBuffer = ""; _pendingSecondN = false;
                _compositionVersion++;
                ClearCandidates();
            }
            int previousSelectionRevision = _composition.SelectionRevision;
            _composition.MoveSelection(newSelStart, newSelEnd);
            SyncSelectionPosition();
            if (previousSelectionRevision != _composition.SelectionRevision) ClearCandidates();
            if (!_composition.SelectionWithin)
            {
                ClearCandidates(); // Suspend; moving away does not discard the pending range.
                return;
            }
            if (_romajiDraftStart >= 0) return;
            if (ReadTrackedText(ic) != _composition.Text)
            {
                // An external paste/edit changed the editor independently. Never overwrite its text.
                _composition.Reset(newSelStart, newSelEnd);
                SyncSelectionPosition();
                _compositionVersion++;
                ClearCandidates();
                return;
            }
            if (_composition.Active) RestoreNativeComposition(ic);
            if (_composition.GetTarget() is not null && _candidates.Count == 0) ScheduleAutoConversion();
        }

        public override void OnDestroy()
        {
            _conversionCancellation?.Cancel();
            _conversionCancellation?.Dispose();
            _conversionCancellation = null;
            _autoConversionVersion++;
            _destroyed = true;
            _mainHandler?.RemoveCallbacksAndMessages(null);
            _mainHandler?.Dispose();
            _mainHandler = null;
            _kanaKanjiClient.Dispose();
            base.OnDestroy();
        }

        // =========================================================
        // UI
        // =========================================================

        public override View OnCreateInputView()
        {
            var shell =
                new FrameLayout(this);

            var root =
                new NavigationInsetLayout(
                    this,
                    GetNavigationBarInsetFallback());

            root.Orientation =
                Orientation.Vertical;

            root.SetBackgroundColor(
                Color.Rgb(
                    235,
                    239,
                    242));

            // -----------------------------------------------------
            // 上部バー
            // -----------------------------------------------------

            root.AddView(
                CreateToolbar());

            // -----------------------------------------------------
            // キーボード本体
            // -----------------------------------------------------

            var keyboard =
                new LinearLayout(this);

            keyboard.Orientation =
                Orientation.Horizontal;

            keyboard.SetBackgroundColor(
                Color.Rgb(
                    235,
                    239,
                    242));

            // =====================================================
            // 左列
            // width = 0.65
            // =====================================================

            keyboard.AddView(
                CreateLeftColumn(),
                new LinearLayout.LayoutParams(
                    0,
                    ViewGroup.LayoutParams.MatchParent,
                    SideWeight));

            // =====================================================
            // 中央3列
            // width = 3.0
            // =====================================================

            var center =
                _numericMode
                    ? CreateNumericCenter()
                    : CreateAlphabetCenter();

            keyboard.AddView(
                center,
                new LinearLayout.LayoutParams(
                    0,
                    ViewGroup.LayoutParams.MatchParent,
                    CenterWeight));

            // =====================================================
            // 右列
            // width = 0.65
            // =====================================================

            keyboard.AddView(
                CreateRightColumn(),
                new LinearLayout.LayoutParams(
                    0,
                    ViewGroup.LayoutParams.MatchParent,
                    SideWeight));

            root.AddView(
                keyboard,
                new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    Dp(KeyboardBodyHeightDp)));

            shell.AddView(
                root,
                new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent));

            // =====================================================
            // フリックプレビュー
            // =====================================================

            _flickPreview =
                new TextView(this);

            _flickPreview.TextSize =
                42;

            _flickPreview.Gravity =
                GravityFlags.Center;

            _flickPreview.SetTextColor(
                Color.Rgb(
                    20,
                    30,
                    40));

            _flickPreview.SetBackgroundColor(
                Color.White);

            _flickPreview.Visibility =
                ViewStates.Gone;

            _flickPreview.Elevation =
                Dp(12);

            var previewParams =
                new FrameLayout.LayoutParams(
                    Dp(88),
                    Dp(88));

            previewParams.Gravity =
                GravityFlags.Top |
                GravityFlags.CenterHorizontal;

            previewParams.TopMargin =
                Dp(32);

            shell.AddView(
                _flickPreview,
                previewParams);

            return shell;
        }

        // =========================================================
        // FlickData
        // =========================================================

        private sealed class NavigationInsetLayout :
            LinearLayout
        {
            private readonly int _fallbackBottomInset;

            public NavigationInsetLayout(
                Context context,
                int fallbackBottomInset)
                : base(context)
            {
                _fallbackBottomInset =
                    fallbackBottomInset;

                SetPadding(
                    0,
                    0,
                    0,
                    _fallbackBottomInset);
            }

            protected override void OnAttachedToWindow()
            {
                base.OnAttachedToWindow();
                RequestApplyInsets();
            }

            public override WindowInsets OnApplyWindowInsets(
                WindowInsets insets)
            {
                int navigationBottom;

                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    navigationBottom =
                        insets.GetInsets(
                            WindowInsets.Type.NavigationBars())
                        .Bottom;
                }
                else
                {
#pragma warning disable CA1422
                    navigationBottom =
                        insets.SystemWindowInsetBottom;
#pragma warning restore CA1422
                }

                SetPadding(
                    PaddingLeft,
                    PaddingTop,
                    PaddingRight,
                    Math.Max(
                        _fallbackBottomInset,
                        navigationBottom));

                return base.OnApplyWindowInsets(
                    insets);
            }
        }

        private class FlickData
        {
            public string Center;
            public string Up;
            public string Left;
            public string Right;
            public string Down;

            public FlickData(
                string center,
                string up,
                string left,
                string right,
                string down)
            {
                Center = center;
                Up = up;
                Left = left;
                Right = right;
                Down = down;
            }
        }

        private LinearLayout CreateAlphabetCenter()
        {
            var center =
                new LinearLayout(this);

            center.Orientation =
                Orientation.Vertical;

            center.AddView(
                CreateRow(
                    new FlickData("A", "X", "C", null, null),
                    new FlickData("K", "F", null, "G", null),
                    new FlickData("H", "P", null, "B", null)));

            center.AddView(
                CreateRow(
                    new FlickData("Y", null, "L", null, null),
                    new FlickData("S", null, null, "Z", null),
                    new FlickData("T", null, null, "D", null)));

            center.AddView(
                CreateRow(
                    new FlickData("I", null, "Q", null, null),
                    new FlickData("N", null, null, "M", null),
                    new FlickData("R", "J", null, "W", null)));

            center.AddView(
                CreateRow(
                    new FlickData("U", null, "V", null, "（"),
                    new FlickData("E", null, "！", null, ")"),
                    new FlickData("O", null, "？", "ー", null)));

            return center;
        }

        private LinearLayout CreateNumericCenter()
        {
            var center =
                new LinearLayout(this);

            center.Orientation =
                Orientation.Vertical;

            center.AddView(
                CreateNumericRow("1", "2", "3"));
            center.AddView(
                CreateNumericRow("4", "5", "6"));
            center.AddView(
                CreateNumericRow("7", "8", "9"));
            center.AddView(
                CreateNumericRow("＊", "0", "."));

            return center;
        }

        private View CreateNumericRow(
            string left,
            string center,
            string right)
        {
            var row =
                new LinearLayout(this);

            row.Orientation =
                Orientation.Horizontal;

            AddNumericKey(
                row,
                left);
            AddNumericKey(
                row,
                center);
            AddNumericKey(
                row,
                right);

            row.LayoutParameters =
                new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    0,
                    1f);

            return row;
        }

        private void AddNumericKey(
            LinearLayout row,
            string text)
        {
            View view =
                string.IsNullOrEmpty(text)
                    ? new Space(this)
                    : CreateFunctionKey(
                        text,
                        () => InputHalfWidthText(text));

            row.AddView(
                view,
                HorizontalWeighted());
        }

        // =========================================================
        // 1行
        // =========================================================

        private View CreateRow(
            FlickData a,
            FlickData b,
            FlickData c)
        {
            var row =
                new LinearLayout(this);

            row.Orientation =
                Orientation.Horizontal;

            row.AddView(
                CreateFlickKey(a),
                HorizontalWeighted());

            row.AddView(
                CreateFlickKey(b),
                HorizontalWeighted());

            row.AddView(
                CreateFlickKey(c),
                HorizontalWeighted());

            row.LayoutParameters =
                new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    0,
                    1f);

            return row;
        }

        // =========================================================
        // フリックキー
        // =========================================================

        private View CreateFlickKey(
            FlickData key)
        {
            var frame =
                new FrameLayout(this);

            frame.SetBackgroundColor(
                Color.Rgb(
                    235,
                    239,
                    242));

            frame.Clickable = true;
            frame.Focusable = false;

            // 中央
            AddLabel(
                frame,
                DisplayLetterForCurrentMode(
                    key.Center),
                34,
                GravityFlags.Center);

            // 上
            AddLabel(
                frame,
                DisplayLetterForCurrentMode(
                    key.Up),
                15,
                GravityFlags.Top |
                GravityFlags.CenterHorizontal);

            // 左
            AddLabel(
                frame,
                DisplayLetterForCurrentMode(
                    key.Left),
                15,
                GravityFlags.Left |
                GravityFlags.CenterVertical);

            // 右
            AddLabel(
                frame,
                DisplayLetterForCurrentMode(
                    key.Right),
                15,
                GravityFlags.Right |
                GravityFlags.CenterVertical);

            // 下
            AddLabel(
                frame,
                DisplayLetterForCurrentMode(
                    key.Down),
                15,
                GravityFlags.Bottom |
                GravityFlags.CenterHorizontal);

            float startX = 0;
            float startY = 0;

            string currentCandidate =
                null;

            frame.Touch += (_, e) =>
            {
                var ev = e.Event;

                switch (ev.ActionMasked)
                {
                    // -------------------------------------------------
                    // DOWN
                    // -------------------------------------------------

                    case MotionEventActions.Down:
                        {
                            startX =
                                ev.GetX();

                            startY =
                                ev.GetY();

                            currentCandidate =
                                key.Center;

                            ShowPreview(
                                currentCandidate);

                            frame.SetBackgroundColor(
                                Color.Rgb(
                                    210,
                                    220,
                                    226));

                            e.Handled = true;
                            break;
                        }

                    // -------------------------------------------------
                    // MOVE
                    // -------------------------------------------------

                    case MotionEventActions.Move:
                        {
                            float dx =
                                ev.GetX() -
                                startX;

                            float dy =
                                ev.GetY() -
                                startY;

                            string candidate =
                                ResolveFlick(
                                    key,
                                    dx,
                                    dy);

                            if (candidate !=
                                currentCandidate)
                            {
                                currentCandidate =
                                    candidate;

                                ShowPreview(
                                    currentCandidate);
                            }

                            e.Handled = true;
                            break;
                        }

                    // -------------------------------------------------
                    // UP
                    // -------------------------------------------------

                    case MotionEventActions.Up:
                        {
                            float dx =
                                ev.GetX() -
                                startX;

                            float dy =
                                ev.GetY() -
                                startY;

                            string output =
                                ResolveFlick(
                                    key,
                                    dx,
                                    dy);

                            HidePreview();

                            frame.SetBackgroundColor(
                                Color.Rgb(
                                    235,
                                    239,
                                    242));

                            if (!string.IsNullOrEmpty(
                                output))
                            {
                                InputRomaji(
                                    output
                                        .ToLowerInvariant());
                            }

                            currentCandidate =
                                null;

                            e.Handled = true;
                            break;
                        }

                    // -------------------------------------------------
                    // CANCEL
                    // -------------------------------------------------

                    case MotionEventActions.Cancel:
                        {
                            HidePreview();

                            frame.SetBackgroundColor(
                                Color.Rgb(
                                    235,
                                    239,
                                    242));

                            currentCandidate =
                                null;

                            e.Handled = true;
                            break;
                        }

                    default:
                        {
                            e.Handled = true;
                            break;
                        }
                }
            };

            return frame;
        }

        private string DisplayLetterForCurrentMode(
            string text)
        {
            if (!_halfWidthAlphaMode ||
                string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text.ToLowerInvariant();
        }

        // =========================================================
        // フリック方向
        // =========================================================

        private string ResolveFlick(
            FlickData key,
            float dx,
            float dy)
        {
            float threshold =
                Dp(FlickThresholdDp);

            float ax =
                Math.Abs(dx);

            float ay =
                Math.Abs(dy);

            // 中央
            if (ax < threshold &&
                ay < threshold)
            {
                return key.Center;
            }

            // 横
            if (ax > ay)
            {
                if (dx < 0)
                    return key.Left;

                return key.Right;
            }

            // 縦
            if (dy < 0)
                return key.Up;

            return key.Down;
        }

        // =========================================================
        // キー文字
        // =========================================================

        private void AddLabel(
            FrameLayout parent,
            string text,
            float textSize,
            GravityFlags gravity)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var label =
                new TextView(this);

            label.Text =
                text;

            label.TextSize =
                textSize;

            label.Gravity =
                GravityFlags.Center;

            label.SetTextColor(
                Color.Rgb(
                    20,
                    38,
                    50));

            label.SetPadding(
                Dp(7),
                Dp(4),
                Dp(7),
                Dp(4));

            label.Clickable = false;
            label.Focusable = false;

            var lp =
                new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    ViewGroup.LayoutParams.WrapContent);

            lp.Gravity =
                gravity;

            parent.AddView(
                label,
                lp);
        }

        // =========================================================
        // プレビュー
        // =========================================================

        private void ShowPreview(
            string text)
        {
            if (_flickPreview == null)
                return;

            if (string.IsNullOrEmpty(text))
            {
                _flickPreview.Text =
                    "×";
            }
            else
            {
                _flickPreview.Text =
                    text.ToUpperInvariant();
            }

            _flickPreview.Visibility =
                ViewStates.Visible;
        }

        private void HidePreview()
        {
            if (_flickPreview == null)
                return;

            _flickPreview.Visibility =
                ViewStates.Gone;
        }

        // =========================================================
        // ローマ字入力
        // =========================================================

        private void InputRomaji(
            string text)
        {
            if (_halfWidthAlphaMode)
            {
                InputHalfWidthText(
                    text.ToLowerInvariant());
                return;
            }

            foreach (char ch in text)
            {
                char lower =
                    char.ToLowerInvariant(ch);

                if (_pendingSecondN)
                {
                    // nnaでは2文字目のnを次の「な」に使う。
                    // nnsaiでは2文字目のnを消費して「んさい」にする。
                    if (lower is not ('a' or 'i' or 'u' or 'e' or 'o' or 'y'))
                    {
                        _romajiBuffer = "";
                    }

                    _pendingSecondN = false;
                }

                _romajiBuffer +=
                    lower;

                ProcessRomajiBuffer();
            }

            ScheduleAutoConversion();
        }

        private void InputHalfWidthText(
            string text)
        {
            FlushRomaji();

            CommitRaw(text);
            _composition.Reset();
            _romajiDraftStart = -1; _romajiDraftLength = 0;
            _compositionVersion++;
            _autoConversionVersion++;
            ClearCandidates();
        }

        // =========================================================
        // 決定的かな変換
        // =========================================================

        private void ProcessRomajiBuffer()
        {
            while (_romajiBuffer.Length > 0)
            {
                // matcha / batchi などの「tch」は「っち」にする。
                if (_romajiBuffer.StartsWith(
                    "tch",
                    StringComparison.Ordinal))
                {
                    CommitRaw(
                        "っ");

                    _romajiBuffer =
                        _romajiBuffer.Substring(1);

                    continue;
                }

                // -------------------------------------------------
                // 促音
                //
                // tta  -> った
                // kka  -> っか
                // sshi -> っし
                // cchi -> っち
                // -------------------------------------------------

                if (_romajiBuffer.Length >= 2 &&
                    _romajiBuffer[0] == _romajiBuffer[1] &&
                    _romajiBuffer[0] != 'n' &&
                    !IsVowel(_romajiBuffer[0]))
                {
                    CommitRaw(
                        "っ");

                    _romajiBuffer =
                        _romajiBuffer.Substring(1);

                    continue;
                }

                // -------------------------------------------------
                // 完全一致
                // -------------------------------------------------

                if (RomajiMap.TryGetValue(
                        _romajiBuffer,
                        out string kana))
                {
                    // j など、より長い入力候補が存在する場合は待つ
                    if (HasLongerRomaji(
                        _romajiBuffer))
                    {
                        ShowRomajiComposing();
                        return;
                    }

                    CommitRaw(
                        kana);

                    _romajiBuffer =
                        "";

                    return;
                }

                // -------------------------------------------------
                // まだ辞書の途中
                //
                // k
                // ky
                // sh
                // ny
                // n
                //
                // -------------------------------------------------

                if (IsRomajiPrefix(
                    _romajiBuffer))
                {
                    ShowRomajiComposing();
                    return;
                }

                // -------------------------------------------------
                // n 特殊
                //
                // nk → ん + k
                // nn → ん + n
                //
                // -------------------------------------------------

                if (_romajiBuffer[0] ==
                    'n')
                {
                    if (_romajiBuffer == "nn")
                    {
                        CommitRaw(
                            "ん");

                        // 次の母音/yだけに利用するnとして内部保持する。
                        // 画面には余分な「n」を表示しない。
                        _romajiBuffer = "n";
                        _pendingSecondN = true;
                        return;
                    }

                    CommitRaw(
                        "ん");

                    _romajiBuffer =
                        _romajiBuffer.Substring(
                            1);

                    continue;
                }

                // -------------------------------------------------
                // 先頭側に確定可能な辞書語があるか
                //
                // jk
                //
                // j → じ
                // k → 次の状態
                //
                // -------------------------------------------------

                string matchedKey =
                    null;

                foreach (var pair in
                    RomajiMap)
                {
                    string mapKey =
                        pair.Key;

                    if (_romajiBuffer.StartsWith(
                        mapKey,
                        StringComparison.Ordinal))
                    {
                        if (matchedKey == null ||
                            mapKey.Length >
                            matchedKey.Length)
                        {
                            matchedKey =
                                mapKey;
                        }
                    }
                }

                if (matchedKey != null)
                {
                    CommitRaw(
                        RomajiMap[
                            matchedKey]);

                    _romajiBuffer =
                        _romajiBuffer.Substring(
                            matchedKey.Length);

                    continue;
                }

                // -------------------------------------------------
                // 辞書にもprefixにもならない
                // 先頭一文字をそのまま確定
                // -------------------------------------------------

                string literal =
                    _romajiBuffer[0]
                        .ToString();

                CommitRaw(
                    literal);

                _romajiBuffer =
                    _romajiBuffer.Substring(
                        1);
            }
        }

        // =========================================================
        // prefix判定
        // =========================================================

        private bool IsRomajiPrefix(
            string text)
        {
            if (text == "tc" ||
                text == "tch")
            {
                return true;
            }

            // n は単体では待つ
            if (text == "n")
                return true;

            foreach (string key in
                RomajiMap.Keys)
            {
                if (key.StartsWith(
                    text,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsVowel(
            char value)
        {
            return value is
                'a' or 'i' or 'u' or 'e' or 'o';
        }

        // =========================================================
        // より長い候補
        // =========================================================

        private bool HasLongerRomaji(
            string text)
        {
            foreach (string key in
                RomajiMap.Keys)
            {
                if (key.Length <=
                    text.Length)
                {
                    continue;
                }

                if (key.StartsWith(
                    text,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // =========================================================
        // composing
        // =========================================================

        private void ShowRomajiComposing()
        {
            var ic = CurrentInputConnection;
            if (ic is null) return;
            var selection = GetEditorSelection(ic);
            int first = _romajiDraftStart >= 0 ? _romajiDraftStart : Math.Min(selection.Start, selection.End);
            int last = _romajiDraftStart >= 0 ? first + _romajiDraftLength : Math.Max(selection.Start, selection.End);
            if (first < 0)
            {
                ic.SetComposingText(new Java.Lang.String(_romajiBuffer), 1);
                ClearCandidates();
                return;
            }
            _suppressSelectionUpdates = true;
            try
            {
                if (_romajiDraftStart < 0) FinishNativeComposition(ic);
                else ic.SetComposingRegion(first, last);
                _lastNativeSpan = null; _nativeRestoreSelectionRevision = -1;
                if (!ic.SetComposingText(new Java.Lang.String(_romajiBuffer), 1)) return;
                _composition.ApplyEdit(first, last, _romajiBuffer, first + _romajiBuffer.Length);
                _romajiDraftStart = _romajiBuffer.Length == 0 ? -1 : first;
                _romajiDraftLength = _romajiBuffer.Length;
                SyncSelectionPosition();
                _compositionVersion++;
                ClearCandidates();
                if (_romajiBuffer.Length == 0) RestoreNativeComposition(ic);
            }
            finally { _suppressSelectionUpdates = false; }
        }

        private void CommitRaw(string text)
        {
            var ic = CurrentInputConnection;
            if (ic is null) return;
            var selection = GetEditorSelection(ic);
            int first = _romajiDraftStart >= 0 ? _romajiDraftStart : Math.Min(selection.Start, selection.End);
            int last = _romajiDraftStart >= 0 ? first + _romajiDraftLength : Math.Max(selection.Start, selection.End);
            if (first < 0)
            {
                ic.CommitText(new Java.Lang.String(text), 1);
                _composition.Reset();
                ClearCandidates();
                return;
            }
            _suppressSelectionUpdates = true;
            try
            {
                if (_romajiDraftStart < 0) FinishNativeComposition(ic);
                else ic.SetComposingRegion(first, last);
                _lastNativeSpan = null; _nativeRestoreSelectionRevision = -1;
                if (!ic.CommitText(new Java.Lang.String(text), 1)) return;
                _composition.ApplyEdit(first, last, text, first + text.Length);
                _romajiDraftStart = -1; _romajiDraftLength = 0;
                SyncSelectionPosition();
                _compositionVersion++;
                ClearCandidates();
                RestoreNativeComposition(ic);
            }
            finally { _suppressSelectionUpdates = false; }
        }

        private sealed class CompositionEditor(IInputConnection ic) : ICompositionEditor
        {
            public bool BeginBatchEdit() => ic.BeginBatchEdit();
            public bool EndBatchEdit() => ic.EndBatchEdit();
            public bool FinishComposingText() => ic.FinishComposingText();
            public bool SetSelection(int start, int end) => ic.SetSelection(start, end);
            public bool CommitText(string text) => ic.CommitText(new Java.Lang.String(text), 1);
            public bool SetComposingRegion(int start, int end) => ic.SetComposingRegion(start, end);
        }

        private void RestoreNativeComposition(IInputConnection ic)
        {
            // SetComposingRegion changes spans, not text or selection. Avoid repeating identical
            // markers when a composing-span callback itself triggers OnUpdateSelection.
            var span = _composition.NativeComposingSpan;
            if (span == _lastNativeSpan && _nativeRestoreSelectionRevision == _composition.SelectionRevision) return;
            _lastNativeSpan = span;
            _nativeRestoreSelectionRevision = _composition.SelectionRevision;
            if (!CompositionCommitter.Restore(_composition, new CompositionEditor(ic)))
                Android.Util.Log.Warn("NyaIme", "Editor did not restore composing span; internal conversion remains active.");
        }

        private void FinishNativeComposition(IInputConnection ic)
        {
            ic.FinishComposingText();
            _lastNativeSpan = null; _nativeRestoreSelectionRevision = -1;
        }

        private void SyncSelectionPosition()
        {
            _lastSelectionPosition = _composition.SelectionStart == _composition.SelectionEnd ? _composition.SelectionStart : -1;
        }

        private (int Start, int End, bool Verified) GetEditorSelection(IInputConnection ic)
        {
            try
            {
                var extracted = ic.GetExtractedText(new ExtractedTextRequest(), GetTextFlags.None);
                if (extracted is not null && extracted.SelectionStart >= 0 && extracted.SelectionEnd >= 0)
                    return (extracted.StartOffset + extracted.SelectionStart, extracted.StartOffset + extracted.SelectionEnd, true);
            }
            catch (Exception e) when (e is Java.Lang.RuntimeException or ObjectDisposedException) { }
            int cursor = _lastSelectionPosition >= 0 ? _lastSelectionPosition : _composition.SelectionStart;
            return (cursor, cursor, false);
        }

        private string? ReadTrackedText(IInputConnection ic)
        {
            if (!_composition.SelectionWithin) return null;
            var selection = GetEditorSelection(ic);
            if (selection.Start != _composition.SelectionStart || selection.End != _composition.SelectionEnd) return null;
            int first = Math.Min(selection.Start, selection.End), last = Math.Max(selection.Start, selection.End);
            int leftLength = first - _composition.Start, rightLength = _composition.End - last;
            string left = leftLength == 0 ? "" : ic.GetTextBeforeCursor(leftLength, GetTextFlags.None)?.ToString() ?? "";
            string selected = first == last ? "" : ic.GetSelectedText(GetTextFlags.None)?.ToString() ?? "";
            string right = rightLength == 0 ? "" : ic.GetTextAfterCursor(rightLength, GetTextFlags.None)?.ToString() ?? "";
            if (left.Length != leftLength || selected.Length != last - first || right.Length != rightLength) return null;
            return left + selected + right;
        }

        private string? ReadCompositionSource(IInputConnection ic)
        {
            var target = _composition.GetTarget();
            return target is not null && ReadTrackedText(ic) == _composition.Text ? target.Source : null;
        }

        private void FlushRomaji()
        {
            if (_pendingSecondN)
            {
                // 「nn」の最初のnは既に「ん」として確定済み。
                _romajiBuffer = "";
                _pendingSecondN = false;
                return;
            }

            if (_romajiBuffer.Length ==
                0)
            {
                return;
            }

            if (_romajiBuffer ==
                "n")
            {
                CommitRaw(
                    "ん");

                _romajiBuffer =
                    "";

                return;
            }

            if (RomajiMap.TryGetValue(
                    _romajiBuffer,
                    out string kana))
            {
                CommitRaw(
                    kana);

                _romajiBuffer =
                    "";

                return;
            }

            CommitRaw(
                _romajiBuffer);

            _romajiBuffer =
                "";
        }

        // =========================================================
        // Backspace
        // =========================================================

        private void Backspace()
        {
            var ic = CurrentInputConnection;
            if (ic is null) return;
            if (_pendingSecondN) { _romajiBuffer = ""; _pendingSecondN = false; }
            else if (_romajiBuffer.Length > 0)
            {
                _romajiBuffer = _romajiBuffer[..^1];
                ShowRomajiComposing();
                if (_romajiBuffer.Length == 0) RestoreNativeComposition(ic);
                ScheduleAutoConversion();
                return;
            }
            var selection = GetEditorSelection(ic);
            int first = Math.Min(selection.Start, selection.End), last = Math.Max(selection.Start, selection.End);
            if (first < 0) { SendKey(Keycode.Del); return; }
            bool selected = first != last;
            int count = 0;
            if (!selected)
            {
                string before = ic.GetTextBeforeCursor(2, GetTextFlags.None)?.ToString() ?? "";
                if (before.Length == 0) return;
                count = before.Length >= 2 && char.IsSurrogatePair(before[^2], before[^1]) ? 2 : 1;
                first -= count;
            }
            _suppressSelectionUpdates = true;
            try
            {
                FinishNativeComposition(ic);
                bool edited = selected ? ic.CommitText(new Java.Lang.String(""), 1) : ic.DeleteSurroundingText(count, 0);
                if (!edited) return;
                _composition.ApplyEdit(first, last, "", first);
                SyncSelectionPosition();
                _compositionVersion++;
                ClearCandidates();
                RestoreNativeComposition(ic);
            }
            finally { _suppressSelectionUpdates = false; }
            ScheduleAutoConversion();
        }

        private void HalfWidthSpace()
        {
            FlushRomaji();

            CommitRaw(
                " ");

            ScheduleAutoConversion();
        }

        // =========================================================
        // AI補完用マーカー
        // =========================================================

        private void InputMissingTextMarker()
        {
            FlushRomaji();

            CommitRaw(
                "<m>");

            ScheduleAutoConversion();
        }

        // =========================================================
        // Enter
        // =========================================================

        private void Enter()
        {
            var ic = CurrentInputConnection;
            if (ic is null) return;
            bool hadDraft = _romajiBuffer.Length > 0 || _pendingSecondN;
            FlushRomaji();
            CompositionCommitter.EnterAction action;
            _suppressSelectionUpdates = true;
            try
            {
                action = CompositionCommitter.FinishRaw(_composition, new CompositionEditor(ic), hadDraft);
            }
            finally { _suppressSelectionUpdates = false; }
            if (action == CompositionCommitter.EnterAction.Failed) return;
            _lastNativeSpan = null; _nativeRestoreSelectionRevision = -1;
            _romajiDraftStart = -1; _romajiDraftLength = 0;
            _romajiBuffer = ""; _pendingSecondN = false;
            _compositionVersion++;
            _autoConversionVersion++;
            ClearCandidates();
            if (action == CompositionCommitter.EnterAction.SendEnter) SendKey(Keycode.Enter);
        }

        // =========================================================
        // カーソル
        // =========================================================

        private void MoveLeft() => MoveCursor(false);
        private void MoveRight() => MoveCursor(true);

        private void MoveCursor(bool right)
        {
            FlushRomaji();
            var ic = CurrentInputConnection;
            if (ic is null) return;
            var selection = GetEditorSelection(ic);
            if (selection.Start < 0) { SendKey(right ? Keycode.DpadRight : Keycode.DpadLeft); return; }
            int target;
            if (selection.Start != selection.End)
                target = right ? Math.Max(selection.Start, selection.End) : Math.Min(selection.Start, selection.End);
            else
            {
                string adjacent = (right ? ic.GetTextAfterCursor(2, GetTextFlags.None) : ic.GetTextBeforeCursor(2, GetTextFlags.None))?.ToString() ?? "";
                if (adjacent.Length == 0) return;
                int step = adjacent.Length >= 2 && (right ? char.IsSurrogatePair(adjacent[0], adjacent[1]) : char.IsSurrogatePair(adjacent[^2], adjacent[^1])) ? 2 : 1;
                target = Math.Max(0, selection.Start + (right ? step : -step));
            }
            _suppressSelectionUpdates = true;
            try
            {
                if (!ic.SetSelection(target, target))
                {
                    SendKey(right ? Keycode.DpadRight : Keycode.DpadLeft);
                    return; // The selection callback supplies the editor's actual result.
                }
                _composition.MoveSelection(target, target);
                SyncSelectionPosition();
                ClearCandidates();
                RestoreNativeComposition(ic);
            }
            finally { _suppressSelectionUpdates = false; }
            if (_composition.GetTarget() is not null) ScheduleAutoConversion();
        }

        private void SendKey(
            Keycode key)
        {
            var ic =
                CurrentInputConnection;

            if (ic == null)
                return;

            ic.SendKeyEvent(
                new KeyEvent(
                    KeyEventActions.Down,
                    key));

            ic.SendKeyEvent(
                new KeyEvent(
                    KeyEventActions.Up,
                    key));
        }

        // =========================================================
        // 左列
        //
        // LayoutParamsはここでは設定しない。
        // 親側で0.65 weightを与える。
        // =========================================================

        private View CreateLeftColumn()
        {
            var column =
                new LinearLayout(this);

            column.Orientation =
                Orientation.Vertical;

            column.SetBackgroundColor(
                Color.Rgb(
                    235,
                    239,
                    242));

            column.AddView(
                CreateConversionModeKey(),
                VerticalWeighted());

            column.AddView(
                CreateFunctionKey(
                    "<m>",
                    InputMissingTextMarker),
                VerticalWeighted());

            column.AddView(
                CreateFunctionKey(
                    "◀",
                    MoveLeft),
                VerticalWeighted());

            if (_numericMode)
            {
                column.AddView(
                    CreateFunctionKey(
                        _emojiMode
                            ? "123"
                            : "絵文字",
                        ToggleEmojiMode),
                    VerticalWeighted());
            }
            else
            {
                column.AddView(
                    CreateFunctionKey(
                        "123",
                        ToggleNumericMode),
                    VerticalWeighted());
            }

            column.AddView(
                CreateFunctionKey(
                    _halfWidthAlphaMode
                        ? "aあ"
                        : "あa",
                    ToggleAlphaMode),
                VerticalWeighted());

            return column;
        }

        private Button CreateConversionModeKey()
        {
            // The narrow side column needs two short lines, rather than a clipped 21sp label.
            var button = CreateFunctionKey(_offlineMode ? "オフ\nライン" : "AI", ToggleConversionMode);
            button.TextSize = 13;
            button.SetAllCaps(false);
            button.SetSingleLine(false);
            button.SetMaxLines(2);
            button.SetMinWidth(0);
            button.SetMinHeight(0);
            button.ContentDescription = _offlineMode ? "オフライン変換。タップでAI変換に切替" : "AI変換。タップでオフライン変換に切替";
            return button;
        }

        private void ToggleConversionMode()
        {
            _offlineMode = !_offlineMode;
            _conversionPhase = "入力後に候補を表示";
            GetSharedPreferences("conversion", FileCreationMode.Private)!.Edit()!
                .PutBoolean("offline", _offlineMode)!.Apply();
            _conversionCancellation?.Cancel();
            _conversionRequest++;
            _autoConversionVersion++;
            ClearCandidates();
            RebuildInputView();
            ScheduleAutoConversion();
        }

        private void ToggleNumericMode()
        {
            FlushRomaji();
            FinishCurrentConversionSegment();
            _numericMode = true;
            _emojiMode = false;
            RebuildInputView();
        }

        private void ToggleEmojiMode()
        {
            FlushRomaji();
            FinishCurrentConversionSegment();
            _numericMode = true;
            _emojiMode =
                !_emojiMode;

            if (_emojiMode)
            {
                LoadEmojiHistory();
                _showEmojiHistory =
                    _emojiHistory.Count > 0;
            }

            RebuildInputView();
        }

        private void ToggleAlphaMode()
        {
            FlushRomaji();
            FinishCurrentConversionSegment();
            _halfWidthAlphaMode =
                !_halfWidthAlphaMode;
            _numericMode = false;
            _emojiMode = false;
            RebuildInputView();
        }

        private void FinishCurrentConversionSegment()
        {
            if (CurrentInputConnection is not null) FinishNativeComposition(CurrentInputConnection);
            _composition.Reset();
            _romajiDraftStart = -1; _romajiDraftLength = 0;
            _compositionVersion++;
            _autoConversionVersion++;
            _romajiBuffer = "";
            _pendingSecondN = false;
            ClearCandidates();
        }

        private void RebuildInputView()
        {
            SetInputView(
                OnCreateInputView());
        }

        // =========================================================
        // 右列
        // =========================================================

        private View CreateRightColumn()
        {
            var column =
                new LinearLayout(this);

            column.Orientation =
                Orientation.Vertical;

            column.SetBackgroundColor(
                Color.Rgb(
                    235,
                    239,
                    242));

            column.AddView(
                CreateRepeatingFunctionKey(
                    "⌫",
                    Backspace),
                VerticalWeighted());

            column.AddView(
                CreateFunctionKey(
                    "▶",
                    MoveRight),
                VerticalWeighted());

            column.AddView(
                CreateFunctionKey(
                    "半",
                    HalfWidthSpace),
                VerticalWeighted());

            column.AddView(
                CreateFunctionKey(
                    "↵",
                    Enter),
                VerticalWeighted());

            return column;
        }

        // =========================================================
        // 上部バー
        // =========================================================

        private View CreateToolbar()
        {
            var bar =
                new LinearLayout(this);

            bar.Orientation =
                Orientation.Vertical;

            _conversionModeStatus = new TextView(this) { TextSize = 12 };
            _conversionModeStatus.SetPadding(Dp(8), Dp(2), Dp(8), Dp(2));
            bar.AddView(_conversionModeStatus);
            UpdateConversionModeStatus();

            bar.SetBackgroundColor(
                Color.Rgb(
                    215,
                    222,
                    226));

            _candidateBar =
                new LinearLayout(this);

            _candidateBar.Orientation =
                Orientation.Vertical;

            _candidateBar.SetBackgroundColor(
                Color.Rgb(
                    250,
                    251,
                    252));

            _candidateScroll =
                new ScrollView(this)
                {
                    FillViewport = true,
                    VerticalScrollBarEnabled = true,
                    SmoothScrollingEnabled = true,
                };

            _candidateScroll.AddView(
                _candidateBar,
                new ScrollView.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent));

            _emojiGrid =
                new GridView(this)
                {
                    NumColumns = 8,
                    Visibility = ViewStates.Gone,
                };

            _emojiGrid.ItemClick += (_, e) =>
            {
                if (e.Position >= 0 &&
                    e.Position < _visibleEmojiCandidates.Count)
                {
                    InputEmoji(
                        _visibleEmojiCandidates[e.Position]);
                }
            };

            _emojiGrid.ItemLongClick += (_, e) =>
            {
                if (e.Position >= 0 &&
                    e.Position < _visibleEmojiCandidates.Count)
                {
                    ShowEmojiVariants(
                        _visibleEmojiCandidates[e.Position]);
                    e.Handled = true;
                }
            };

            var emojiTabs =
                new LinearLayout(this)
                {
                    Orientation = Orientation.Horizontal,
                };
            _emojiHistoryButton =
                new Button(this)
                {
                    Text = "履歴",
                    TextSize = 13,
                };
            _emojiAllButton =
                new Button(this)
                {
                    Text = "一覧",
                    TextSize = 13,
                };
            _emojiHistoryButton.SetAllCaps(false);
            _emojiAllButton.SetAllCaps(false);
            _emojiHistoryButton.Click += (_, _) =>
            {
                _showEmojiHistory = true;
                ShowEmojiCandidates();
            };
            _emojiAllButton.Click += (_, _) =>
            {
                _showEmojiHistory = false;
                ShowEmojiCandidates();
            };
            emojiTabs.AddView(
                _emojiHistoryButton,
                HorizontalWeighted());
            emojiTabs.AddView(
                _emojiAllButton,
                HorizontalWeighted());

            _emojiPanel =
                new LinearLayout(this)
                {
                    Orientation = Orientation.Vertical,
                    Visibility = ViewStates.Gone,
                };
            _emojiPanel.AddView(
                emojiTabs,
                new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    Dp(42)));
            _emojiPanel.AddView(
                _emojiGrid,
                new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    0,
                    1f));

            _candidatePanel =
                new FrameLayout(this);
            _candidatePanel.AddView(
                _candidateScroll,
                new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent));
            _candidatePanel.AddView(
                _emojiPanel,
                new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent));

            bar.AddView(
                _candidatePanel,
                new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    Dp(ToolbarHeightDp),
                    0f));

            if (_emojiMode)
            {
                ShowEmojiCandidates();
            }
            else if (_candidates.Count > 0)
            {
                ShowCandidates();
            }
            else
            {
                ShowConversionStatus(
                    _offlineMode ? "オフライン: 入力後に候補を表示" : "AI変換: 入力後に候補を表示");
            }

            return bar;
        }

        // =========================================================
        // AIかな漢字変換
        // =========================================================

        private async void ScheduleAutoConversion()
        {
            int scheduleVersion =
                ++_autoConversionVersion;

            try
            {
                await System.Threading.Tasks.Task.Delay(
                    ConversionIntervalMilliseconds);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (scheduleVersion !=
                _autoConversionVersion ||
                _destroyed)
            {
                return;
            }

            await ConvertCompositionAsync();
        }

        private async System.Threading.Tasks.Task
            ConvertCompositionAsync()
        {
            FlushRomaji();

            var target = _composition.GetTarget();
            if (target is null)
            {
                return;
            }

            var ic =
                CurrentInputConnection;

            if (ic == null)
            {
                return;
            }

            string source = ReadCompositionSource(ic) ?? "";

            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }


            _candidateSourceLength =
                source.Length;
            _conversionCancellation?.Cancel();
            _conversionCancellation?.Dispose();
            _conversionCancellation = new CancellationTokenSource();
            CancellationToken token = _conversionCancellation.Token;
            int request = ++_conversionRequest;
            int version = _compositionVersion;
            bool offline = _offlineMode;
            int modeGeneration = _conversionMode.Generation;
            bool IsCurrentRequest() => request == _conversionRequest && version == _compositionVersion &&
                _conversionMode.IsCurrent(modeGeneration, offline) && _composition.IsCurrent(target) && IsCurrentConversionSource(source);
            IReadOnlyList<string> lastStreamedCandidates =
                Array.Empty<string>();
            ShowConversionStatus(
                offline ? "オフライン変換中…" : "AI変換中…");

            try
            {
                var candidates =
                    await GetConversionCandidatesAsync(
                            source,
                            offline,
                            streamedCandidates =>
                            {
                                if (_destroyed)
                                {
                                    return false;
                                }

                                IReadOnlyList<string> snapshot =
                                    streamedCandidates.ToArray();
                                lastStreamedCandidates = snapshot;

                                PostToMainThread(() =>
                                {
                                    if (!IsCurrentRequest())
                                    {
                                        return;
                                    }

                                    _candidates = snapshot;
                                    _candidateSource = source;
                                    _candidateTarget = target;
                                    _candidateCompositionVersion = version;
                                    ShowCandidates();
                                });
                                return true;
                            },
                            token);

                if (_destroyed)
                {
                    return;
                }

                IReadOnlyList<string> finalCandidates =
                    candidates.Count == 0
                        ? new[] { source }
                        : candidates.ToArray();

                PostToMainThread(() =>
                {
                    if (!IsCurrentRequest())
                    {
                        return;
                    }

                    _candidates = finalCandidates;
                    _candidateSource = source;
                    _candidateTarget = target;
                    _candidateCompositionVersion = version;

                    if (candidates.Count == 0)
                    {
                        Toast.MakeText(
                                this,
                                "候補を取得できませんでした",
                                ToastLength.Short)?
                            .Show();
                    }

                    ShowCandidates();
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
                // IME終了と通信完了が重なった場合は結果を破棄する。
            }
            catch (Exception exception)
            {
                Android.Util.Log.Error(
                    "NyaIme",
                    $"AI conversion failed: {exception}");

                if (!_destroyed)
                {
                    PostToMainThread(() =>
                    {
                        if (!IsCurrentRequest())
                        {
                            return;
                        }

                        _candidates =
                            lastStreamedCandidates.Count > 0
                                ? lastStreamedCandidates
                                : new[] { source };
                        _candidateSource = source;
                        _candidateTarget = target;
                        _candidateCompositionVersion = version;
                        ShowCandidates();
                    });
                }
            }
        }

        private async Task<IReadOnlyList<string>> GetConversionCandidatesAsync(
            string source, bool offline, Func<IReadOnlyList<string>, bool> changed, CancellationToken token)
        {
            IReadOnlyList<string> local;
            try
            {
                var engine = await _offlineEngine!.WaitAsync(token);
                local = await Task.Run(() => engine.GetCandidates(source, token), token);
                // Predictions are explicit completions, placed after exact conversion and raw input.
                var predicted = await Task.Run(() => engine.Predict(source, token), token);
                token.ThrowIfCancellationRequested();
                _predictions.Clear();
                foreach (string candidate in predicted.Except(local, StringComparer.Ordinal)) _predictions.Add(candidate);
                local = local.Concat(predicted).Distinct(StringComparer.Ordinal).Take(12).ToArray();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                Android.Util.Log.Error("NyaIme", $"Offline dictionary unavailable: {exception.GetType().Name}");
                local = new[] { source };
            }
            return await ConversionRouting.ConvertAsync(source, offline, local, changed,
                (callback, cancellation) => _kanaKanjiClient.GetCandidatesStreamingAsync(source, callback, cancellation), token,
                stage => PostToMainThread(() =>
                {
                    if (token.IsCancellationRequested || offline != _offlineMode) return;
                    if (stage == ConversionRouting.Stage.Online) _predictions.Clear();
                    _conversionPhase = stage switch
                    {
                        ConversionRouting.Stage.Local => "ローカル候補",
                        ConversionRouting.Stage.OnlinePending => "AI応答待ち・ローカル候補",
                        ConversionRouting.Stage.Online => "AI候補",
                        _ => "AI応答なし・ローカル候補",
                    };
                    UpdateConversionModeStatus();
                }));
        }

        private bool IsCurrentConversionSource(string source)
        {
            if (_destroyed || source.Length == 0 || CurrentInputConnection is null) return false;
            string? current = ReadCompositionSource(CurrentInputConnection);
            return current is not null && current == source;
        }

        private void PostToMainThread(Action action)
        {
            Handler? handler = _mainHandler;

            if (_destroyed || handler == null)
            {
                return;
            }

            if (Looper.MyLooper() == Looper.MainLooper)
            {
                action();
                return;
            }

            handler.Post(action);
        }

        private void ShowConversionStatus(
            string text)
        {
            if (_candidateBar == null)
            {
                return;
            }

            ShowCandidateScroll();
            _candidateBar.RemoveAllViews();
            SetCandidatePanelHeight(
                ToolbarHeightDp);

            var label =
                new TextView(this);

            label.Text = text;
            label.Gravity =
                GravityFlags.CenterVertical;
            label.SetPadding(
                Dp(8),
                0,
                Dp(8),
                0);

            _candidateBar.AddView(
                label,
                new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    Dp(ToolbarHeightDp)));
        }

        private void ShowCandidates()
        {
            UpdateConversionModeStatus();
            if (_candidateBar == null)
            {
                return;
            }

            ShowCandidateScroll();
            _candidateBar.RemoveAllViews();

            bool useLongTextLayout =
                _candidateSourceLength >=
                LongCandidateTextLength;
            int columnCount =
                useLongTextLayout ? 3 : 4;
            int rowCount =
                useLongTextLayout ? 3 : 2;

            int longestCandidateLength = 0;

            foreach (string candidate in _candidates)
            {
                longestCandidateLength =
                    Math.Max(
                        longestCandidateLength,
                        candidate.Length);
            }

            // 文字を省略せず折り返す。長文時は候補欄を広げ、
            // 画面に収まらない高さだけScrollViewに任せる。
            int approximateCharactersPerLine =
                useLongTextLayout ? 12 : 9;
            int estimatedLineCount =
                Math.Max(
                    1,
                    (longestCandidateLength +
                        approximateCharactersPerLine - 1) /
                    approximateCharactersPerLine);
            int candidateRowHeightDp =
                Math.Max(
                    CandidateRowMinimumHeightDp,
                    18 * estimatedLineCount + 18);
            int desiredPanelHeightDp =
                Math.Clamp(
                    candidateRowHeightDp * rowCount,
                    useLongTextLayout
                        ? ExpandedToolbarHeightDp
                        : ToolbarHeightDp,
                    MaximumToolbarHeightDp);

            SetCandidatePanelHeight(
                desiredPanelHeightDp);

            for (int rowIndex = 0;
                 rowIndex < rowCount;
                 rowIndex++)
            {
                var row =
                    new LinearLayout(this);

                row.Orientation =
                    Orientation.Horizontal;

                for (int columnIndex = 0;
                     columnIndex < columnCount;
                     columnIndex++)
                {
                    int index =
                        rowIndex * columnCount +
                        columnIndex;

                    if (index >= _candidates.Count)
                    {
                        var empty =
                            new Space(this);

                        row.AddView(
                            empty,
                            new LinearLayout.LayoutParams(
                                0,
                                ViewGroup.LayoutParams.MatchParent,
                                1f));
                        continue;
                    }

                    int candidateIndex = index;
                    var button =
                        new Button(this);

                    button.Text =
                        $"{index + 1}. {(_predictions.Contains(_candidates[index]) ? "予測: " : "")}{_candidates[index]}";
                    button.TextSize = 12;
                    button.SetAllCaps(false);
                    button.SetSingleLine(false);
                    button.Ellipsize = null;
                    button.SetMinHeight(
                        Dp(CandidateRowMinimumHeightDp));
                    button.Gravity =
                        GravityFlags.Center;
                    button.SetTextColor(
                        Color.Rgb(
                            25,
                            42,
                            54));

                    var background =
                        new GradientDrawable();

                    background.SetColor(
                        index == 0
                            ? Color.Rgb(225, 239, 252)
                            : Color.White);
                    background.SetStroke(
                        Dp(1),
                        Color.Rgb(190, 200, 207));
                    background.SetCornerRadius(
                        Dp(6));

                    button.Background =
                        background;
                    button.SetPadding(
                        Dp(3),
                        0,
                        Dp(3),
                        0);
                    button.Click += (_, _) =>
                    {
                        SelectCandidate(
                            candidateIndex);
                    };

                    var cellLayout =
                        new LinearLayout.LayoutParams(
                            0,
                            ViewGroup.LayoutParams.WrapContent,
                            1f);

                    cellLayout.SetMargins(
                        Dp(2),
                        Dp(2),
                        Dp(2),
                        Dp(2));

                    row.AddView(
                        button,
                        cellLayout);
                }

                _candidateBar.AddView(
                    row,
                    new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent,
                        ViewGroup.LayoutParams.WrapContent));
            }
        }

        private void ShowEmojiCandidates()
        {
            if (_emojiGrid == null ||
                _candidateScroll == null ||
                _emojiPanel == null)
            {
                return;
            }

            LoadEmojiData();
            LoadEmojiHistory();

            if (_showEmojiHistory &&
                _emojiHistory.Count == 0)
            {
                _showEmojiHistory = false;
            }

            _candidateScroll.Visibility =
                ViewStates.Gone;
            _emojiPanel.Visibility =
                ViewStates.Visible;
            _emojiGrid.Visibility =
                ViewStates.Visible;
            _visibleEmojiCandidates =
                _showEmojiHistory
                    ? _emojiHistory
                    : _emojiCandidates;
            _emojiGrid.Adapter =
                new EmojiGridAdapter(
                    this,
                    _visibleEmojiCandidates);
            _emojiHistoryButton.Enabled =
                !_showEmojiHistory;
            _emojiAllButton.Enabled =
                _showEmojiHistory;
            SetCandidatePanelHeight(
                MaximumToolbarHeightDp);
            _emojiGrid.SetSelection(0);
        }

        private void ShowCandidateScroll()
        {
            if (_candidateScroll != null)
            {
                _candidateScroll.Visibility =
                    ViewStates.Visible;
            }

            if (_emojiPanel != null)
            {
                _emojiPanel.Visibility =
                    ViewStates.Gone;
            }
        }

        private void LoadEmojiHistory()
        {
            if (_emojiHistoryLoaded)
            {
                return;
            }

            _emojiHistoryLoaded = true;
            var preferences =
                GetSharedPreferences(
                    EmojiHistoryPreferences,
                    FileCreationMode.Private);
            string serialized =
                preferences?.GetString(
                    EmojiHistoryKey,
                    "") ?? "";

            foreach (string emoji in serialized.Split(
                '\u001F',
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (!_emojiHistory.Contains(
                        emoji,
                        StringComparer.Ordinal))
                {
                    _emojiHistory.Add(emoji);
                }
            }
        }

        private void RememberEmoji(
            string emoji)
        {
            LoadEmojiHistory();

            _emojiHistory.RemoveAll(
                value => string.Equals(
                    value,
                    emoji,
                    StringComparison.Ordinal));
            _emojiHistory.Insert(
                0,
                emoji);

            if (_emojiHistory.Count >
                MaximumEmojiHistoryCount)
            {
                _emojiHistory.RemoveRange(
                    MaximumEmojiHistoryCount,
                    _emojiHistory.Count -
                        MaximumEmojiHistoryCount);
            }

            var preferences =
                GetSharedPreferences(
                    EmojiHistoryPreferences,
                    FileCreationMode.Private);
            preferences?.Edit()?
                .PutString(
                    EmojiHistoryKey,
                    string.Join(
                        '\u001F',
                        _emojiHistory))?
                .Apply();
        }

        private void LoadEmojiData()
        {
            if (_emojiCandidates.Count > 0)
            {
                return;
            }

            var baseEmojis =
                new List<string>();
            var baseSet =
                new HashSet<string>(
                    StringComparer.Ordinal);
            var variants =
                new Dictionary<string, List<string>>(
                    StringComparer.Ordinal);

            try
            {
                using var stream =
                    Assets.Open("emoji-test.txt");
                using var reader =
                    new StreamReader(stream);

                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    int semicolonIndex =
                        line.IndexOf(';');
                    int commentIndex =
                        line.IndexOf('#');

                    if (semicolonIndex < 0 ||
                        commentIndex < semicolonIndex)
                    {
                        continue;
                    }

                    string status =
                        line.Substring(
                                semicolonIndex + 1,
                                commentIndex - semicolonIndex - 1)
                            .Trim();

                    if (!string.Equals(
                            status,
                            "fully-qualified",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string[] hexadecimalCodePoints =
                        line.Substring(0, semicolonIndex)
                            .Trim()
                            .Split(
                                ' ',
                                StringSplitOptions.RemoveEmptyEntries);
                    var codePoints =
                        new List<int>(
                            hexadecimalCodePoints.Length);

                    foreach (string hexadecimal in
                        hexadecimalCodePoints)
                    {
                        codePoints.Add(
                            int.Parse(
                                hexadecimal,
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture));
                    }

                    string emoji =
                        BuildEmoji(codePoints);
                    bool hasSkinTone =
                        codePoints.Any(IsEmojiSkinTone);
                    bool hasJoiner =
                        codePoints.Contains(0x200D);

                    if (!hasSkinTone &&
                        !hasJoiner)
                    {
                        if (baseSet.Add(emoji))
                        {
                            baseEmojis.Add(emoji);
                        }

                        continue;
                    }

                    var baseCodePoints =
                        new List<int>();

                    foreach (int codePoint in codePoints)
                    {
                        if (hasJoiner &&
                            codePoint == 0x200D)
                        {
                            break;
                        }

                        if (!IsEmojiSkinTone(codePoint))
                        {
                            baseCodePoints.Add(codePoint);
                        }
                    }

                    string baseEmoji =
                        BuildEmoji(baseCodePoints);

                    if (string.IsNullOrEmpty(baseEmoji))
                    {
                        continue;
                    }

                    if (!variants.TryGetValue(
                            baseEmoji,
                            out List<string>? variantList))
                    {
                        variantList =
                            new List<string>();
                        variants[baseEmoji] =
                            variantList;
                    }

                    if (!variantList.Contains(
                            emoji,
                            StringComparer.Ordinal))
                    {
                        variantList.Add(emoji);
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                FormatException)
            {
                Android.Util.Log.Error(
                    "NyaIme",
                    $"Emoji data load failed: {exception}");
            }

            foreach (string baseEmoji in variants.Keys)
            {
                if (baseSet.Add(baseEmoji))
                {
                    baseEmojis.Add(baseEmoji);
                }
            }

            if (baseEmojis.Count == 0)
            {
                baseEmojis.AddRange(
                    new[]
                    {
                        "😀", "😂", "😊", "😍",
                        "👍", "🙏", "❤️", "🎉",
                    });
            }

            _emojiCandidates =
                baseEmojis;
            _emojiVariants =
                variants.ToDictionary(
                    pair => pair.Key,
                    pair =>
                        (IReadOnlyList<string>)pair.Value,
                    StringComparer.Ordinal);
        }

        private static string BuildEmoji(
            IEnumerable<int> codePoints)
        {
            var builder =
                new System.Text.StringBuilder();

            foreach (int codePoint in codePoints)
            {
                builder.Append(
                    char.ConvertFromUtf32(codePoint));
            }

            return builder.ToString();
        }

        private static bool IsEmojiSkinTone(
            int codePoint)
        {
            return codePoint is >= 0x1F3FB and <= 0x1F3FF;
        }

        private void ShowEmojiVariants(
            string baseEmoji)
        {
            if (!_emojiVariants.TryGetValue(
                    baseEmoji,
                    out IReadOnlyList<string>? variants) ||
                variants.Count == 0)
            {
                return;
            }

            var choices =
                new List<string>
                {
                    baseEmoji,
                };
            choices.AddRange(variants);

            _visibleEmojiCandidates =
                choices;
            _emojiGrid.Adapter =
                new EmojiGridAdapter(
                    this,
                    _visibleEmojiCandidates);
            _emojiGrid.SetSelection(0);
        }

        private sealed class EmojiGridAdapter :
            BaseAdapter
        {
            private readonly Context _context;
            private readonly IReadOnlyList<string> _items;

            public EmojiGridAdapter(
                Context context,
                IReadOnlyList<string> items)
            {
                _context = context;
                _items = items;
            }

            public override int Count =>
                _items.Count;

            public override Java.Lang.Object GetItem(
                int position)
            {
                return new Java.Lang.String(
                    _items[position]);
            }

            public override long GetItemId(
                int position)
            {
                return position;
            }

            public override View GetView(
                int position,
                View? convertView,
                ViewGroup? parent)
            {
                var cell =
                    convertView as TextView ??
                    new TextView(_context);
                int height =
                    (int)(48 *
                        _context.Resources!.DisplayMetrics!.Density +
                        0.5f);

                cell.Text =
                    _items[position];
                cell.TextSize = 24;
                cell.Gravity =
                    GravityFlags.Center;
                cell.SetBackgroundColor(
                    Color.White);
                cell.LayoutParameters =
                    new AbsListView.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent,
                        height);

                return cell;
            }
        }

        private void InputEmoji(
            string emoji)
        {
            var ic =
                CurrentInputConnection;

            if (ic == null)
            {
                return;
            }

            FinishNativeComposition(ic);

            ic.CommitText(
                new Java.Lang.String(emoji),
                1);

            RememberEmoji(emoji);

            _composition.Reset();
            _romajiDraftStart = -1; _romajiDraftLength = 0;
            _compositionVersion++;
            _autoConversionVersion++;
            _romajiBuffer = "";
            _pendingSecondN = false;
            _candidates =
                Array.Empty<string>();
            _candidateSourceLength = 0;

            ShowEmojiCandidates();
        }

        private void SetCandidatePanelHeight(
            int heightDp)
        {
            if (_candidatePanel?.LayoutParameters == null)
            {
                return;
            }

            _candidatePanel.LayoutParameters.Height =
                Dp(heightDp);
            _candidatePanel.RequestLayout();

            if (_candidateScroll?.Visibility ==
                ViewStates.Visible)
            {
                _candidateScroll.ScrollTo(0, 0);
            }
        }

        private void SelectCandidate(int index)
        {
            var ic = CurrentInputConnection;
            if (ic is null || index < 0 || index >= _candidates.Count || _candidateSource is null ||
                _candidateTarget is null || !_composition.IsCurrent(_candidateTarget) ||
                _candidateCompositionVersion != _compositionVersion || !IsCurrentConversionSource(_candidateSource))
            {
                ClearCandidates();
                return;
            }
            string candidate = _candidates[index];
            var target = _candidateTarget;
            _suppressSelectionUpdates = true;
            try
            {
                var result = CompositionCommitter.Commit(_composition, target, candidate, new CompositionEditor(ic));
                if (!result.Committed) return;
                _lastNativeSpan = _composition.NativeComposingSpan;
                _nativeRestoreSelectionRevision = _composition.SelectionRevision;
                if (!result.ComposingRestored)
                    Android.Util.Log.Warn("NyaIme", "Candidate committed; editor rejected remaining composing span.");
                SyncSelectionPosition();
                _compositionVersion++;
                _romajiBuffer = ""; _pendingSecondN = false;
                _romajiDraftStart = -1; _romajiDraftLength = 0;
                _autoConversionVersion++;
                ClearCandidates();
            }
            finally { _suppressSelectionUpdates = false; }
            if (_composition.GetTarget() is not null) ScheduleAutoConversion();
        }

        private void ClearCandidates()
        {
            _conversionPhase = "入力後に候補を表示";
            UpdateConversionModeStatus();
            _conversionCancellation?.Cancel();
            _conversionRequest++;
            _candidateSource = null;
            _candidateTarget = null;
            _predictions.Clear();
            _candidates =
                Array.Empty<string>();
            _candidateSourceLength = 0;

            if (_candidateBar != null)
            {
                if (_emojiMode)
                {
                    ShowEmojiCandidates();
                    return;
                }

                ShowConversionStatus(
                    _composition.Active
                        ? "入力中…"
                        : (_offlineMode ? "オフライン: 入力後に候補を表示" : "AI変換: 入力後に候補を表示"));
            }
        }

        // =========================================================
        // 機能キー
        // =========================================================

        private void UpdateConversionModeStatus()
        {
            if (_conversionModeStatus is not null)
            {
                string target = _candidateTarget is null ? "" : " ・ 対象: " +
                    (_candidateTarget.Source.Length > 12 ? _candidateTarget.Source[..12] + "…" : _candidateTarget.Source);
                _conversionModeStatus.Text = (_offlineMode ? "オフライン" : "AI変換") + " ・ " + _conversionPhase + target;
            }
        }

        private Button CreateFunctionKey(
            string text,
            Action action)
        {
            var button =
                new Button(this);

            button.Text =
                text;

            button.TextSize =
                21;

            button.SetTextColor(
                Color.Rgb(
                    70,
                    90,
                    100));

            button.SetBackgroundColor(
                Color.Rgb(
                    235,
                    239,
                    242));

            button.SetPadding(
                0,
                0,
                0,
                0);

            button.Click += (_, _) =>
            {
                action();
            };

            return button;
        }

        private Button CreateRepeatingFunctionKey(
            string text,
            Action action)
        {
            Button button =
                CreateFunctionKey(
                    text,
                    () => { });
            CancellationTokenSource? repeatCancellation =
                null;
            bool longPressStarted = false;

            button.Touch += (_, e) =>
            {
                if (e.Event == null)
                {
                    e.Handled = true;
                    return;
                }

                switch (e.Event.ActionMasked)
                {
                    case MotionEventActions.Down:
                        repeatCancellation?.Cancel();
                        repeatCancellation?.Dispose();
                        repeatCancellation =
                            new CancellationTokenSource();
                        longPressStarted = false;
                        button.Pressed = true;
                        _ = RepeatFunctionKeyAsync(
                            action,
                            () => longPressStarted = true,
                            repeatCancellation.Token);
                        break;

                    case MotionEventActions.Up:
                        repeatCancellation?.Cancel();
                        repeatCancellation?.Dispose();
                        repeatCancellation = null;
                        button.Pressed = false;

                        if (!longPressStarted)
                        {
                            action();
                        }

                        break;

                    case MotionEventActions.Cancel:
                        repeatCancellation?.Cancel();
                        repeatCancellation?.Dispose();
                        repeatCancellation = null;
                        button.Pressed = false;
                        break;
                }

                e.Handled = true;
            };

            return button;
        }

        private static async System.Threading.Tasks.Task
            RepeatFunctionKeyAsync(
                Action action,
                Action onRepeatStarted,
                CancellationToken cancellationToken)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(
                    400,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            onRepeatStarted();

            while (!cancellationToken.IsCancellationRequested)
            {
                action();

                try
                {
                    await System.Threading.Tasks.Task.Delay(
                        70,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        // =========================================================
        // Layout
        // =========================================================

        private LinearLayout.LayoutParams
            HorizontalWeighted()
        {
            return new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.MatchParent,
                1f);
        }

        private LinearLayout.LayoutParams
            VerticalWeighted()
        {
            return new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                0,
                1f);
        }

        // =========================================================
        // DP
        // =========================================================

        private int GetNavigationBarInsetFallback()
        {
            int resourceId =
                Resources.GetIdentifier(
                    "navigation_bar_height",
                    "dimen",
                    "android");

            if (resourceId > 0)
            {
                return Math.Max(
                    Dp(MinimumBottomSpacerDp),
                    Resources.GetDimensionPixelSize(
                        resourceId));
            }

            return Dp(
                MinimumBottomSpacerDp);
        }

        private int Dp(
            int value)
        {
            return (int)(
                value *
                Resources.DisplayMetrics.Density +
                0.5f);
        }
    }
}
