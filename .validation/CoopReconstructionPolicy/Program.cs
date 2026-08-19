using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("80-command golden order", () =>
    {
        const string golden =
            "0:24,0,S|1:24,0,C|2:-24,0,S|3:-24,0,C|4:32,0,S|5:32,0,C|6:-32,0,S|7:-32,0,C|" +
            "8:40,0,S|9:40,0,C|10:-40,0,S|11:-40,0,C|12:48,0,S|13:48,0,C|14:-48,0,S|15:-48,0,C|" +
            "16:24,-8,S|17:24,-8,C|18:-24,-8,S|19:-24,-8,C|20:32,-8,S|21:32,-8,C|22:-32,-8,S|23:-32,-8,C|" +
            "24:40,-8,S|25:40,-8,C|26:-40,-8,S|27:-40,-8,C|28:48,-8,S|29:48,-8,C|30:-48,-8,S|31:-48,-8,C|" +
            "32:24,8,S|33:24,8,C|34:-24,8,S|35:-24,8,C|36:32,8,S|37:32,8,C|38:-32,8,S|39:-32,8,C|" +
            "40:40,8,S|41:40,8,C|42:-40,8,S|43:-40,8,C|44:48,8,S|45:48,8,C|46:-48,8,S|47:-48,8,C|" +
            "48:24,-16,S|49:24,-16,C|50:-24,-16,S|51:-24,-16,C|52:32,-16,S|53:32,-16,C|54:-32,-16,S|55:-32,-16,C|" +
            "56:40,-16,S|57:40,-16,C|58:-40,-16,S|59:-40,-16,C|60:48,-16,S|61:48,-16,C|62:-48,-16,S|63:-48,-16,C|" +
            "64:24,16,S|65:24,16,C|66:-24,16,S|67:-24,16,C|68:32,16,S|69:32,16,C|70:-32,16,S|71:-32,16,C|" +
            "72:40,16,S|73:40,16,C|74:-40,16,S|75:-40,16,C|76:48,16,S|77:48,16,C|78:-48,16,S|79:-48,16,C";
        var actual = new System.Text.StringBuilder();
        var reducer = new ReconstructionPolicyReducer();
        ReconstructionPolicyTransition transition = reducer.Begin();
        for (int index = 0; index < ReconstructionPolicyReducer.CandidateCount; index++)
        {
            ReconstructionCommand command = transition.Command;
            if (index != 0) actual.Append('|');
            actual.Append(command.Candidate.Index).Append(':').Append(command.Candidate.OffsetX).Append(',')
                .Append(command.Candidate.OffsetY).Append(',').Append(command.Candidate.Crouched ? 'C' : 'S');
            transition = reducer.Observe(command, ReconstructionObservation.Blocked);
        }
        Equal(golden, actual.ToString());
        Equal(ReconstructionPolicyPhase.NoSafeCandidate, transition.State.Phase);
        Equal(ReconstructionCommandKind.None, transition.Command.Kind);
    }),
    ("every candidate can be first success", () =>
    {
        for (int selected = 0; selected < ReconstructionPolicyReducer.CandidateCount; selected++)
        {
            var reducer = new ReconstructionPolicyReducer();
            ReconstructionPolicyTransition transition = reducer.Begin();
            for (int index = 0; index < selected; index++)
                transition = reducer.Observe(transition.Command, ReconstructionObservation.Blocked);
            ReconstructionCommand winner = transition.Command;
            Equal(selected, winner.Candidate.Index);
            transition = reducer.Observe(winner, ReconstructionObservation.Valid);
            Equal(ReconstructionPolicyPhase.Selected, transition.State.Phase);
            Equal(selected, transition.State.CandidateIndex);
            Equal(ReconstructionCommandKind.None, transition.Command.Kind);
        }
    }),
    ("stance and side priority", () =>
    {
        ReconstructionCommand[] first = Commands(6);
        Candidate(first[0], 24, 0, false);
        Candidate(first[1], 24, 0, true);
        Candidate(first[2], -24, 0, false);
        Candidate(first[3], -24, 0, true);
        Candidate(first[4], 32, 0, false);
        Candidate(first[5], 32, 0, true);
    }),
    ("orchestration uses player-relative first middle and last candidates", () =>
    {
        (int Index, int X, int Y, bool Crouched)[] cases =
        [
            (0, 1024, -300, false),
            (40, 1040, -292, false),
            (79, 952, -284, true),
        ];
        foreach ((int index, int expectedX, int expectedY, bool expectedCrouched) in cases)
        {
            var trace = new AdapterTrace(validIndex: index);
            var adapter = new FakeReconstructionAdapter(trace);
            ReconstructionRunResult result = ReconstructionPolicyOrchestration.Run(
                new ReconstructionPolicyReducer(), 1000, -300, ref adapter);
            Equal(ReconstructionRunResult.Selected, result);
            Equal(index + 1, trace.ProbeCount);
            Equal(expectedX, trace.ProbeX[index]);
            Equal(expectedY, trace.ProbeY[index]);
            Equal(expectedCrouched, trace.ProbeCrouched[index]);
            Equal(expectedX, trace.InitializedX);
            Equal(expectedY, trace.InitializedY);
            Equal(expectedCrouched, trace.InitializedCrouched);
            Equal(index + 6, trace.OperationCount);
            AssertSuccessTail(trace, index + 1);
        }
    }),
    ("orchestration blocked probes continue in exact order", () =>
    {
        var trace = new AdapterTrace(validIndex: 3);
        var adapter = new FakeReconstructionAdapter(trace);
        ReconstructionRunResult result = ReconstructionPolicyOrchestration.Run(
            new ReconstructionPolicyReducer(), 200, 500, ref adapter);
        Equal(ReconstructionRunResult.Selected, result);
        Equal(4, trace.ProbeCount);
        Probe(trace, 0, 224, 500, false);
        Probe(trace, 1, 224, 500, true);
        Probe(trace, 2, 176, 500, false);
        Probe(trace, 3, 176, 500, true);
        AssertSuccessTail(trace, 4);
    }),
    ("orchestration Nth collision fault stops without later work", () =>
    {
        for (int faultAt = 1; faultAt <= ReconstructionPolicyReducer.CandidateCount; faultAt++)
        {
            var trace = new AdapterTrace(validIndex: 79, faultAt: faultAt);
            var adapter = new FakeReconstructionAdapter(trace);
            ReconstructionRunResult result = ReconstructionPolicyOrchestration.Run(
                new ReconstructionPolicyReducer(), -50, 70, ref adapter);
            Equal(ReconstructionRunResult.AdapterFault, result);
            Equal(faultAt, trace.ProbeCount);
            Equal(faultAt + 1, trace.OperationCount);
            Equal(AdapterOperation.CollisionFault, trace.Operations[faultAt]);
            Equal(0, trace.InitializeCount);
            Equal(0, trace.PoseValidationCount);
            Equal(0, trace.HealthReconstructionCount);
            Equal(0, trace.SuccessCount);
            Equal(0, trace.NoSafeCount);
        }
    }),
    ("thrown probe becomes adapter fault with no later work", () =>
    {
        var adapter = new ThrowingProbeAdapter();
        Equal(ReconstructionRunResult.AdapterFault, ReconstructionPolicyOrchestration.Run(
            new ReconstructionPolicyReducer(), 0, 0, ref adapter));
        Equal(1, adapter.Probes);
        Equal(0, adapter.LaterWork);
    }),
    ("orchestration exhaustion maps to no safe candidate", () =>
    {
        var trace = new AdapterTrace();
        var adapter = new FakeReconstructionAdapter(trace);
        ReconstructionRunResult result = ReconstructionPolicyOrchestration.Run(
            new ReconstructionPolicyReducer(), 10, 20, ref adapter);
        Equal(ReconstructionRunResult.NoSafeCandidate, result);
        Equal(80, trace.ProbeCount);
        Equal(81, trace.OperationCount);
        Equal(AdapterOperation.NoSafeCandidate, trace.Operations[80]);
        Equal(1, trace.NoSafeCount);
        Equal(0, trace.InitializeCount);
        Equal(0, trace.SuccessCount);
    }),
    ("success commit follows initialize pose validation and managed health", () =>
    {
        var trace = new AdapterTrace(validIndex: 12);
        var adapter = new FakeReconstructionAdapter(trace);
        ReconstructionRunResult result = ReconstructionPolicyOrchestration.Run(
            new ReconstructionPolicyReducer(), 0, 0, ref adapter);
        Equal(ReconstructionRunResult.Selected, result);
        AssertSuccessTail(trace, 13);
        Equal(1, trace.InitializeCount);
        Equal(1, trace.PoseValidationCount);
        Equal(1, trace.HealthReconstructionCount);
        Equal(1, trace.SuccessCount);
        Equal(ManagedHealthMachine.DamageInvulnerabilityUpdates, trace.HealthSeenByDiagnostics);
    }),
    ("all blocked yields no safe candidate", () =>
    {
        var reducer = new ReconstructionPolicyReducer();
        ReconstructionPolicyTransition transition = reducer.Begin();
        int probes = 0;
        while (transition.Command.Kind != ReconstructionCommandKind.None)
        {
            probes++;
            transition = reducer.Observe(transition.Command, ReconstructionObservation.Blocked);
        }
        Equal(80, probes);
        Equal(ReconstructionPolicyPhase.NoSafeCandidate, transition.State.Phase);
    }),
    ("Nth adapter fault suspends without later probe", () =>
    {
        for (int faultAt = 1; faultAt <= 80; faultAt++)
        {
            var reducer = new ReconstructionPolicyReducer();
            ReconstructionPolicyTransition transition = reducer.Begin();
            int probes = 0;
            while (transition.Command.Kind != ReconstructionCommandKind.None)
            {
                probes++;
                transition = reducer.Observe(transition.Command, probes == faultAt
                    ? ReconstructionObservation.AdapterFault
                    : ReconstructionObservation.Blocked);
            }
            Equal(faultAt, probes);
            Equal(ReconstructionPolicyPhase.Suspended, transition.State.Phase);
        }
    }),
    ("stale duplicate wrong-owner and out-of-order fail closed", () =>
    {
        var first = new ReconstructionPolicyReducer();
        var second = new ReconstructionPolicyReducer();
        ReconstructionCommand stale = first.Begin().Command;
        ReconstructionCommand foreign = second.Begin().Command;
        Equal(stale.Revision, foreign.Revision);
        ReconstructionPolicyTransition advanced = first.Observe(stale, ReconstructionObservation.Blocked);
        ReconstructionPolicyState before = first.State;
        Reject(() => first.Observe(stale, ReconstructionObservation.Valid));
        EqualState(before, first.State);
        Reject(() => first.Observe(foreign, ReconstructionObservation.Valid));
        EqualState(before, first.State);

        ReconstructionCandidate wrongCandidate = new(10, 40, 0, false);
        ReconstructionCommand outOfOrder = new(advanced.Command.OwnerId, advanced.Command.Revision, wrongCandidate);
        Reject(() => first.Observe(outOfOrder, ReconstructionObservation.Blocked));
        EqualState(before, first.State);
        first.Observe(advanced.Command, ReconstructionObservation.Valid);
        Reject(() => first.Observe(advanced.Command, ReconstructionObservation.Valid));
    }),
    ("invalid observation fails without partial commit", () =>
    {
        var reducer = new ReconstructionPolicyReducer();
        ReconstructionCommand command = reducer.Begin().Command;
        ReconstructionPolicyState before = reducer.State;
        RejectArgument(() => reducer.Observe(command, (ReconstructionObservation)255));
        EqualState(before, reducer.State);
    }),
    ("revision exhaustion is nonwrapping and atomic", () =>
    {
        var beginReducer = new ReconstructionPolicyReducer();
        SetRevision(beginReducer, ulong.MaxValue);
        ReconstructionPolicyState beforeBegin = beginReducer.State;
        Reject(() => beginReducer.Begin());
        EqualState(beforeBegin, beginReducer.State);

        var observeReducer = new ReconstructionPolicyReducer();
        ReconstructionCommand issued = observeReducer.Begin().Command;
        SetRevision(observeReducer, ulong.MaxValue);
        ReconstructionCommand exhausted = new(issued.OwnerId, ulong.MaxValue, issued.Candidate);
        ReconstructionPolicyState beforeObserve = observeReducer.State;
        Reject(() => observeReducer.Observe(exhausted, ReconstructionObservation.Blocked));
        EqualState(beforeObserve, observeReducer.State);
    }),
    ("generated replay is deterministic", () =>
    {
        for (uint seed = 1; seed <= 128; seed++) Equal(Replay(seed, 1_000), Replay(seed, 1_000));
    }),
    ("steady-state reduction and orchestration allocate nothing", () =>
    {
        var reducer = new ReconstructionPolicyReducer();
        for (int index = 0; index < 256; index++) ReduceAttempt(reducer, index);
        var adapter = new AllocationAdapter();
        for (int index = 0; index < 256; index++)
            ReconstructionPolicyOrchestration.Run(reducer, 100, 200, ref adapter);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++)
            ReconstructionPolicyOrchestration.Run(reducer, 100, 200, ref adapter);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopReconstructionPolicy: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static ReconstructionCommand[] Commands(int count)
{
    var commands = new ReconstructionCommand[count];
    var reducer = new ReconstructionPolicyReducer();
    ReconstructionPolicyTransition transition = reducer.Begin();
    for (int index = 0; index < count; index++)
    {
        commands[index] = transition.Command;
        transition = reducer.Observe(transition.Command, ReconstructionObservation.Blocked);
    }
    return commands;
}

static void ReduceAttempt(ReconstructionPolicyReducer reducer, int value)
{
    ReconstructionPolicyTransition transition = reducer.Begin();
    int blocked = value & 7;
    for (int index = 0; index < blocked; index++)
        transition = reducer.Observe(transition.Command, ReconstructionObservation.Blocked);
    reducer.Observe(transition.Command, (value & 8) == 0
        ? ReconstructionObservation.Valid
        : ReconstructionObservation.AdapterFault);
}

static ulong Replay(uint seed, int attempts)
{
    var reducer = new ReconstructionPolicyReducer();
    ulong hash = 14695981039346656037UL;
    for (int attempt = 0; attempt < attempts; attempt++)
    {
        ReconstructionPolicyTransition transition = reducer.Begin();
        while (transition.Command.Kind != ReconstructionCommandKind.None)
        {
            seed = Next(seed);
            ReconstructionObservation observation = (seed & 31) == 0
                ? ReconstructionObservation.AdapterFault
                : (seed & 7) == 0 ? ReconstructionObservation.Valid : ReconstructionObservation.Blocked;
            transition = reducer.Observe(transition.Command, observation);
        }
        ReconstructionPolicyState state = reducer.State;
        hash = Mix(hash, state.Revision);
        hash = Mix(hash, (ulong)state.Phase);
        hash = Mix(hash, (ulong)state.CandidateIndex);
    }
    return hash;
}

static uint Next(uint value)
{
    value ^= value << 13;
    value ^= value >> 17;
    value ^= value << 5;
    return value;
}

static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);

