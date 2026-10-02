namespace NyaIme;

internal sealed class ConversionModeState(bool offline)
{
    public bool Offline { get; private set; } = offline;
    public int Generation { get; private set; }
    public void Set(bool offline)
    {
        if (Offline == offline) return;
        Offline = offline; Generation++;
    }
    public bool IsCurrent(int generation, bool offline) => generation == Generation && offline == Offline;
}
