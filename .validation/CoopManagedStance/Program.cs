using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("entry requires grounded control", () =>
    {
        var reducer = new ManagedStanceReducer();
        reducer.Observe(true, false, true);
        True(!reducer.Crouched);
        reducer.Observe(false, true, true);
        True(!reducer.Crouched);
        reducer.Observe(true, true, true);
        True(reducer.Crouched && !reducer.StandBlocked);
    }),
    ("hold crouch requests no probe", () =>
    {
        var reducer = Crouched();
        ulong revision = reducer.State.Revision;
        for (int index = 0; index < 10; index++)
        {
            ManagedStanceTransition held = reducer.Observe(true, true, true);
            Equal(ManagedStanceCommandKind.None, held.Command.Kind);
        }
        Equal(revision, reducer.State.Revision);
    }),
    ("clear probe stands and clears blocked", () =>
    {
        var reducer = Blocked();
        ManagedStanceCommand command = reducer.Observe(true, true, false).Command;
        reducer.CompleteStandingProbe(command, true);
        True(!reducer.Crouched && !reducer.StandBlocked);
    }),
    ("blocked probe remains crouched and marks blocked", () =>
    {
        var reducer = Crouched();
        int probes = RunAdapterUpdate(reducer, clear: false, fault: false);
        Equal(1, probes);
        True(reducer.Crouched && reducer.StandBlocked);
        reducer.Observe(true, true, true);
        True(reducer.Crouched && reducer.StandBlocked);
    }),
    ("query fault commits nothing", () =>
    {
        var reducer = Crouched();
        ManagedStanceState before = reducer.State;
        int probes = RunAdapterUpdate(reducer, clear: true, fault: true);
        Equal(1, probes);
        EqualState(before, reducer.State);
    }),
    ("stale and duplicate commands fail closed", () =>
    {
        var reducer = Crouched();
        ManagedStanceCommand stale = reducer.Observe(true, true, false).Command;
        reducer.CompleteStandingProbe(stale, false);
        Reject(() => reducer.CompleteStandingProbe(stale, true));
        ManagedStanceCommand invalidated = reducer.Observe(true, true, false).Command;
        reducer.Initialize(true);
        Reject(() => reducer.CompleteStandingProbe(invalidated, true));
    }),
    ("wrong owner rejected at equal revision", () =>
    {
        var first = Crouched();
        var second = Crouched();
        ManagedStanceCommand firstCommand = first.Observe(true, true, false).Command;
        ManagedStanceCommand secondCommand = second.Observe(true, true, false).Command;
        Equal(firstCommand.Revision, secondCommand.Revision);
        Reject(() => second.CompleteStandingProbe(firstCommand, true));
        second.CompleteStandingProbe(secondCommand, true);
    }),
    ("lifecycle selection clears blocked and lethal preserves it", () =>
    {
        var reducer = Blocked();
        reducer.ApplyLethalDamage();
        True(!reducer.Crouched && reducer.StandBlocked);
        reducer.Observe(true, true, true);
        True(reducer.Crouched && reducer.StandBlocked);
        reducer.Initialize(true);
        True(reducer.Crouched && !reducer.StandBlocked);
        reducer.Initialize(false);
        True(!reducer.Crouched && !reducer.StandBlocked);
    }),
    ("inability to act and airborne release preserve stance", () =>
    {
        var reducer = Blocked();
        reducer.Observe(false, true, false);
        True(reducer.Crouched && reducer.StandBlocked);
        reducer.Observe(true, false, false);
        True(reducer.Crouched && reducer.StandBlocked);
    }),
    ("revision exhaustion never wraps or partially commits", () =>
    {
        var reducer = new ManagedStanceReducer();
        SetRevision(reducer, ulong.MaxValue);
        ManagedStanceState before = reducer.State;
        Reject(() => reducer.Observe(true, true, true));
        EqualState(before, reducer.State);
        Reject(() => reducer.Initialize(true));
        EqualState(before, reducer.State);
    }),
    ("generated sequences are deterministic and valid", () =>
    {
        for (uint seed = 1; seed <= 128; seed++) Equal(Replay(seed, 2_000), Replay(seed, 2_000));
    }),
    ("steady-state reduction allocates nothing", () =>
    {
        var reducer = Crouched();
        for (int index = 0; index < 256; index++) Reduce(reducer, index);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++) Reduce(reducer, index);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopManagedStance: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static ManagedStanceReducer Crouched()
{
    var reducer = new ManagedStanceReducer();
    reducer.Observe(true, true, true);
    return reducer;
}

static ManagedStanceReducer Blocked()
{
    ManagedStanceReducer reducer = Crouched();
    reducer.CompleteStandingProbe(reducer.Observe(true, true, false).Command, false);
    return reducer;
}

static int RunAdapterUpdate(ManagedStanceReducer reducer, bool clear, bool fault)
{
    ManagedStanceTransition transition = reducer.Observe(true, true, false);
    if (transition.Command.Kind == ManagedStanceCommandKind.None) return 0;
    int probes = 1;
    try
    {
        if (fault) throw new InvalidOperationException("injected collision fault");
        reducer.CompleteStandingProbe(transition.Command, clear);
    }
    catch when (fault)
    {
        // The real adapter takes its existing abort/reconstruction path without committing stance.
    }
    return probes;
}

static ulong Replay(uint seed, int steps)
{
    var reducer = new ManagedStanceReducer();
    ulong digest = 14695981039346656037UL;
    for (int index = 0; index < steps; index++)
    {
        seed = Next(seed);
        if ((seed & 31) == 0) reducer.Initialize((seed & 32) != 0);
        else if ((seed & 63) == 1) reducer.ApplyLethalDamage();
        else
        {
            ManagedStanceTransition transition = reducer.Observe((seed & 2) != 0,
                (seed & 4) != 0, (seed & 8) != 0);
            if (transition.Command.Kind != ManagedStanceCommandKind.None)
                reducer.CompleteStandingProbe(transition.Command, (seed & 16) != 0);
        }
        ManagedStanceState state = reducer.State;
        True(state.Revision != 0);
        digest = Mix(digest, state.Revision);
        digest = Mix(digest, state.Crouched ? 1UL : 0UL);
        digest = Mix(digest, state.StandBlocked ? 1UL : 0UL);
    }
    return digest;
}

static void Reduce(ManagedStanceReducer reducer, int index)
{
    bool down = (index & 3) != 0;
    ManagedStanceTransition transition = reducer.Observe(true, true, down);
    if (transition.Command.Kind != ManagedStanceCommandKind.None)
        reducer.CompleteStandingProbe(transition.Command, (index & 4) != 0);
}

static uint Next(uint value)
{
    value ^= value << 13;
    value ^= value >> 17;
    value ^= value << 5;
    return value;
}

static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);

static void SetRevision(ManagedStanceReducer reducer, ulong revision)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic;
    typeof(ManagedStanceReducer).GetField("_revision", flags)?.SetValue(reducer, revision);
    Equal(revision, reducer.State.Revision);
}

static void EqualState(ManagedStanceState expected, ManagedStanceState actual)
{
    Equal(expected.Revision, actual.Revision);
    Equal(expected.Crouched, actual.Crouched);
    Equal(expected.StandBlocked, actual.StandBlocked);
}

static void Reject(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Invalid stance transition was accepted.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}
