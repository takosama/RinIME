using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Android.Views.InputMethods;

namespace NyaIme
{
    [Activity(
        Label = "RinIME オフライン",
        MainLauncher = true,
        Exported = true)]
    public class MainActivity : Activity
    {
        private TextView? _imeStatus;

        protected override void OnResume()
        {
            base.OnResume();
            RefreshImeStatus();
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
            if (hasFocus) RefreshImeStatus();
        }

        private void RefreshImeStatus()
        {
            if (_imeStatus is null) return;
            string selected = Android.Provider.Settings.Secure.GetString(ContentResolver,
                Android.Provider.Settings.Secure.DefaultInputMethod) ?? "";
            _imeStatus.Text = selected.StartsWith(PackageName + "/", StringComparison.Ordinal)
                ? "RinIME オフライン 1.6 が選択されています。"
                : selected.StartsWith("com.companyname.NyaIme/", StringComparison.Ordinal)
                    ? "旧版のRinIMEが選択されています。「RinIME オフライン」を選んでください。"
                    : "別のキーボードが選択されています。「RinIME オフライン」を選んでください。";
        }

        protected override void OnCreate(
            Bundle savedInstanceState)
        {
            base.OnCreate(
                savedInstanceState);

            var root =
                new LinearLayout(this);

            root.Orientation =
                Orientation.Vertical;

            root.AddView(new TextView(this) { Text = "RinIME オフライン 1.6", TextSize = 20 });
            _imeStatus = new TextView(this);
            root.AddView(_imeStatus);

            var chooseIme = new Button(this) { Text = "入力方法を選ぶ" };
            chooseIme.Click += (_, _) =>
            {
                var manager = (InputMethodManager?)GetSystemService(InputMethodService);
                manager?.ShowInputMethodPicker();
            };
            root.AddView(chooseIme);

            var test =
                new EditText(this);

            test.Hint =
                "RinIME入力テスト";

            test.SetMinHeight(
                160);

            var button =
                new Button(this);

            button.Text =
                "IME設定を開く";

            button.Click += (_, _) =>
            {
                StartActivity(
                    new Intent(
                        "android.settings.INPUT_METHOD_SETTINGS"));
            };

            root.AddView(
                test,
                new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MatchParent,
                    LinearLayout.LayoutParams.WrapContent));

            root.AddView(
                button,
                new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MatchParent,
                    LinearLayout.LayoutParams.WrapContent));

            var guide = new TextView(this)
            {
                Text = "まずIME設定で「RinIME オフライン」を有効にし、「入力方法を選ぶ」で選択してください。\n" +
                       "キーボード左上の「オフ／ライン」（2行表示）と「AI」で切替。オフラインでは入力を送信しません。\n" +
                       "候補をタップして確定。「予測:」は語尾まで補完します。元のかなも候補に残ります。\n" +
                       "カーソル左側を変換し、右側は残します。範囲選択時は選択部分だけを変換します。\n" +
                       "英字画面のShiftは1回で次の1文字を大文字、2回で大文字固定、もう1回で解除。\n" +
                       "O（お）キーの下フリックは半角@です。",
            };
            root.AddView(guide);
            var licenses = new Button(this) { Text = "オフライン辞書のライセンス" };
            licenses.Click += (_, _) =>
            {
                using var stream = Assets!.Open("MOZC-LICENSE.txt");
                using var reader = new StreamReader(stream);
                using var noticesStream = Assets.Open("MOZC-DICTIONARY-NOTICES.txt");
                using var noticesReader = new StreamReader(noticesStream);
                new AlertDialog.Builder(this)!.SetTitle("Mozc OSS dictionary")!
                    .SetMessage(reader.ReadToEnd() + "\n\n" + noticesReader.ReadToEnd())!
                    .SetPositiveButton("閉じる", (_, _) => { })!.Show();
            };
            root.AddView(licenses);

            SetContentView(
                root);
        }
    }
}
