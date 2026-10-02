using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NyaIme
{
    internal sealed class KanaKanjiClient : IDisposable
    {
        private readonly string _apiKey;

        private readonly HttpClient _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        public KanaKanjiClient(Func<string>? keyProvider = null)
        {
            _apiKey = keyProvider?.Invoke() ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
            if (!string.IsNullOrWhiteSpace(_apiKey))
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<IReadOnlyList<string>> GetCandidatesStreamingAsync(
            string source,
            Func<IReadOnlyList<string>, bool> onCandidatesChanged,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_apiKey)) return Array.Empty<string>();

            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["candidates"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "string",
                        },
                        ["minItems"] = 8,
                        ["maxItems"] = 8,
                    },
                },
                ["required"] = new JsonArray(
                    JsonValue.Create("candidates")),
                ["additionalProperties"] = false,
            };

            var request = new JsonObject
            {
                ["model"] = "gpt-6-luna",
                ["reasoning_effort"] = "low",
                ["stream"] = true,
                // 50文字程度の長文を8候補すべて返しても切れない余裕を持たせる。
                ["max_completion_tokens"] = 2000,
                ["messages"] = new JsonArray
                {
                    (JsonNode)new JsonObject
                    {
                        ["role"] = "developer",
                        ["content"] = """
                            あなたは日本語入力欄で動作する文脈認識型IMEです。
                            入力に返答するのではなく、入力全体を置き換えられる変換候補を作ってください。

                            【変換方針】
                            - 入力は、単語、文節、入力途中の断片、長文のいずれもあります。
                            - 質問、依頼、命令の形でも、回答・会話・実行・翻訳・要約・解説をしてはいけません。
                            - 入力途中の文を勝手に完成させず、語調、時制、文体、句読点、空白、改行を保ってください。
                            - すでに自然な漢字、カタカナ、英数字、URL、メールアドレス、コード、絵文字、記号は原則そのまま残してください。
                            - 珍しい内容、固有名詞、専門用語を誤りと決めつけてはいけません。
                            - 長文では文節や節を落とさず、元の意味と情報量を維持してください。
                            - 誤打鍵、近い音、助詞、活用の修正は、文脈上自然な場合だけ下位候補で控えめに行ってください。

                            【外国語名の音の揺れ】
                            - 入力が通常の日本語として不自然で、人名・地名・作品名などの外国語固有名詞らしい場合は、単純なかな漢字変換に限定しない。
                            - 候補6～8では、複数モーラにまたがる音の脱落・追加・入れ替わりを許容する。
                            - 長音の有無、小書きの母音・ャュョ・ッ、濁音・半濁音、ラ行・ダ行など近い音、語の区切りの揺れを許容する。
                            - 元の音を手掛かりに、実在しそうなカタカナ固有名詞を積極的に復元し、少なくとも1件はその候補を入れる。
                            - 通常の日本語として自然に読める文章には、この強い音補正を適用しない。

                            【8候補の順序】
                            1: 最も安全な候補。読み、語順、意味を変えず、必要な箇所だけを漢字・かな・英数字に変換する。
                            2～3: 読みと意味を維持し、同音語、文節区切り、送り仮名、表記だけを変える。
                            4～5: 文脈に基づき、誤打鍵、近い音、助詞、活用を最小限だけ修正する。
                            6～8: より大胆だが文脈上妥当な音の補正や別解釈を示す。外国語固有名詞らしい入力ではカタカナ名を優先する。

                            【<m>の扱い】
                            - 入力に<m>がある場合は、その位置に文脈から推定した名称または語句を補い、全候補から<m>を除く。
                            - 1～3は確実性の高い補完、4～8は異なる可能性の補完を優先する。
                            - <m>がない場合は通常の候補順序に従う。

                            【出力規則】
                            - candidatesに、重複しない文字列を必ず8件入れる。
                            - 各候補は入力の一部分ではなく、入力全体を置き換えられる完全な文字列にする。
                            - 候補番号、引用符、注釈、理由、前置き、返答文を候補に含めない。
                            - 修正量が小さく、読みと意味に忠実で、自然な候補ほど上位にする。
                            """,
                    },
                    (JsonNode)new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = source,
                    },
                },
                ["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = "kana_kanji_candidates",
                        ["strict"] = true,
                        ["schema"] = schema,
                    },
                },
            };

            var values = new List<string>();

            try
            {
                using var requestMessage = new HttpRequestMessage(
                    HttpMethod.Post,
                    "v1/chat/completions");
                requestMessage.Content = new StringContent(
                    request.ToJsonString(),
                    Encoding.UTF8,
                    "application/json");

                using var response = await _httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<string>();
                }

                using Stream stream =
                    await response.Content.ReadAsStreamAsync(
                        cancellationToken);
                using var reader = new StreamReader(stream);
                var parser = new StreamingCandidateParser();
                var responseText = new StringBuilder();

                while (await reader.ReadLineAsync(
                           cancellationToken) is { } line)
                {
                    if (!line.StartsWith(
                            "data:",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string eventData = line[5..].TrimStart();

                    if (eventData == "[DONE]")
                    {
                        break;
                    }

                    string? fragment =
                        ReadContentFragment(eventData);

                    if (string.IsNullOrEmpty(fragment))
                    {
                        continue;
                    }

                    responseText.Append(fragment);

                    foreach (string candidate in
                        parser.Append(fragment))
                    {
                        if (values.Count >= 8 ||
                            values.Contains(
                                candidate,
                                StringComparer.Ordinal))
                        {
                            continue;
                        }

                        values.Add(candidate);

                        if (!onCandidatesChanged(
                                values.ToArray()))
                        {
                            return values;
                        }
                    }
                }

                // チャンク境界やAPI側の出力形態によって逐次解析が
                // 取りこぼしても、受信完了済みのJSONから候補を復元する。
                foreach (string candidate in
                    ParseCompletedCandidates(
                        responseText.ToString()))
                {
                    if (values.Count >= 8 ||
                        values.Contains(
                            candidate,
                            StringComparer.Ordinal))
                    {
                        continue;
                    }

                    values.Add(candidate);
                    onCandidatesChanged(values.ToArray());
                }

                return values;
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or
                TaskCanceledException or
                IOException or
                JsonException)
            {
                return values;
            }
        }

        private static IReadOnlyList<string>
            ParseCompletedCandidates(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return Array.Empty<string>();
            }

            using JsonDocument json =
                JsonDocument.Parse(responseText);

            if (!json.RootElement.TryGetProperty(
                    "candidates",
                    out JsonElement candidates) ||
                candidates.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();

            foreach (JsonElement item in
                candidates.EnumerateArray())
            {
                string? value =
                    item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : null;

                if (!string.IsNullOrWhiteSpace(value) &&
                    !values.Contains(
                        value,
                        StringComparer.Ordinal))
                {
                    values.Add(value);
                }
            }

            return values.Take(8).ToArray();
        }

        private static string? ReadContentFragment(
            string eventData)
        {
            using var json = JsonDocument.Parse(eventData);

            if (json.RootElement.TryGetProperty(
                    "choices",
                    out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty(
                    "delta",
                    out var delta) &&
                delta.TryGetProperty(
                    "content",
                    out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            return null;
        }

        private sealed class StreamingCandidateParser
        {
            private readonly StringBuilder _buffer = new();
            private int _scanIndex;
            private int _stringStart;
            private bool _arrayStarted;
            private bool _inString;
            private bool _escaping;
            private bool _completed;

            public IReadOnlyList<string> Append(string fragment)
            {
                if (_completed || fragment.Length == 0)
                {
                    return Array.Empty<string>();
                }

                _buffer.Append(fragment);
                var completedValues = new List<string>();

                if (!_arrayStarted)
                {
                    int propertyIndex = _buffer.ToString().IndexOf(
                        "\"candidates\"",
                        StringComparison.Ordinal);

                    if (propertyIndex < 0)
                    {
                        return completedValues;
                    }

                    int arrayIndex = _buffer.ToString().IndexOf(
                        '[',
                        propertyIndex + "\"candidates\"".Length);

                    if (arrayIndex < 0)
                    {
                        return completedValues;
                    }

                    _arrayStarted = true;
                    _scanIndex = arrayIndex + 1;
                }

                for (;
                     _scanIndex < _buffer.Length;
                     _scanIndex++)
                {
                    char current = _buffer[_scanIndex];

                    if (!_inString)
                    {
                        if (current == ']')
                        {
                            _completed = true;
                            break;
                        }

                        if (current == '"')
                        {
                            _inString = true;
                            _escaping = false;
                            _stringStart = _scanIndex;
                        }

                        continue;
                    }

                    if (_escaping)
                    {
                        _escaping = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        _escaping = true;
                        continue;
                    }

                    if (current != '"')
                    {
                        continue;
                    }

                    string quotedValue = _buffer.ToString(
                        _stringStart,
                        _scanIndex - _stringStart + 1);
                    using JsonDocument quotedJson =
                        JsonDocument.Parse(quotedValue);
                    string? value =
                        quotedJson.RootElement.GetString();

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        completedValues.Add(value);
                    }

                    _inString = false;
                }

                return completedValues;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
