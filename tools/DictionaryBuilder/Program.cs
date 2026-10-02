using System.IO.Compression;
using System.Text;

if (args.Length != 2) throw new ArgumentException("DictionaryBuilder <source-directory> <output.gz>");
var words = new SortedDictionary<string, List<Word>>(StringComparer.Ordinal);
foreach (string path in Directory.GetFiles(args[0], "dictionary??.txt").Order())
foreach (string line in File.ReadLines(path, Encoding.UTF8))
{
    var fields = line.Split('\t');
    if (fields.Length < 5 || fields[0].Length == 0) continue;
    var word = new Word(ushort.Parse(fields[1]), ushort.Parse(fields[2]), short.Parse(fields[3]), fields[4]);
    if (!words.TryGetValue(fields[0], out var list)) words[fields[0]] = list = new();
    if (!list.Contains(word)) list.Add(word);
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
using var file = File.Create(args[1]);
using var zip = new GZipStream(file, CompressionLevel.SmallestSize);
using var writer = new BinaryWriter(zip, Encoding.UTF8);
writer.Write(0x314B4E52); // RNK1, little endian
writer.Write(words.Count);
long count = 0;
foreach (var (key, list) in words)
{
    writer.Write(key);
    using var buffer = new MemoryStream();
    using var entryWriter = new BinaryWriter(buffer, Encoding.UTF8, true);
    foreach (var word in list.OrderBy(x => x.Cost).ThenBy(x => x.Text, StringComparer.Ordinal))
    {
        entryWriter.Write(word.Left); entryWriter.Write(word.Right);
        entryWriter.Write(word.Cost); entryWriter.Write(word.Text); count++;
    }
    entryWriter.Flush();
    writer.Write((ushort)list.Count); writer.Write((int)buffer.Length); writer.Write(buffer.ToArray());
}
using var matrix = new StreamReader(Path.Combine(args[0], "connection_single_column.txt"));
int size = int.Parse(matrix.ReadLine()!);
writer.Write(size);
for (int index = 0; index < size * size; index++)
    writer.Write(index == 0 ? ReadZero(matrix) : short.Parse(matrix.ReadLine()!));
if (matrix.ReadLine() is not null) throw new InvalidDataException("Unexpected matrix size");
Console.WriteLine($"Readings={words.Count}; entries={count}; matrix={size}x{size}");
static short ReadZero(StreamReader reader) { reader.ReadLine(); return 0; }
record Word(ushort Left, ushort Right, short Cost, string Text);
