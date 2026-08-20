using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("third safe update requests initialization", () =>
    {
        ManagedMovementSessionReducer session = NewSession();
        False(session.ObserveSafeRoom(Room(1)).ReconstructionRequested);
        False(session.ObserveSafeRoom(Room(1)).ReconstructionRequested);
        ManagedMovementSessionTransition third = session.ObserveSafeRoom(Room(1));
        True(third.ReconstructionRequested);
        Equal(3, third.State.SafeUpdates);
        Equal(3, third.State.RoomStableUpdates);
        session.CompleteReconstruction(third.Reconstruction, ManagedMovementReconstructionResult.Selected);
        True(session.State.ProxyInitialized && session.SnapshotEligible);
    }),
    ("unsafe observations restart stabilization", () =>
    {
        ManagedMovementSessionReducer session = NewSession();
        session.ObserveSafeRoom(Room(1));
        session.ObserveSafeRoom(Room(1));
        session.ObserveUnsafe();
        Equal(0, session.State.SafeUpdates);
        False(session.ObserveSafeRoom(Room(1)).ReconstructionRequested);
        False(session.ObserveSafeRoom(Room(1)).ReconstructionRequested);
        True(session.ObserveSafeRoom(Room(1)).ReconstructionRequested);
    }),
    ("room change owns epoch transition and stable thirty", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        ulong priorEpoch = session.RoomEpoch;
        ManagedMovementSessionTransition changed = session.ObserveSafeRoom(Room(2));
        True(session.State.TransitionPending);
        False(changed.ReconstructionRequested);
        Equal(priorEpoch + 1, session.RoomEpoch);
        False(session.ObserveSafeRoom(Room(2)).ReconstructionRequested);
        ManagedMovementSessionTransition reconstruct = session.ObserveSafeRoom(Room(2));
        True(reconstruct.ReconstructionRequested);
        session.CompleteReconstruction(reconstruct.Reconstruction, ManagedMovementReconstructionResult.Selected);
        while (session.State.RoomStableUpdates < ManagedMovementSessionReducer.TransitionStableUpdates)
            session.ObserveSafeRoom(Room(2));
        False(session.State.TransitionPending);
        Equal(1, session.State.CompletedTransitions);
        True(session.State.AwaitingPostTransitionMovement);
    }),
    ("same-room layer transition does not count room completion", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        session.RoomLayerLoaded();
        Equal(1, session.State.RoomLayerEvents);
        ManagedMovementSessionTransition reconstruction = default;
        for (int update = 0; update < 30; update++)
        {
            ManagedMovementSessionTransition next = session.ObserveSafeRoom(Room(1));
            if (next.ReconstructionRequested)
            {
                reconstruction = next;
                session.CompleteReconstruction(next.Reconstruction, ManagedMovementReconstructionResult.Selected);
            }
        }
        True(reconstruction.ReconstructionRequested);
        False(session.State.TransitionPending);
        Equal(0, session.State.CompletedTransitions);
    }),
    ("bounds-only churn stays one completion without abandonment", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        var settledA = new ManagedRoomKey(1, 2, 0, 0, 0, 256, 240);
        var settledB = new ManagedRoomKey(1, 2, 0, 16, 0, 272, 240);
        for (int update = 0; update < 40; update++)
        {
            ManagedMovementSessionTransition next = session.ObserveSafeRoom(settledA);
            if (next.ReconstructionRequested)
                session.CompleteReconstruction(next.Reconstruction, ManagedMovementReconstructionResult.Selected);
        }
        False(session.State.TransitionPending);
        Equal(1, session.State.CompletedTransitions);
        True(session.State.AwaitingPostTransitionMovement);
        ulong settledEpoch = session.RoomEpoch;
        for (int update = 0; update < 40; update++)
        {
            ManagedMovementSessionTransition churn = session.ObserveSafeRoom(settledB);
            False(churn.ReconstructionRequested);
        }
        Equal(1, session.State.CompletedTransitions);
        Equal(0, session.State.PostTransitionAbandonments);
        True(session.State.AwaitingPostTransitionMovement);
        True(session.State.ProxyInitialized);
        Equal(settledEpoch, session.RoomEpoch);
        True(session.State.Room.Equals(settledB));
        True(session.ObservePostTransitionMovement(ManagedMovementSessionReducer.PostTransitionAcceptanceRaw));
        Equal(1, session.State.PassedTransitions);
        Equal(0, session.State.PostTransitionAbandonments);
    }),
    ("bounds churn before completion cannot double count one crossing", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        var scrolledA = new ManagedRoomKey(1, 2, 0, 0, 0, 256, 240);
        var scrolledB = new ManagedRoomKey(1, 2, 0, 32, 0, 288, 240);
        for (int update = 0; update < 60; update++)
        {
            ManagedMovementSessionTransition next = session.ObserveSafeRoom(
                (update & 1) == 0 ? scrolledA : scrolledB);
            if (next.ReconstructionRequested)
                session.CompleteReconstruction(next.Reconstruction, ManagedMovementReconstructionResult.Selected);
        }
        False(session.State.TransitionPending);
        Equal(1, session.State.CompletedTransitions);
        Equal(0, session.State.PostTransitionAbandonments);
        True(session.State.AwaitingPostTransitionMovement);
    }),
    ("manual reset is eligible on next safe update", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        session.RequestManualReset();
        Equal(1, session.State.ManualResetRequests);
        ManagedMovementSessionTransition reset = session.ObserveSafeRoom(Room(1));
        True(reset.ReconstructionRequested);
        session.CompleteReconstruction(reset.Reconstruction, ManagedMovementReconstructionResult.Selected);
        False(session.State.ManualResetPending);
        Equal(1, session.State.ManualResetCompletions);
    }),
    ("hard failure retries without another stabilization delay", () =>
    {
        ManagedMovementSessionReducer session = NewSession();
        ManagedMovementSessionTransition first = Trigger(session, Room(1));
        session.CompleteReconstruction(first.Reconstruction, ManagedMovementReconstructionResult.NoSafeCandidate);
        True(session.State.ReconstructionHardFailure);
        Equal(1, session.State.ReconstructionFailures);
        ManagedMovementSessionTransition retry = session.ObserveSafeRoom(Room(1));
        True(retry.ReconstructionRequested);
        session.CompleteReconstruction(retry.Reconstruction, ManagedMovementReconstructionResult.Selected);
        False(session.State.ReconstructionHardFailure);
        Equal(2, session.State.ReconstructionAttempts);
    }),
    ("collision and tether recoveries suppress snapshots immediately", () =>
    {
        ManagedMovementSessionReducer collision = Active(Room(1));
        collision.BeginRecovery(ManagedMovementRecoveryKind.Collision);
        False(collision.SnapshotEligible);
        Equal(0, collision.State.TetherRecoveries);
        ManagedMovementSessionReducer tether = Active(Room(1));
        tether.BeginRecovery(ManagedMovementRecoveryKind.Tether);
        False(tether.SnapshotEligible);
        Equal(1, tether.State.TetherRecoveries);
    }),
    ("post-transition acceptance is exactly eight pixels", () =>
    {
        ManagedMovementSessionReducer session = ChangedRoomTransition();
        False(session.ObservePostTransitionMovement(ManagedMovementSessionReducer.PostTransitionAcceptanceRaw - 1));
        True(session.State.AwaitingPostTransitionMovement);
        True(session.ObservePostTransitionMovement(1));
        Equal(1, session.State.PassedTransitions);
        False(session.State.AwaitingPostTransitionMovement);
    }),
    ("transition duration failure and abandonment evidence is truthful", () =>
    {
        ManagedMovementSessionReducer completed = ChangedRoomTransition();
        Equal(0, completed.State.TransitionPendingUpdates);
        Equal(ManagedMovementSessionReducer.TransitionStableUpdates,
            completed.State.TransitionPendingMaxUpdates);
        completed.ObserveSafeRoom(Room(3));
        Equal(1, completed.State.PostTransitionAbandonments);

        ManagedMovementSessionReducer failed = Active(Room(1));
        failed.ObserveSafeRoom(Room(2));
        failed.ObserveSafeRoom(Room(2));
        ManagedMovementSessionTransition reconstruction = failed.ObserveSafeRoom(Room(2));
        failed.CompleteReconstruction(reconstruction.Reconstruction,
            ManagedMovementReconstructionResult.NoSafeCandidate);
        Equal(1, failed.State.TransitionReconstructionFailures);
    }),
    ("reload reset fatal and unload phases fail closed", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        ulong epoch = session.RoomEpoch;
        session.PlayerReloaded();
        False(session.State.RoomKnown || session.State.ProxyInitialized);
        Equal(epoch + 1, session.RoomEpoch);
        Trigger(session, Room(1));
        session.DiagnosticReset();
        Equal(0, session.State.ReconstructionAttempts);
        session.Fatal();
        Equal(ManagedMovementSessionPhase.Fatal, session.State.Phase);
        Reject(() => session.ObserveSafeRoom(Room(1)));
        session.Unload();
        Equal(ManagedMovementSessionPhase.Unloaded, session.State.Phase);
        Reject(() => session.RequestManualReset());
    }),
    ("fatal diagnostic reset atomically recovers session health and projections", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        session.RequestManualReset();
        ManagedHealthState health = ManagedHealthMachine.ApplyIncomingHit(
            ManagedHealthMachine.Reset(), 40, 7, 0x20, false).State;
        Equal(60, health.Hp);
        True(session.State.ProxyInitialized && session.State.ManualResetPending);
        ulong epoch = session.RoomEpoch;

        session.Fatal();
        False(session.State.ProxyInitialized);
        ManagedMovementSessionState beforePreflight = session.State;
        session.ValidateDiagnosticReset();
        EqualState(beforePreflight, session.State);
        session.DiagnosticReset();
        health = ManagedHealthMachine.Reset(); // Adapter projection follows authorized session reset.

        Equal(ManagedMovementSessionPhase.WaitingForSafeUpdate, session.State.Phase);
        False(session.State.RoomKnown || session.State.ProxyInitialized ||
            session.State.ManualResetPending || session.State.ReconstructionHardFailure);
        Equal(0, session.State.ManualResetRequests);
        Equal(0, session.State.ManualResetCompletions);
        Equal(0, session.State.ReconstructionAttempts);
        Equal(ManagedHealthMachine.MaximumHp, health.Hp);
        Equal(epoch, session.RoomEpoch);

        ManagedMovementSessionTransition reconstruction = Trigger(session, Room(1));
        Equal(epoch, session.RoomEpoch); // Same-room diagnostic reconciliation does not advance.
        session.CompleteReconstruction(reconstruction.Reconstruction,
            ManagedMovementReconstructionResult.Selected);
        True(session.SnapshotEligible);
    }),
    ("unloaded diagnostic reset remains rejected without mutation", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        session.Unload();
        ManagedMovementSessionState before = session.State;
        Reject(session.ValidateDiagnosticReset);
        EqualState(before, session.State);
        Reject(session.DiagnosticReset);
        EqualState(before, session.State);
    }),
    ("prepared diagnostic reset is owner revision bound", () =>
    {
        ManagedMovementSessionReducer first = Active(Room(1));
        ManagedMovementSessionReducer second = Active(Room(1));
        ManagedMovementDiagnosticResetCommand command = first.PrepareDiagnosticReset();
        ManagedMovementSessionState secondBefore = second.State;
        False(second.CommitDiagnosticReset(command));
        EqualState(secondBefore, second.State);
        True(first.CommitDiagnosticReset(command));
        Equal(ManagedMovementSessionPhase.WaitingForSafeUpdate, first.State.Phase);
        False(first.CommitDiagnosticReset(command));
    }),
    ("default stale duplicate wrong-owner and out-of-phase completions fail closed", () =>
    {
        ManagedMovementSessionReducer first = NewSession();
        ManagedMovementSessionReducer second = NewSession();
        ManagedMovementSessionTransition a = Trigger(first, Room(1));
        ManagedMovementSessionTransition b = Trigger(second, Room(1));
        Reject(() => first.CompleteReconstruction(default, ManagedMovementReconstructionResult.Selected));
        Reject(() => first.CompleteReconstruction(b.Reconstruction, ManagedMovementReconstructionResult.Selected));
        first.CompleteReconstruction(a.Reconstruction, ManagedMovementReconstructionResult.Selected);
        Reject(() => first.CompleteReconstruction(a.Reconstruction, ManagedMovementReconstructionResult.Selected));
        Reject(() => second.ObserveSafeRoom(Room(1)));
        second.BeginRecovery(ManagedMovementRecoveryKind.Collision);
        Reject(() => second.CompleteReconstruction(b.Reconstruction, ManagedMovementReconstructionResult.Selected));
    }),
    ("invalid events leave state unchanged", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        ManagedMovementSessionState before = session.State;
        RejectArgument(() => session.BeginRecovery((ManagedMovementRecoveryKind)255));
        EqualState(before, session.State);
        RejectArgument(() => session.ObservePostTransitionMovement(-1));
        EqualState(before, session.State);
    }),
    ("revision exhaustion is nonwrapping", () =>
    {
        ManagedMovementSessionReducer session = Active(Room(1));
        SetRevision(session, ulong.MaxValue);
        ManagedMovementSessionState before = session.State;
        Reject(() => session.RequestManualReset());
        EqualState(before, session.State);

        ManagedMovementSessionReducer fatal = Active(Room(1));
        fatal.Fatal();
        SetRevision(fatal, ulong.MaxValue);
        ManagedMovementSessionState fatalBefore = fatal.State;
        Reject(fatal.DiagnosticReset);
        EqualState(fatalBefore, fatal.State);
    }),
    ("checked counter exhaustion is atomic", () =>
    {
        ManagedMovementSessionReducer requests = Active(Room(1));
        SetField(requests, "_manualResetRequests", int.MaxValue);
        ManagedMovementSessionState requestBefore = requests.State;
        RejectOverflow(requests.RequestManualReset);
        EqualState(requestBefore, requests.State);

        ManagedMovementSessionReducer attempts = Active(Room(1));
        attempts.BeginRecovery(ManagedMovementRecoveryKind.Collision);
        attempts.ObserveSafeRoom(Room(1));
        attempts.ObserveSafeRoom(Room(1));
        SetField(attempts, "_reconstructionAttempts", int.MaxValue);
        ManagedMovementSessionState attemptBefore = attempts.State;
        RejectOverflow(() => attempts.ObserveSafeRoom(Room(1)));
        EqualState(attemptBefore, attempts.State);

        ManagedMovementSessionReducer pending = Active(Room(1));
        pending.RoomLayerLoaded();
        SetField(pending, "_transitionPendingUpdates", int.MaxValue);
        ManagedMovementSessionState pendingBefore = pending.State;
        RejectOverflow(pending.ObserveUnsafe);
        EqualState(pendingBefore, pending.State);
    }),
    ("generated lifecycle replay is deterministic", () =>
    {
        for (uint seed = 1; seed <= 128; seed++) Equal(Replay(seed, 500), Replay(seed, 500));
    }),
    ("reconstruction Nth preparation faults stop without rollback work", () =>
    {
        for (int faultAt = 1; faultAt <= 5; faultAt++)
        {
            var state = new FaultState(faultAt);
            var adapter = new FaultAdapter(state);
            ReconstructionRunResult result = ReconstructionPolicyOrchestration.Run(
                new ReconstructionPolicyReducer(), 100, 200, ref adapter);
            Equal(ReconstructionRunResult.AdapterFault, result);
            Equal(0, state.AuthoritativeProxy);
            Equal(0, state.AuthoritativeHealth);
            Equal(0, state.SuccessDiagnostics);
            Equal(faultAt, state.Step);
        }
    }),
    ("production shared commit seam is atomic at every preparation and commit boundary", () =>
    {
        for (int faultAt = 1; faultAt <= 14; faultAt++)
        {
            CommitFixture fixture = CommitFixture.Create(faultAt);
            CommitProjection before = fixture.Projection();
            var adapter = new ProductionEvidenceAdapter(fixture);
            Equal(ReconstructionRunResult.AdapterFault,
                ManagedReconstructionCommitOrchestration.Run(ref adapter));
            Equal(before, fixture.Projection());
            Equal(faultAt, fixture.Step);
        }
    }),
    ("production shared commit seam publishes all real projections once", () =>
    {
        CommitFixture fixture = CommitFixture.Create(0);
        CommitProjection before = fixture.Projection();
        var adapter = new ProductionEvidenceAdapter(fixture);
        Equal(ReconstructionRunResult.Selected,
            ManagedReconstructionCommitOrchestration.Run(ref adapter));
        CommitProjection after = fixture.Projection();
        True(!before.Equals(after));
        Equal(14, fixture.Step);
        True(fixture.Session.SnapshotEligible);
        Equal(60, fixture.Health.Invulnerability);
        Equal(123 * 0x10000, fixture.X);
        Equal(-45 * 0x10000, fixture.Y);
        Equal("ROOM:R24/Y0/S", fixture.Diagnostics);
    }),
    ("real reducer preparation exhaustion leaves every projection unchanged", () =>
    {
        string[] fields = ["stance", "jump", "locomotion", "session"];
        foreach (string field in fields)
        {
            CommitFixture fixture = CommitFixture.Create(0);
            if (field == "stance") SetField(fixture.Stance, "_revision", ulong.MaxValue);
            else if (field == "jump") SetField(fixture.Jump, "_revision", ulong.MaxValue);
            else if (field == "locomotion")
                SetField(fixture.Locomotion, "_initializationRevision", ulong.MaxValue);
            else SetField(fixture.Session, "_revision", ulong.MaxValue);
            CommitProjection before = fixture.Projection();
            var adapter = new ProductionEvidenceAdapter(fixture);
            Equal(ReconstructionRunResult.AdapterFault,
                ManagedReconstructionCommitOrchestration.Run(ref adapter));
            Equal(before, fixture.Projection());
        }
    }),
    ("successful transaction publishes state once after all preparations", () =>
    {
        var state = new FaultState(0);
        var adapter = new FaultAdapter(state);
        Equal(ReconstructionRunResult.Selected, ReconstructionPolicyOrchestration.Run(
            new ReconstructionPolicyReducer(), 100, 200, ref adapter));
        Equal(1, state.AuthoritativeProxy);
        Equal(1, state.AuthoritativeHealth);
        Equal(1, state.SuccessDiagnostics);
        Equal(5, state.Step);
    }),
    ("warmed lifecycle and orchestration allocate nothing", () =>
    {
        ManagedMovementSessionReducer session = NewSession();
        ManagedRoomKey room = Room(1);
        for (int index = 0; index < 256; index++) Cycle(session, room);
        var adapter = new AllocationAdapter();
        for (int index = 0; index < 256; index++)
            ReconstructionPolicyOrchestration.Run(new ReconstructionPolicyReducer(), 1, 2, ref adapter);
        // Reuse one policy: Begin deliberately supports retry from every terminal phase.
        var policy = new ReconstructionPolicyReducer();
        var commit = new AllocationCommitAdapter();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++)
        {
            Cycle(session, room);
            ReconstructionPolicyOrchestration.Run(policy, 1, 2, ref adapter);
            ManagedReconstructionCommitOrchestration.Run(ref commit);
        }
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopMovementSession: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static ManagedMovementSessionReducer NewSession()
{
    var session = new ManagedMovementSessionReducer(new RoomEpochTracker());
    session.Load();
    return session;
}

static ManagedMovementSessionReducer Active(ManagedRoomKey room)
{
    ManagedMovementSessionReducer session = NewSession();
    ManagedMovementSessionTransition reconstruction = Trigger(session, room);
    session.CompleteReconstruction(reconstruction.Reconstruction, ManagedMovementReconstructionResult.Selected);
    return session;
}

static ManagedMovementSessionTransition Trigger(ManagedMovementSessionReducer session, ManagedRoomKey room)
{
    ManagedMovementSessionTransition transition = default;
    while (!transition.ReconstructionRequested) transition = session.ObserveSafeRoom(room);
    return transition;
}

static ManagedMovementSessionReducer ChangedRoomTransition()
{
    ManagedMovementSessionReducer session = Active(Room(1));
    session.ObserveSafeRoom(Room(2));
    session.ObserveSafeRoom(Room(2));
    ManagedMovementSessionTransition reconstruction = session.ObserveSafeRoom(Room(2));
    session.CompleteReconstruction(reconstruction.Reconstruction, ManagedMovementReconstructionResult.Selected);
    while (session.State.RoomStableUpdates < ManagedMovementSessionReducer.TransitionStableUpdates)
        session.ObserveSafeRoom(Room(2));
    return session;
}

static void Cycle(ManagedMovementSessionReducer session, ManagedRoomKey room)
{
    session.BeginRecovery(ManagedMovementRecoveryKind.Collision);
    session.ObserveSafeRoom(room);
    session.ObserveSafeRoom(room);
    ManagedMovementSessionTransition reconstruction = session.ObserveSafeRoom(room);
    session.CompleteReconstruction(reconstruction.Reconstruction, ManagedMovementReconstructionResult.Selected);
}

static ulong Replay(uint seed, int count)
{
    ManagedMovementSessionReducer session = Active(Room(1));
    ulong hash = 14695981039346656037UL;
    for (int index = 0; index < count; index++)
    {
        seed = Next(seed);
        if ((seed & 7) == 0) session.RequestManualReset();
        ManagedMovementSessionTransition transition = session.ObserveSafeRoom(Room((byte)(1 + ((seed >> 8) & 1))));
        if (transition.ReconstructionRequested)
            session.CompleteReconstruction(transition.Reconstruction, (seed & 31) == 1
                ? ManagedMovementReconstructionResult.NoSafeCandidate
                : ManagedMovementReconstructionResult.Selected);
        ManagedMovementSessionState state = session.State;
        hash = Mix(hash, state.Revision);
        hash = Mix(hash, (ulong)state.Phase);
        hash = Mix(hash, (ulong)state.ReconstructionAttempts);
        hash = Mix(hash, session.RoomEpoch);
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
static ManagedRoomKey Room(byte room) => new(1, room, 0, 0, 0, 256, 240);

static void SetRevision(ManagedMovementSessionReducer reducer, ulong revision)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic;
    typeof(ManagedMovementSessionReducer).GetField("_revision", flags)!.SetValue(reducer, revision);
}

static void SetField(object reducer, string name, object value)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic;
    reducer.GetType().GetField(name, flags)!.SetValue(reducer, value);
}

static void EqualState(ManagedMovementSessionState expected, ManagedMovementSessionState actual)
{
    Equal(expected.Revision, actual.Revision);
    Equal(expected.Phase, actual.Phase);
    Equal(expected.SafeUpdates, actual.SafeUpdates);
    Equal(expected.ProxyInitialized, actual.ProxyInitialized);
    Equal(expected.ReconstructionAttempts, actual.ReconstructionAttempts);
}

static void Reject(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Invalid transition was accepted.");
}

static void RejectArgument(Action action)
{
    try { action(); }
    catch (ArgumentOutOfRangeException) { return; }
    throw new InvalidOperationException("Invalid event was accepted.");
}

static void RejectOverflow(Action action)
{
    try { action(); }
    catch (OverflowException) { return; }
    throw new InvalidOperationException("Exhausted counter was accepted.");
}

static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
static void False(bool value) => True(!value);
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

sealed class FaultState
{
    public readonly int FaultAt;
    public int Step;
    public int AuthoritativeProxy;
    public int AuthoritativeHealth;
    public int SuccessDiagnostics;
    public FaultState(int faultAt) => FaultAt = faultAt;
    public void Next() { Step++; if (Step == FaultAt) throw new InjectedFault(); }
}

sealed class InjectedFault : Exception { }

readonly record struct CommitProjection(int X, int Y, int VelocityX, int VelocityY, bool Grounded,
    bool JumpPending, int AttackTimer, bool AttackPending, ManagedStanceState Stance,
    JumpForgivenessState Jump, ManagedLocomotionState Locomotion, ManagedHealthState Health,
    ManagedMovementSessionState Session, int PoseProjection, string Diagnostics);

sealed class CommitFixture
{
    public readonly ManagedStanceReducer Stance = new();
    public readonly JumpForgivenessReducer Jump = new();
    public readonly ManagedLocomotionReducer Locomotion = new();
    public readonly ManagedMovementSessionReducer Session;
    public readonly ManagedMovementReconstructionContinuation Continuation;
    public readonly int FaultAt;
    public int Step;
    public int X = 17;
    public int Y = 29;
    public int VelocityX = 3;
    public int VelocityY = 4;
    public bool Grounded;
    public bool JumpPending = true;
    public int AttackTimer = 5;
    public bool AttackPending = true;
    public ManagedHealthState Health;
    public int PoseProjection = 7;
    public string Diagnostics = "BEFORE";

    private CommitFixture(int faultAt)
    {
        FaultAt = faultAt;
        Session = new ManagedMovementSessionReducer(new RoomEpochTracker());
        Session.Load();
        ManagedRoomKey room = new(1, 1, 0, 0, 0, 256, 240);
        ManagedMovementSessionTransition transition = default;
        while (!transition.ReconstructionRequested) transition = Session.ObserveSafeRoom(room);
        Continuation = transition.Reconstruction;
        Stance.Initialize(true);
        Jump.BeginUpdate(true, false, false);
        Locomotion.Update(new ManagedLocomotionObservation(false, false, false, 0, false,
            false, false, 0, 1, false));
        Health = ManagedHealthMachine.Reset();
    }

    public static CommitFixture Create(int faultAt) => new(faultAt);

    public void Boundary()
    {
        Step++;
        if (Step == FaultAt) throw new InjectedFault();
    }

    public CommitProjection Projection() => new(X, Y, VelocityX, VelocityY, Grounded, JumpPending,
        AttackTimer, AttackPending, Stance.State, Jump.State, Locomotion.State, Health,
        Session.State, PoseProjection, Diagnostics);
}

struct ProductionEvidenceAdapter : IManagedReconstructionCommitAdapter
{
    private readonly CommitFixture _fixture;
    private ManagedStanceInitialization _stance;
    private JumpForgivenessClearPreparation _jump;
    private ManagedLocomotionInitialization _locomotion;
    private ManagedHealthState _health;
    private ManagedMovementReconstructionCompletion _session;
    private int _x;
    private int _y;
    private int _pose;
    private string? _diagnostics;

    public ProductionEvidenceAdapter(CommitFixture fixture)
    {
        _fixture = fixture;
        _stance = default;
        _jump = default;
        _locomotion = default;
        _health = fixture.Health;
        _session = default;
        _x = _y = _pose = 0;
        _diagnostics = null;
    }

    public void PrepareScalars()
    {
        _fixture.Boundary();
        _x = checked(123 * 0x10000);
        _y = checked(-45 * 0x10000);
    }

    public void PrepareStance()
    {
        _fixture.Boundary();
        _stance = _fixture.Stance.PrepareInitialization(false);
    }

    public void PrepareJump()
    {
        _fixture.Boundary();
        _jump = _fixture.Jump.PrepareClear();
    }

    public void PrepareLocomotion()
    {
        _fixture.Boundary();
        _locomotion = _fixture.Locomotion.PrepareInitialization();
    }

    public void ValidatePoseProjection() { _fixture.Boundary(); _pose = 11; }
    public void PrepareHealthProjection()
    {
        _fixture.Boundary();
        _health = ManagedHealthMachine.Reconstructed(_fixture.Health);
    }
    public void PrepareSessionCompletion()
    {
        _fixture.Boundary();
        _session = _fixture.Session.PrepareReconstructionCompletion(_fixture.Continuation,
            ManagedMovementReconstructionResult.Selected);
    }
    public void PrepareDiagnostics() { _fixture.Boundary(); _diagnostics = "ROOM:R24/Y0/S"; }

    public bool CanCommit() => _diagnostics != null &&
        _fixture.Stance.CanCommitInitialization(_stance) &&
        _fixture.Jump.CanCommitClear(_jump) &&
        _fixture.Locomotion.CanCommitInitialization(_locomotion) &&
        _fixture.Session.CanCommitReconstructionCompletion(_session);

    public bool CommitPrepared()
    {
        // Injection points model each production publication boundary. All are checked before the
        // first write, matching the production requirement that commit itself cannot fault.
        for (int boundary = 0; boundary < 6; boundary++) _fixture.Boundary();
        _fixture.X = _x;
        _fixture.Y = _y;
        _fixture.VelocityX = _fixture.VelocityY = 0;
        _fixture.Grounded = true;
        _fixture.JumpPending = false;
        _fixture.AttackTimer = 0;
        _fixture.AttackPending = false;
        _fixture.Stance.CommitPreparedInitialization(_stance);
        _fixture.Jump.CommitPreparedClear(_jump);
        _fixture.Locomotion.CommitPreparedInitialization(_locomotion);
        _fixture.PoseProjection = _pose;
        _fixture.Health = _health;
        _fixture.Diagnostics = _diagnostics!;
        _fixture.Session.CommitPreparedReconstructionCompletion(_session);
        return true;
    }
}

readonly struct FaultAdapter : IReconstructionPolicyAdapter
{
    private readonly FaultState _state;
    public FaultAdapter(FaultState state) => _state = state;
    public ReconstructionObservation ProbeCandidate(int worldX, int worldY, bool crouched) => ReconstructionObservation.Valid;
    public void PrepareInitialization(int worldX, int worldY, bool crouched) => _state.Next();
    public void PreparePoseProjection() => _state.Next();
    public void PrepareHealthProjection() => _state.Next();
    public void PrepareSuccessDiagnostics(ReconstructionCandidate candidate) => _state.Next();
    public void CommitPreparedSuccess()
    {
        _state.Next();
        _state.AuthoritativeProxy = _state.AuthoritativeHealth = _state.SuccessDiagnostics = 1;
    }
    public void CommitCollisionFault() { }
    public void CommitNoSafeCandidate() { }
}

struct AllocationAdapter : IReconstructionPolicyAdapter
{
    public ReconstructionObservation ProbeCandidate(int worldX, int worldY, bool crouched) => ReconstructionObservation.Valid;
    public void PrepareInitialization(int worldX, int worldY, bool crouched) { }
    public void PreparePoseProjection() { }
    public void PrepareHealthProjection() { }
    public void PrepareSuccessDiagnostics(ReconstructionCandidate candidate) { }
    public void CommitPreparedSuccess() { }
    public void CommitCollisionFault() { }
    public void CommitNoSafeCandidate() { }
}

struct AllocationCommitAdapter : IManagedReconstructionCommitAdapter
{
    public void PrepareScalars() { }
    public void PrepareStance() { }
    public void PrepareJump() { }
    public void PrepareLocomotion() { }
    public void ValidatePoseProjection() { }
    public void PrepareHealthProjection() { }
    public void PrepareSessionCompletion() { }
    public void PrepareDiagnostics() { }
    public bool CanCommit() => true;
    public bool CommitPrepared() => true;
}