static void SetRevision(ReconstructionPolicyReducer reducer, ulong revision)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic;
    typeof(ReconstructionPolicyReducer).GetField("_revision", flags)?.SetValue(reducer, revision);
    Equal(revision, reducer.State.Revision);
}

static void Candidate(ReconstructionCommand command, int x, int y, bool crouched)
{
    Equal(x, command.Candidate.OffsetX);
    Equal(y, command.Candidate.OffsetY);
    Equal(crouched, command.Candidate.Crouched);
}

static void Probe(AdapterTrace trace, int index, int x, int y, bool crouched)
{
    Equal(x, trace.ProbeX[index]);
    Equal(y, trace.ProbeY[index]);
    Equal(crouched, trace.ProbeCrouched[index]);
}

static void AssertSuccessTail(AdapterTrace trace, int probeCount)
{
    Equal(AdapterOperation.Initialize, trace.Operations[probeCount]);
    Equal(AdapterOperation.ValidatePoses, trace.Operations[probeCount + 1]);
    Equal(AdapterOperation.ReconstructHealth, trace.Operations[probeCount + 2]);
    Equal(AdapterOperation.SuccessDiagnostics, trace.Operations[probeCount + 3]);
    Equal(AdapterOperation.Commit, trace.Operations[probeCount + 4]);
}

