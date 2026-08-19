using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("baseline and exact counters", () =>
    {
        var machine = new ContactOpportunityMachine();
        ContactObservation[] scan = EmptyScan();
        scan[0] = Body(3, attack: 7);
        scan[127] = Body(9, attack: 0, overlapping: false);
        ContactOpportunityTransition transition = machine.Advance(scan);
        True(!transition.HasWinner && transition.OpportunityCount == 0);
        Equal(1L, transition.State.ScanFrames);
        Equal(128L, transition.State.SlotsScanned);
        Equal(2L, transition.State.EligibleSamples);
        Equal(1L, transition.State.OverlapSamples);
        Equal(1L, transition.State.DamagingSamples);
        Equal(1, transition.State.Current);
        Equal(1, transition.State.Peak);
    }),
    ("entry exit stay and nondamaging activation", () =>
    {
        var machine = Baseline();
        ContactObservation[] scan = EmptyScan();
        scan[4] = Body(4, attack: 0);
        ContactOpportunityTransition entry = machine.Advance(scan);
        Equal(1, entry.State.Entries);
        True(!entry.HasWinner);
        ContactOpportunityTransition stay = machine.Advance(scan);
        Equal(1L, stay.State.StaySamples);
        scan[4] = Body(4, attack: 6);
        ContactOpportunityTransition activated = machine.Advance(scan);
        True(activated.HasWinner);
        Equal(1, activated.OpportunityCount);
        ContactOpportunityTransition exit = machine.Advance(EmptyScan());
        Equal(1, exit.State.Exits);
        Equal(0, exit.State.Current);
    }),
    ("phase packing changes create opportunities", () =>
    {
        var machine = new ContactOpportunityMachine();
        ContactObservation[] scan = EmptyScan();
        scan[1] = Body(1, attack: 5, element: 2, state: 1);
        machine.Advance(scan);
        scan[1] = Body(1, attack: 5, element: 3, state: 1);
        True(machine.Advance(scan).HasWinner);
        scan[1] = Body(1, attack: 5, element: 3, state: 3);
        True(machine.Advance(scan).HasWinner);
    }),
    ("repeat opportunity occurs on scan sixty", () =>
    {
        var machine = new ContactOpportunityMachine();
        ContactObservation[] scan = EmptyScan();
        scan[2] = Body(2, attack: 8);
        machine.Advance(scan);
        for (int repeat = 1; repeat < 60; repeat++) True(!machine.Advance(scan).HasWinner);
        Equal(59, machine.RepeatTickAt(2));
        True(machine.Advance(scan).HasWinner);
        Equal(0, machine.RepeatTickAt(2));
    }),
    ("resume grace seeds repeat fifty nine", () =>
    {
        var machine = new ContactOpportunityMachine();
        ContactObservation[] scan = EmptyScan();
        scan[5] = Body(5, attack: 10);
        machine.Advance(scan);
        for (int index = 1; index < 60; index++) machine.Advance(scan);
        Equal(1, machine.ResumeGraceBudget);
        ContactOpportunityState suspended = machine.Suspend();
        True(suspended.Suspended && suspended.ResumeGracePending);
        ContactOpportunityTransition grace = machine.Advance(scan);
        True(!grace.HasWinner);
        Equal(59, machine.RepeatTickAt(5));
        Equal(1, grace.State.ResumeGraceScans);
        Equal(0, grace.State.ResumeGraceBudget);
        True(machine.Advance(scan).HasWinner);
    }),
    ("resume grace budget is consumed until sixty safe scans", () =>
    {
        var machine = new ContactOpportunityMachine();
        ContactObservation[] scan = EmptyScan();
        scan[5] = Body(5, attack: 10);
        machine.Advance(scan);
        True(machine.Suspend().ResumeGracePending);
        ContactOpportunityState state = machine.Advance(scan).State;
        Equal(1, state.ResumeGraceScans);
        Equal(0, state.ResumeGraceBudget);
        machine.Advance(scan);
        True(!machine.Suspend().ResumeGracePending);
        Equal(1, machine.Advance(scan).State.ResumeGraceScans);
    }),
    ("suspension reset counts once and baseline suspension does not", () =>
    {
        var machine = new ContactOpportunityMachine();
        Equal(0, machine.Suspend().Resets);
        Equal(0, machine.Suspend().Resets);
        machine.Reset();
        machine.Advance(EmptyScan());
        Equal(1, machine.Suspend().Resets);
        Equal(1, machine.Suspend().Resets);
    }),
    ("all opportunities consumed and strongest lower slot wins ties", () =>
    {
        var machine = Baseline();
        ContactObservation[] scan = EmptyScan();
        scan[3] = Body(3, attack: 100, element: 3, centerX: 30);
        scan[7] = Body(7, attack: 40, element: 7, centerX: 70);
        scan[8] = Body(8, attack: 12);
        ContactOpportunityTransition transition = machine.Advance(scan);
        Equal(3, transition.OpportunityCount);
        True(transition.HasWinner);
        Equal(3, transition.Winner.Index);
        Equal(40, transition.Winner.Damage);
        Equal((ushort)3, transition.Winner.Element);
        Equal((short)30, transition.Winner.CenterX);
    }),
    ("positive damage clamps at both boundaries", () =>
    {
        var machine = Baseline();
        ContactObservation[] scan = EmptyScan();
        scan[0] = Body(0, attack: 1);
        Equal(1, machine.Advance(scan).Winner.Damage);
        scan = EmptyScan();
        scan[0] = Body(1, attack: short.MaxValue);
        Equal(40, machine.Advance(scan).Winner.Damage);
    }),
    ("eligibility loss advances unchecked generation", () =>
    {
        var machine = new ContactOpportunityMachine();
        ContactObservation[] scan = EmptyScan();
        scan[6] = Body(6, attack: 1);
        machine.Advance(scan);
        ulong first = machine.IdentityAt(6);
        scan[6] = default;
        machine.Advance(scan);
        Equal(1U, machine.GenerationAt(6));
        scan[6] = Body(6, attack: 1);
        ContactOpportunityTransition reused = machine.Advance(scan);
        True(machine.IdentityAt(6) != first);
        Equal(1, reused.State.Entries);

        SetGeneration(machine, 6, uint.MaxValue, wasEligible: true);
        scan[6] = default;
        machine.Advance(scan);
        Equal(0U, machine.GenerationAt(6));
    }),
    ("identity and phase packing are exact", () =>
    {
        Equal(0x89ABCDEF12345678UL, ContactOpportunityMachine.PackIdentity(0x89ABCDEF, 0x1234, 0x5678));
        Equal(0x0000FFFFABCD1357UL, ContactOpportunityMachine.PackPhase(-1, 0xABCD, 0x1357));
    }),
    ("result snapshots cannot observe later machine mutations", () =>
    {
        var machine = new ContactOpportunityMachine();
        ContactOpportunityState prior = machine.State;
        ContactObservation[] scan = EmptyScan();
        scan[0] = Body(0, attack: 4);
        ContactOpportunityState next = machine.Advance(scan).State;
        machine.Advance(EmptyScan());
        machine.Suspend();
        True(prior.BaselinePending);
        Equal(0L, prior.ScanFrames);
        True(!next.BaselinePending);
        Equal(1L, next.ScanFrames);
        Equal(1, next.Current);
    }),
    ("invalid scan and observation are rejected", () =>
    {
        var machine = new ContactOpportunityMachine();
        RejectArgument(() => machine.Advance(new ContactObservation[127]));
        RejectArgument(() => new ContactObservation(false, true, 1, 1, 1, 1, 1, 1, 1, 1));
        RejectArgument(() => new ContactObservation(true, true, 0, 1, 1, 1, 1, 1, 1, 1));
    }),
    ("generated sequences are deterministic and bounded", () =>
    {
        for (uint seed = 1; seed <= 64; seed++) Equal(Replay(seed, 500), Replay(seed, 500));
    }),
    ("steady state scans and repeated suspends allocate nothing", () =>
    {
        var machine = new ContactOpportunityMachine();
        ContactObservation[] scan = EmptyScan();
        scan[0] = Body(0, attack: 8);
        for (int index = 0; index < 256; index++)
        {
            machine.Advance(scan);
            machine.Suspend();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10_000; index++)
        {
            ContactOpportunityTransition transition = machine.Advance(scan);
            ContactOpportunityState suspended = machine.Suspend();
            if (transition.State.ScanFrames < 0 || suspended.Resets < 0) throw new InvalidOperationException();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated);
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopContactOpportunity: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static ContactOpportunityMachine Baseline()
{
    var machine = new ContactOpportunityMachine();
    machine.Advance(EmptyScan());
    return machine;
}

static ContactObservation[] EmptyScan() => new ContactObservation[ContactOpportunityMachine.SlotCount];

static ContactObservation Body(int identity, short attack, ushort element = 0, ushort state = 1,
    bool overlapping = true, short centerX = 0, short centerY = 0) =>
    new(true, overlapping, (ushort)(identity + 1), (ushort)(identity + 100), (uint)(identity + 1000),
        state, attack, element, centerX, centerY);

static ulong Replay(uint seed, int steps)
{
    var machine = new ContactOpportunityMachine();
    var scan = EmptyScan();
    ulong digest = 14695981039346656037UL;
    for (int step = 0; step < steps; step++)
    {
        seed = Next(seed);
        if ((seed & 31) == 0)
        {
            digest = Mix(digest, (ulong)machine.Suspend().Resets);
            continue;
        }

        Array.Clear(scan);
        for (int index = 0; index < scan.Length; index++)
        {
            seed = Next(seed);
            if ((seed & 7) == 0) continue;
            bool overlap = (seed & 3) == 0;
            short attack = (short)((seed >> 8) % 81 - 20);
            scan[index] = Body((int)((seed >> 16) & 15), attack, (ushort)(seed >> 24),
                (ushort)(1 | ((seed >> 4) & 0x3E)), overlap, (short)index, (short)-index);
        }
        ContactOpportunityTransition transition = machine.Advance(scan);
        ContactOpportunityState state = transition.State;
        Equal(state.ScanFrames * ContactOpportunityMachine.SlotCount, state.SlotsScanned);
        True(state.Current is >= 0 and <= ContactOpportunityMachine.SlotCount);
        True(state.Peak >= state.Current && state.Peak <= ContactOpportunityMachine.SlotCount);
        True(transition.OpportunityCount is >= 0 and <= ContactOpportunityMachine.SlotCount);
        if (transition.HasWinner)
        {
            True(transition.OpportunityCount > 0);
            True(transition.Winner.Index is >= 0 and < ContactOpportunityMachine.SlotCount);
            True(transition.Winner.Damage is >= 1 and <= ContactOpportunityMachine.MaximumDamage);
        }
        digest = Mix(digest, (ulong)state.Current);
        digest = Mix(digest, (ulong)state.Entries);
        digest = Mix(digest, (ulong)state.Exits);
        digest = Mix(digest, (ulong)state.StaySamples);
        digest = Mix(digest, (ulong)transition.OpportunityCount);
        digest = Mix(digest, transition.HasWinner ? (ulong)(transition.Winner.Index + 1) : 0);
    }
    return digest;
}

static uint Next(uint value)
{
    value ^= value << 13;
    value ^= value >> 17;
    value ^= value << 5;
    return value;
}

static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);

static void SetGeneration(ContactOpportunityMachine machine, int index, uint generation, bool wasEligible)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic;
    uint[] generations = (uint[])(typeof(ContactOpportunityMachine).GetField("_generations", flags)?.GetValue(machine)
        ?? throw new InvalidOperationException("Generation test seam is missing."));
    bool[] eligibility = (bool[])(typeof(ContactOpportunityMachine).GetField("_wasEligible", flags)?.GetValue(machine)
        ?? throw new InvalidOperationException("Eligibility test seam is missing."));
    generations[index] = generation;
    eligibility[index] = wasEligible;
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

static void RejectArgument(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Invalid contact input was accepted.");
}
