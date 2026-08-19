namespace CoopFeasibilityMod;

public sealed class Pad2SourceAvailability
{
    public bool ProcessedInputLatched { get; private set; }

    public void ObserveProcessed(bool virtualKeyboard, ushort buttons)
    {
        if (!virtualKeyboard && buttons != ushort.MaxValue)
            ProcessedInputLatched = true;
    }

    public bool IsAvailable(bool virtualKeyboard, bool physicalConnected) =>
        virtualKeyboard || physicalConnected || ProcessedInputLatched;

    public void Reset() => ProcessedInputLatched = false;
}
