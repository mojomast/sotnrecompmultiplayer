using CoopFeasibilityMod;

var tests = new List<(string, Action)>
{
    ("exact prize equipment and morph", () =>
    {
        var t = new NativeDropObservationTracker();
        Window(t, after: [(160, Prize(10, 20)), (161, Equipment(30, 40))]);
        Equal(1UL, t.Diagnostics.PrizeSpawns); Equal(1UL, t.Diagnostics.EquipmentSpawns);
        Window(t, before: [(161, Equipment(30, 40))], after: [(161, Prize(30, 40))]);
        Equal(1UL, t.Diagnostics.Morphs); Equal(1UL, t.Diagnostics.PrizeSpawns);
    }),
    ("one exact causal association and native exp observation", () =>
    {
        var t = new NativeDropObservationTracker();
        t.BeginWindow(7, 100); t.RecordUniqueCausalDefeat(7, 50, 60);
        t.SetAfter(160, Prize(55, 64)); t.CompleteWindow(112);
        Equal(1UL, t.Diagnostics.P2AssociatedSpawns); Equal(1UL, t.Diagnostics.ObservedNativeExpEvents);
        Equal(12UL, t.Diagnostics.ObservedNativeExpDelta);
    }),
    ("causal defeat without drop expires bounded", () =>
    {
        var t = new NativeDropObservationTracker();
        t.BeginWindow(1, null); t.RecordUniqueCausalDefeat(1, 0, 0); t.CompleteWindow(null);
        for (int i = 0; i < 3; i++) Window(t);
        Equal(1UL, t.Diagnostics.CausalDefeatsWithoutDrop);
    }),
    ("ambient simultaneous multiple and causal ambiguity never associate", () =>
    {
        var ambient = new NativeDropObservationTracker();
        Window(ambient, after: [(160, Prize(0, 0)), (161, Equipment(1, 1))]);
        Equal(2UL, ambient.Diagnostics.AmbientSpawns);
        var ambiguous = new NativeDropObservationTracker();
        ambiguous.BeginWindow(1, null); ambiguous.RecordUniqueCausalDefeat(1, 0, 0);
        ambiguous.SetAfter(160, Prize(0, 0)); ambiguous.SetAfter(161, Prize(1, 1));
        ambiguous.CompleteWindow(null);
        Equal(1UL, ambiguous.Diagnostics.AmbiguousSpawns); Equal(0UL, ambiguous.Diagnostics.P2AssociatedSpawns);
        var overflow = new NativeDropObservationTracker();
        overflow.BeginWindow(4, null); overflow.RecordAmbiguousOverflowWindow(4); overflow.CompleteWindow(null);
        Equal(1UL, overflow.Diagnostics.AmbiguousSpawns); Equal(1UL, overflow.Diagnostics.OverflowEvents);
        True(!overflow.Diagnostics.Faulted);
    }),
    ("wrong room position and update are not associated", () =>
    {
        var t = new NativeDropObservationTracker();
        t.BeginWindow(2, null); t.RecordUniqueCausalDefeat(2, 0, 0);
        t.SetAfter(160, Prize(100, 100));
        t.SetAfter(161, new(3, 0xDEADBEEF, 0, 0, 0, 0, 0)); t.CompleteWindow(null);
        Equal(1UL, t.Diagnostics.AmbientSpawns); Equal(0UL, t.Diagnostics.P2AssociatedSpawns);
        t.BeginWindow(3, null); t.CompleteWindow(null);
        Equal(1UL, t.Diagnostics.CausalDefeatsWithoutDrop);
    }),
    ("collection expiration lifecycle reuse and unresolved are distinct", () =>
    {
        var t = new NativeDropObservationTracker();
        Window(t, expBefore: 5, expAfter: 8, before: [(160, Prize(0, 0) with { Step = 5 })]);
        Window(t, before: [(160, Prize(0, 0) with { Step = 6 })]);
        Window(t, before: [(160, Prize(0, 0) with { Step = 2 })]);
        Window(t, before: [(160, Prize(0, 0))], after: [(160, new(20, 0x1234, 0, 0, 0, 0, 0))]);
        Window(t, before: [(160, Prize(0, 0) with { HitFlags = 1 })]);
        Equal(1UL, t.Diagnostics.Collections); Equal(1UL, t.Diagnostics.Expirations);
        Equal(1UL, t.Diagnostics.LifecycleDisappears); Equal(1UL, t.Diagnostics.Reuses);
        Equal(1UL, t.Diagnostics.UnresolvedPickups);
    }),
    ("bounds fault pending overflow and counter exhaustion fail closed", () =>
    {
        var t = new NativeDropObservationTracker(); t.BeginWindow(1, null); t.SetAfter(159, Prize(0, 0));
        for (int i = 0; i < 5; i++) t.RecordUniqueCausalDefeat(1, i, i);
        True(t.Diagnostics.Faulted); True(t.Diagnostics.OverflowEvents >= 2);
        Set(t, "_scans", ulong.MaxValue); t.CompleteWindow(null); True(t.Diagnostics.Faulted);
    }),
    ("deterministic generated sequence", () =>
    {
        for (uint seed = 1; seed <= 32; seed++) Equal(Replay(seed), Replay(seed));
    }),
    ("warmed windows allocate zero", () =>
    {
        var t = new NativeDropObservationTracker(); for (int i = 0; i < 100; i++) Window(t);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) Window(t);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests) try { run(); Console.WriteLine($"PASS {name}"); }
catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
Console.WriteLine($"CoopNativeDrop: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static NativeDropSlotObservation Prize(short x, short y) => new(3, 0x801C9220, 1, 1, 0, x, y);
static NativeDropSlotObservation Equipment(short x, short y) => new(10, 0x801C9C34, 2, 1, 0, x, y);
static void Window(NativeDropObservationTracker tracker, ulong room = 1, long? expBefore = null,
    long? expAfter = null, (int Slot, NativeDropSlotObservation Value)[]? before = null,
    (int Slot, NativeDropSlotObservation Value)[]? after = null)
{
    tracker.BeginWindow(room, expBefore);
    if (before != null) foreach (var value in before) tracker.SetBefore(value.Slot, value.Value);
    if (after != null) foreach (var value in after) tracker.SetAfter(value.Slot, value.Value);
    tracker.CompleteWindow(expAfter);
}
static ulong Replay(uint seed)
{
    var t = new NativeDropObservationTracker();
    for (int i = 0; i < 100; i++)
    {
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        t.BeginWindow(1, null);
        if ((seed & 7) == 0) t.RecordUniqueCausalDefeat(1, (int)(seed % 200), (int)((seed >> 8) % 200));
        if ((seed & 3) == 0) t.SetAfter(160 + (int)(seed % 32), Prize((short)(seed % 200), (short)((seed >> 8) % 200)));
        t.CompleteWindow(null);
    }
    NativeDropDiagnostics d = t.Diagnostics;
    return d.Scans ^ (d.PrizeSpawns << 8) ^ (d.P2AssociatedSpawns << 16) ^ (d.AmbiguousSpawns << 24);
}
static void Set(object value, string field, object replacement) => value.GetType().GetField(field,
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(value, replacement);
static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual))
    throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