static void EqualState(ReconstructionPolicyState expected, ReconstructionPolicyState actual)
{
    Equal(expected.Revision, actual.Revision);
    Equal(expected.Phase, actual.Phase);
    Equal(expected.CandidateIndex, actual.CandidateIndex);
}

static void Reject(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Invalid reconstruction transition was accepted.");
}

static void RejectArgument(Action action)
{
    try { action(); }
    catch (ArgumentOutOfRangeException) { return; }
    throw new InvalidOperationException("Invalid reconstruction observation was accepted.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

enum AdapterOperation : byte
{
    Probe,
    Initialize,
    ValidatePoses,
    ReconstructHealth,
    SuccessDiagnostics,
    Commit,
    CollisionFault,
    NoSafeCandidate,
}

sealed class AdapterTrace
{
    public readonly int ValidIndex;
    public readonly int FaultAt;
    public readonly int[] ProbeX = new int[ReconstructionPolicyReducer.CandidateCount];
    public readonly int[] ProbeY = new int[ReconstructionPolicyReducer.CandidateCount];
    public readonly bool[] ProbeCrouched = new bool[ReconstructionPolicyReducer.CandidateCount];
    public readonly AdapterOperation[] Operations = new AdapterOperation[ReconstructionPolicyReducer.CandidateCount + 5];
    public int ProbeCount;
    public int OperationCount;
    public int InitializedX;
    public int InitializedY;
    public bool InitializedCrouched;
    public int InitializeCount;
    public int PoseValidationCount;
    public int HealthReconstructionCount;
    public int SuccessCount;
    public int CollisionFaultCount;
    public int NoSafeCount;
    public int HealthSeenByDiagnostics;
    public ManagedHealthState Health = ManagedHealthMachine.Reset();

    public AdapterTrace(int validIndex = -1, int faultAt = -1)
    {
        ValidIndex = validIndex;
        FaultAt = faultAt;
    }

    public void Record(AdapterOperation operation) => Operations[OperationCount++] = operation;
}

readonly struct FakeReconstructionAdapter : IReconstructionPolicyAdapter
{
    private readonly AdapterTrace _trace;

    public FakeReconstructionAdapter(AdapterTrace trace) => _trace = trace;

    public ReconstructionObservation ProbeCandidate(int worldX, int worldY, bool crouched)
    {
        int index = _trace.ProbeCount;
        _trace.Record(AdapterOperation.Probe);
        _trace.ProbeX[index] = worldX;
        _trace.ProbeY[index] = worldY;
        _trace.ProbeCrouched[index] = crouched;
        _trace.ProbeCount++;
        if (_trace.FaultAt == _trace.ProbeCount) return ReconstructionObservation.AdapterFault;
        return _trace.ValidIndex == index ? ReconstructionObservation.Valid : ReconstructionObservation.Blocked;
    }

    public void PrepareInitialization(int worldX, int worldY, bool crouched)
    {
        _trace.Record(AdapterOperation.Initialize);
        _trace.InitializeCount++;
        _trace.InitializedX = worldX;
        _trace.InitializedY = worldY;
        _trace.InitializedCrouched = crouched;
    }

    public void PreparePoseProjection()
    {
        _trace.Record(AdapterOperation.ValidatePoses);
        _trace.PoseValidationCount++;
    }

    public void PrepareHealthProjection()
    {
        _trace.Record(AdapterOperation.ReconstructHealth);
        _trace.HealthReconstructionCount++;
        _trace.Health = ManagedHealthMachine.Reconstructed(_trace.Health);
    }

    public void PrepareSuccessDiagnostics(ReconstructionCandidate candidate)
    {
        _trace.Record(AdapterOperation.SuccessDiagnostics);
    }

    public void CommitPreparedSuccess()
    {
        _trace.Record(AdapterOperation.Commit);
        _trace.SuccessCount++;
        _trace.HealthSeenByDiagnostics = _trace.Health.Invulnerability;
    }


    public void CommitCollisionFault()
    {
        _trace.Record(AdapterOperation.CollisionFault);
        _trace.CollisionFaultCount++;
    }

    public void CommitNoSafeCandidate()
    {
        _trace.Record(AdapterOperation.NoSafeCandidate);
        _trace.NoSafeCount++;
    }
}

struct AllocationAdapter : IReconstructionPolicyAdapter
{
    private int _probe;

    public ReconstructionObservation ProbeCandidate(int worldX, int worldY, bool crouched) =>
        _probe++ % 4 == 3 ? ReconstructionObservation.Valid : ReconstructionObservation.Blocked;

    public void PrepareInitialization(int worldX, int worldY, bool crouched) { }
    public void PreparePoseProjection() { }
    public void PrepareHealthProjection() { }
    public void PrepareSuccessDiagnostics(ReconstructionCandidate candidate) { }
    public void CommitPreparedSuccess() { }
    public void CommitCollisionFault() { }
    public void CommitNoSafeCandidate() { }
}

struct ThrowingProbeAdapter : IReconstructionPolicyAdapter
{
    public int Probes;
    public int LaterWork;
    public ReconstructionObservation ProbeCandidate(int worldX, int worldY, bool crouched)
    {
        Probes++;
        throw new InjectedProbeException();
    }
    public void PrepareInitialization(int worldX, int worldY, bool crouched) => LaterWork++;
    public void PreparePoseProjection() => LaterWork++;
    public void PrepareHealthProjection() => LaterWork++;
    public void PrepareSuccessDiagnostics(ReconstructionCandidate candidate) => LaterWork++;
    public void CommitPreparedSuccess() => LaterWork++;
    public void CommitCollisionFault() => LaterWork++;
    public void CommitNoSafeCandidate() => LaterWork++;
}

sealed class InjectedProbeException : Exception { }
