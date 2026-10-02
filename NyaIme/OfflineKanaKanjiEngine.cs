using System.IO.Compression;
using System.Text;

namespace NyaIme;

// Managed CPU-only lattice search over the bundled Mozc OSS lexicon.
// This is not the Mozc converter: no Mozc rewriting, learning or neural model.
internal sealed class OfflineKanaKanjiEngine
{
    private readonly string[] _keys;
    private readonly int[] _offsets;
    private readonly ushort[] _counts;
    private readonly byte[] _words;
    private readonly short[] _connections;
    private readonly int _matrixSize;
    private const int BeamWidth = 48;
    public const int MaximumInputLength = 256;

    public OfflineKanaKanjiEngine(Stream compressedDictionary)
    {
        using var gzip = new GZipStream(compressedDictionary, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip, Encoding.UTF8);
        if (reader.ReadInt32() != 0x314B4E52) throw new InvalidDataException("Dictionary version");
        int count = reader.ReadInt32();
        _keys = new string[count]; _offsets = new int[count]; _counts = new ushort[count];
        using var words = new MemoryStream();
        for (int index = 0; index < count; index++)
        {
            _keys[index] = reader.ReadString(); _offsets[index] = checked((int)words.Position);
            _counts[index] = reader.ReadUInt16();
            int length = reader.ReadInt32();
            var data = reader.ReadBytes(length);
            if (data.Length != length) throw new EndOfStreamException();
            words.Write(data);
        }
        _words = words.ToArray();
        _matrixSize = reader.ReadInt32();
        _connections = new short[checked(_matrixSize * _matrixSize)];
        for (int i = 0; i < _connections.Length; i++) _connections[i] = reader.ReadInt16();
    }

    public IReadOnlyList<string> GetCandidates(string source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(source)) return Array.Empty<string>();
        if (source.Length > MaximumInputLength) return new[] { source };
        string reading = ToHiragana(source);
        var lattice = new List<Path>[source.Length + 1];
        lattice[0] = new() { new Path("", 0, 0) };
        for (int start = 0; start < source.Length; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lattice[start] is null) continue;
            var incoming = Prune(lattice[start]);
            // Preserve non-kana runs verbatim, including surrogate pairs, URLs and punctuation.
            if (!IsKana(reading[start]))
            {
                int end = start + 1;
                while (end < source.Length && !IsKana(reading[end])) end++;
                foreach (var previous in incoming)
                    Add(end, new(previous.Text + source[start..end], 0, previous.Cost + Transition(previous.Right, 0)));
                continue;
            }
            int limit = start;
            while (limit < source.Length && IsKana(reading[limit])) limit++;
            for (int end = start + 1; end <= limit; end++)
            {
                int index = Array.BinarySearch(_keys, reading[start..end], StringComparer.Ordinal);
                if (index < 0) continue;
                foreach (var word in ReadWords(index))
                foreach (var previous in incoming)
                    Add(end, new(previous.Text + word.Text, word.Right,
                        previous.Cost + word.Cost + Transition(previous.Right, word.Left)));
            }
            // Unknown readings always have a lossless path. High cost favors dictionary words.
            foreach (var previous in incoming)
                Add(start + 1, new(previous.Text + source[start], previous.Right, previous.Cost + 12000));
        }
        var candidates = (lattice[^1] ?? new()).OrderBy(x => x.Cost + Transition(x.Right, 0))
            .Select(x => x.Text).Distinct(StringComparer.Ordinal).Where(x => x != source).Take(6).ToList();
        // Reserve slots for katakana and the exact original, even for ambiguous readings.
        string katakana = ToKatakana(source);
        if (!candidates.Contains(katakana, StringComparer.Ordinal) && katakana != source) candidates.Add(katakana);
        candidates.Add(source);
        return candidates;

        void Add(int end, Path path)
        {
            var list = lattice[end] ??= new();
            list.Add(path);
            if (list.Count > BeamWidth * 8) lattice[end] = Prune(list);
        }
    }

    // Prefix lookup is restricted to a single kana word. Returned strings replace the whole reading.
    public IReadOnlyList<string> Predict(string source, CancellationToken cancellationToken = default)
    {
        if (source.Length < 2 || source.Length > 32) return Array.Empty<string>();
        string prefix = ToHiragana(source);
        if (!prefix.All(IsKana)) return Array.Empty<string>();
        int index = Array.BinarySearch(_keys, prefix, StringComparer.Ordinal);
        if (index < 0) index = ~index;
        var values = new List<(string Text, int Cost)>();
        int examined = 0;
        while (index < _keys.Length && _keys[index].StartsWith(prefix, StringComparison.Ordinal) && examined++ < 512)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_keys[index].Length > prefix.Length)
                foreach (var word in ReadWords(index))
                    values.Add((word.Text, word.Cost + Transition(0, word.Left) + Transition(word.Right, 0)
                        + (_keys[index].Length - prefix.Length) * 600));
            index++;
        }
        return values.OrderBy(x => x.Cost).Select(x => x.Text).Distinct(StringComparer.Ordinal).Take(4).ToArray();
    }

    private IEnumerable<Word> ReadWords(int index)
    {
        using var stream = new MemoryStream(_words, false);
        stream.Position = _offsets[index];
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        for (int i = 0; i < _counts[index]; i++)
            yield return new(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadInt16(), reader.ReadString());
    }
    private int Transition(int right, int left) => _connections[right * _matrixSize + left];
    private static List<Path> Prune(List<Path> paths) => paths.OrderBy(x => x.Cost)
        .DistinctBy(x => (x.Text, x.Right)).Take(BeamWidth).ToList();
    private static bool IsKana(char value) => value is >= '\u3041' and <= '\u3096' or '\u30fc';
    private static string ToHiragana(string value) => new(value.Select(c => c is >= '\u30a1' and <= '\u30f6' ? (char)(c - 0x60) : c).ToArray());
    private static string ToKatakana(string value) => new(value.Select(c => c is >= '\u3041' and <= '\u3096' ? (char)(c + 0x60) : c).ToArray());
    private sealed record Path(string Text, ushort Right, long Cost);
    private sealed record Word(ushort Left, ushort Right, short Cost, string Text);
}
