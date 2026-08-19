using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("virtual mode is available without a controller", () =>
    {
        var source = new Pad2SourceAvailability();
        True(source.IsAvailable(virtualKeyboard: true, physicalConnected: false));
        False(source.ProcessedInputLatched);
    }),
    ("physical configured Pad 2 is available", () =>
    {
        var source = new Pad2SourceAvailability();
        True(source.IsAvailable(virtualKeyboard: false, physicalConnected: true));
        False(source.ProcessedInputLatched);
    }),
    ("first configured processed input latches availability", () =>
    {
        var source = new Pad2SourceAvailability();
        False(source.IsAvailable(virtualKeyboard: false, physicalConnected: false));
        source.ObserveProcessed(virtualKeyboard: false, buttons: 0xDFFF);
        True(source.ProcessedInputLatched);
        True(source.IsAvailable(virtualKeyboard: false, physicalConnected: false));
    }),
    ("neutral processed frames continue after injection", () =>
    {
        var source = new Pad2SourceAvailability();
        source.ObserveProcessed(virtualKeyboard: false, buttons: 0x7FFF);
        source.ObserveProcessed(virtualKeyboard: false, buttons: ushort.MaxValue);
        True(source.IsAvailable(virtualKeyboard: false, physicalConnected: false));
    }),
    ("neutral configured input alone does not latch", () =>
    {
        var source = new Pad2SourceAvailability();
        source.ObserveProcessed(virtualKeyboard: false, buttons: ushort.MaxValue);
        False(source.ProcessedInputLatched);
        False(source.IsAvailable(virtualKeyboard: false, physicalConnected: false));
    }),
    ("virtual observations never impersonate configured Pad 2", () =>
    {
        var source = new Pad2SourceAvailability();
        source.ObserveProcessed(virtualKeyboard: true, buttons: 0xDFFF);
        False(source.ProcessedInputLatched);
        False(source.IsAvailable(virtualKeyboard: false, physicalConnected: false));
    }),
    ("reset and mode switch clear configured injection", () =>
    {
        var source = new Pad2SourceAvailability();
        source.ObserveProcessed(virtualKeyboard: false, buttons: 0xDFFF);
        source.Reset();
        False(source.ProcessedInputLatched);
        True(source.IsAvailable(virtualKeyboard: true, physicalConnected: false));
        False(source.IsAvailable(virtualKeyboard: false, physicalConnected: false));
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopPad2Source: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("Expected false.");
}
