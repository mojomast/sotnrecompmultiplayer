using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("contact publication has exact final-live order", ContactPublication),
    ("projectile deactivates mutates and republishes", ProjectileWindow),
    ("exactly one native main-engine observation", ObservationGeneration),
    ("duplicate stale and out-of-order events fail closed", InvalidEvents),
    ("invalid exact-owned cleanup faults retain retry authority", InvalidEventCleanupFault),
    ("ownership mismatch performs no later work", OwnershipMismatch),
    ("initial nth-operation faults are contained", InitialFaults),
    ("partial tuple faults synchronously roll back", PartialTupleRollback),
    ("rollback faults retry then retain terminal evidence", RollbackEvidence),
    ("every rollback operation retries synchronously", RollbackNthFaults),
    ("projectile nth-operation faults are contained", ProjectileFaults),
    ("cleanup and rollback nth-operation faults are contained", CleanupFaults),
    ("generated contact projectile lifecycle sequences", GeneratedSequences),
    ("target capture conditional paths and Nth faults", TargetCapturePaths),
    ("target observation one and max paths and Nth faults", TargetObservationPaths),
    ("ordinary publication windows allocate nothing", AllocationFreeWindows),
    ("unload cleanup success", UnloadSuccess),
    ("unload cleanup fault retries synchronously", UnloadRetry),
    ("unload memory unavailable records residual evidence", UnloadMemoryUnavailable),
    ("unload ownership mismatch stops without clearing", UnloadMismatch),
    ("reset preflight exact and free cleanup", ResetPreflightSuccess),
    ("reset preflight memory refusal is atomic", ResetPreflightMemoryUnavailable),
    ("reset preflight cleanup fault refuses then retries", ResetPreflightRetry),
    ("reset preflight observed reuse carries terminal authority", ResetPreflightReuse),
    ("shared reset refuses every exhaustion before native work", SharedResetExhaustion),
    ("shared prepared reset commits exact and free cleanup", SharedResetSuccess),
    ("shared prepared reset carries observed reuse", SharedResetReuse),
    ("prepared publication reset rejects phase ABA", SharedResetPublicationAba),
    ("marker projection is exact in publication and cleanup update", MarkerProjectionSameUpdate),
    ("marker projection preserves fail-closed orphan detection", MarkerProjectionOrphan),
    ("M4 target tie is incoming damage arbitration already covered", IncomingTieScope)
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopAttackPublication: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static void MarkerProjectionSameUpdate()
{
    Span<AttackMarkerObservation> slots = stackalloc AttackMarkerObservation[2];
    slots[0] = new(17, true, 4, 9);
    Equal(new AttackMarkerProjection(1, 0), AttackMarkerCensus.Project(slots, 17, 4, 9, -1, 0, 0));
    slots[0] = default; // The successful cleanup write and projection occur in one adapter update.
    Equal(new AttackMarkerProjection(0, 0), AttackMarkerCensus.Project(slots, -1, 0, 0, -1, 0, 0));
}

static void MarkerProjectionOrphan()
{
    Span<AttackMarkerObservation> slots = stackalloc AttackMarkerObservation[2];
    slots[0] = new(17, true, 4, 9);
    slots[1] = new(18, true, 5, 9);
    Equal(new AttackMarkerProjection(2, 1), AttackMarkerCensus.Project(slots, 17, 4, 9, -1, 0, 0));
}

static void ContactPublication()
{
    var fake = new FakeAdapter();
    AttackPublicationState state = AttackPublicationPolicy.Initial();
    True(AttackPublicationPolicy.Publish(ref state, fake, Tuple(), 4, 8));
    Equal(AttackPublicationPhase.Live, state.Phase);
    Equal(464, fake.Operations);
    Equal("probe-marker", fake.Log[0]);
    Equal("clear-0", fake.Log[6]);
    Equal("clear-46", fake.Log[52]);
    Equal("tuple-0", fake.Log[53]);
    Equal("tuple-2", fake.Log[55]);
    Equal("guest", fake.Log[^388]);
    Equal("post-enemy-id", fake.Log[^387]);
    fake.AssertProtected();
}

static void ProjectileWindow()
{
    var fake = Published(out AttackPublicationState state);
    True(AttackPublicationPolicy.Observe(ref state, fake, 5));
    fake.Log.Clear();
    True(AttackPublicationPolicy.RepublishProjectile(ref state, fake, 5, 9));
    Equal(416, fake.Log.Count);
    Equal("probe-marker", fake.Log[0]);
    Equal("deactivate-0", fake.Log[6]);
    Equal("deactivate-2", fake.Log[8]);
    Equal("mutate-scroll-x", fake.Log[15]);
    Equal("probe-marker", fake.Log[407]);
    Equal("live-0", fake.Log[413]);
    Equal("live-2", fake.Log[415]);
    fake.AssertProtected();
}

static void ObservationGeneration()
{
    var early = Published(out AttackPublicationState earlyState);
    False(AttackPublicationPolicy.Observe(ref earlyState, early, 4));
    Equal(AttackPublicationPhase.RolledBack, earlyState.Phase);
    early.AssertSlotCleared();
    var exact = Published(out AttackPublicationState exactState);
    True(AttackPublicationPolicy.Observe(ref exactState, exact, 5));
    Equal(1, exact.Observations);
}

static void InvalidEvents()
{
    var duplicate = Published(out AttackPublicationState state);
    True(AttackPublicationPolicy.Observe(ref state, duplicate, 5));
    False(AttackPublicationPolicy.Observe(ref state, duplicate, 5));
    Equal(AttackPublicationPhase.RolledBack, state.Phase);
    duplicate.AssertSlotCleared();

    var outOfOrder = Published(out AttackPublicationState outOfOrderState);
    False(AttackPublicationPolicy.RepublishProjectile(ref outOfOrderState, outOfOrder, 5, 9));
    Equal(AttackPublicationPhase.RolledBack, outOfOrderState.Phase);
    outOfOrder.AssertSlotCleared();
}

static void InvalidEventCleanupFault()
{
    var fake = Published(out AttackPublicationState state);
    fake.RollbackFailuresRemaining = 2;
    False(AttackPublicationPolicy.Observe(ref state, fake, 4));
    Equal(AttackPublicationPhase.RetryableQuarantine, state.Phase);
    False(state.OwnershipMismatchEvidence);
    fake.RollbackFailuresRemaining = 0;
    True(AttackPublicationPolicy.RetryQuarantine(ref state, fake));
    Equal(AttackPublicationPhase.Empty, state.Phase);
    fake.AssertSlotCleared();

    var republish = Published(out AttackPublicationState republishState);
    republish.RollbackFailuresRemaining = 2;
    False(AttackPublicationPolicy.RepublishProjectile(ref republishState, republish, 5, 9));
    Equal(AttackPublicationPhase.RetryableQuarantine, republishState.Phase);
    republish.RollbackFailuresRemaining = 0;
    True(AttackPublicationPolicy.RetryQuarantine(ref republishState, republish));
    republish.AssertSlotCleared();
}

static void OwnershipMismatch()
{
    var fake = Published(out AttackPublicationState state);
    fake.Native = AttackSlotObservation.Reused;
    int before = fake.Operations;
    False(AttackPublicationPolicy.Observe(ref state, fake, 5));
    Equal(before + 6, fake.Operations);
    False(AttackPublicationPolicy.Cleanup(ref state, fake));
    Equal(before + 6, fake.Operations);
}

static void InitialFaults()
{
    int operationCount = MeasurePublishOperations();
    for (int fail = 1; fail <= operationCount; fail++)
    {
        var fake = new FakeAdapter { FailAt = fail };
        AttackPublicationState state = AttackPublicationPolicy.Initial();
        False(AttackPublicationPolicy.Publish(ref state, fake, Tuple(), 4, 8));
        Equal(AttackPublicationPhase.RolledBack, state.Phase);
        fake.AssertSlotCleared();
        fake.AssertProtected();
    }
}

static void PartialTupleRollback()
{
    // Six probe reads + 47 clear writes place tuple writes at operations 54, 55 and 56.
    for (int operation = 54; operation <= 56; operation++)
    {
        var fake = new FakeAdapter { FailAt = operation };
        AttackPublicationState state = AttackPublicationPolicy.Initial();
        False(AttackPublicationPolicy.Publish(ref state, fake, Tuple(), 0, 0));
        Equal(AttackPublicationPhase.RolledBack, state.Phase);
        Equal(1, state.RollbackAttempts);
        fake.AssertSlotCleared();
    }
}

static void RollbackEvidence()
{
    var retry = new FakeAdapter { FailAtName = "guest", RollbackFailuresRemaining = 1 };
    AttackPublicationState retryState = AttackPublicationPolicy.Initial();
    False(AttackPublicationPolicy.Publish(ref retryState, retry, Tuple(), 0, 0));
    Equal(AttackPublicationPhase.RolledBack, retryState.Phase);
    Equal(2, retryState.RollbackAttempts);
    retry.AssertSlotCleared();

    var terminal = new FakeAdapter { FailAtName = "guest", RollbackFailuresRemaining = 2 };
    AttackPublicationState terminalState = AttackPublicationPolicy.Initial();
    False(AttackPublicationPolicy.Publish(ref terminalState, terminal, Tuple(), 0, 0));
    Equal(AttackPublicationPhase.ResidualStopped, terminalState.Phase);
    True(terminalState.ResidualEvidence);
    Equal(2, terminalState.RollbackAttempts);
    int operations = terminal.Operations;
    False(AttackPublicationPolicy.Cleanup(ref terminalState, terminal));
    Equal(operations, terminal.Operations);
}

static void RollbackNthFaults()
{
    // Guest is operation 77; rollback is six exact reads, three deactivations and 47 clears.
    for (int rollbackOperation = 1; rollbackOperation <= 56; rollbackOperation++)
    {
        var fake = new FakeAdapter
        {
            FailAtName = "guest",
            FailAtSecondary = 77 + rollbackOperation
        };
        AttackPublicationState state = AttackPublicationPolicy.Initial();
        False(AttackPublicationPolicy.Publish(ref state, fake, Tuple(), 0, 0));
        Equal(AttackPublicationPhase.RolledBack, state.Phase);
        Equal(2, state.RollbackAttempts);
        fake.AssertSlotCleared();
        fake.AssertProtected();
    }
}

static void ProjectileFaults()
{
    int count = MeasureProjectileOperations();
    for (int fail = 1; fail <= count; fail++)
    {
        var fake = Published(out AttackPublicationState state);
        True(AttackPublicationPolicy.Observe(ref state, fake, 5));
        fake.FailAt = fake.Operations + fail;
        False(AttackPublicationPolicy.RepublishProjectile(ref state, fake, 5, 9));
        True(state.Phase is AttackPublicationPhase.RetryableQuarantine or AttackPublicationPhase.RolledBack);
        fake.AssertProtected();
    }
}

static void CleanupFaults()
{
    var measured = Published(out AttackPublicationState measuredState);
    AttackPublicationPolicy.Cleanup(ref measuredState, measured);
    int count = measured.OperationsAfterPublish;
    for (int fail = 1; fail <= count; fail++)
    {
        var fake = Published(out AttackPublicationState state);
        fake.FailAt = fake.Operations + fail;
        bool cleaned = AttackPublicationPolicy.Cleanup(ref state, fake);
        if (cleaned) Equal(AttackPublicationPhase.Empty, state.Phase);
        else Equal(AttackPublicationPhase.RetryableQuarantine, state.Phase);
        fake.FailAt = 0;
        if (state.Phase == AttackPublicationPhase.RetryableQuarantine)
            AttackPublicationPolicy.RetryQuarantine(ref state, fake);
        fake.AssertProtected();
    }
}

static void GeneratedSequences()
{
    for (uint seed = 1; seed <= 96; seed++)
    {
        var fake = new FakeAdapter();
        AttackPublicationState state = AttackPublicationPolicy.Initial();
        True(AttackPublicationPolicy.Publish(ref state, fake, Tuple(), 0, 0));
        int windows = (int)(seed % 6);
        for (int window = 0; window < windows && state.Phase == AttackPublicationPhase.Live; window++)
        {
            True(AttackPublicationPolicy.Observe(ref state, fake, window + 1));
            if ((seed & (1U << window)) == 0)
                True(AttackPublicationPolicy.RepublishProjectile(ref state, fake, window + 1, window + 1));
        }
        if (state.Phase is AttackPublicationPhase.Live or AttackPublicationPhase.Observed)
            True(AttackPublicationPolicy.Cleanup(ref state, fake));
        fake.AssertProtected();
    }
}

static void TargetCapturePaths()
{
    TargetReject[] rejects = [TargetReject.Body, TargetReject.HitMismatch, TargetReject.Dead,
        TargetReject.Width, TargetReject.Height, TargetReject.ScreenX, TargetReject.ScreenY,
        TargetReject.OverlapX, TargetReject.OverlapY];
    ExerciseCaptureFaults(new TargetReadFake(0, TargetReject.None), 0);
    foreach (TargetReject reject in rejects) ExerciseCaptureFaults(new TargetReadFake(1, reject), 0);
    ExerciseCaptureFaults(new TargetReadFake(1, TargetReject.None), 1);
    ExerciseCaptureFaults(new TargetReadFake(16, TargetReject.None), 16);
    var overflow = new AttackTargetCaptureState();
    AttackTargetObservationPolicy.Capture(overflow, new TargetReadFake(17, TargetReject.None), TargetInput());
    Equal(16, overflow.Count);
    True(overflow.Overflowed);
    var overflowResult = AttackTargetObservationPolicy.Observe(overflow,
        new TargetReadFake(17, TargetReject.None) { ObserveMode = TargetObserveMode.HpDeath }, 3);
    True(overflowResult.CaptureOverflowed);
    True(!overflowResult.UniqueDefeat);
}

static void ExerciseCaptureFaults(TargetReadFake prototype, int expected)
{
    var measured = prototype.Clone();
    var measuredState = new AttackTargetCaptureState();
    AttackTargetCaptureInput input = TargetInput();
    AttackTargetObservationPolicy.Capture(measuredState, measured, input);
    Equal(expected, measuredState.Count);
    int operations = measured.Operations;
    for (int fail = 1; fail <= operations; fail++)
    {
        var fake = prototype.Clone();
        fake.FailAt = fail;
        var state = new AttackTargetCaptureState();
        RejectFault(() => AttackTargetObservationPolicy.Capture(state, fake, input));
        fake.AssertUnchanged();
    }
}

static void TargetObservationPaths()
{
    foreach (TargetObserveMode mode in Enum.GetValues<TargetObserveMode>())
        ExerciseObservationFaults(1, mode);
    ExerciseObservationFaults(16, TargetObserveMode.DeadFlag);
}

static void ExerciseObservationFaults(int targets, TargetObserveMode mode)
{
    var prototype = new TargetReadFake(targets, TargetReject.None) { ObserveMode = mode };
    var captured = new AttackTargetCaptureState();
    AttackTargetCaptureInput input = TargetInput();
    AttackTargetObservationPolicy.Capture(captured, prototype, input);
    prototype.ResetOperations();
    _ = AttackTargetObservationPolicy.Observe(captured, prototype, 3);
    int operations = prototype.Operations;
    for (int fail = 1; fail <= operations; fail++)
    {
        var fake = new TargetReadFake(targets, TargetReject.None) { ObserveMode = mode };
        var state = new AttackTargetCaptureState();
        AttackTargetObservationPolicy.Capture(state, fake, input);
        fake.ResetOperations();
        fake.FailAt = fail;
        RejectFault(() => _ = AttackTargetObservationPolicy.Observe(state, fake, 3));
        fake.AssertUnchanged();
    }
}

static AttackTargetCaptureInput TargetInput() => new(100 << 16, 100 << 16,
    false, false, 10, 10, 0x20, 3);

static void RejectFault(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Expected injected read fault.");
}

static void IncomingTieScope()
{
    // M4 does not choose outgoing enemy targets. Equal-distance P1/P2 arbitration belongs to the
    // existing incoming contact-damage policy; M6's resolver is intentionally not introduced here.
    True(typeof(AttackPublicationPolicy).Assembly.GetType("CoopFeasibilityMod.AttackPublicationPolicy") != null);
}

static void AllocationFreeWindows()
{
    var adapter = new AllocationFreeAdapter();
    static void Cycle(AllocationFreeAdapter adapter)
    {
        AttackPublicationState state = AttackPublicationPolicy.Initial();
        AttackPublicationTuple tuple = Tuple();
        True(AttackPublicationPolicy.Publish(ref state, adapter, tuple, 0, 0));
        True(AttackPublicationPolicy.Observe(ref state, adapter, 1));
        True(AttackPublicationPolicy.RepublishProjectile(ref state, adapter, 1, 1));
        True(AttackPublicationPolicy.Cleanup(ref state, adapter));
    }
    Cycle(adapter);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 1024; i++) Cycle(adapter);
    Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
}

static void UnloadSuccess()
{
    var fake = Published(out AttackPublicationState state);
    Equal(AttackUnloadResult.Cleaned, AttackPublicationPolicy.Unload(ref state, fake, true));
    Equal(AttackPublicationPhase.Empty, state.Phase);
    fake.AssertSlotCleared();
}

static void UnloadRetry()
{
    var fake = Published(out AttackPublicationState state);
    fake.FailAt = fake.Operations + 1;
    Equal(AttackUnloadResult.Cleaned, AttackPublicationPolicy.Unload(ref state, fake, true));
    Equal(AttackPublicationPhase.Empty, state.Phase);
    fake.AssertSlotCleared();

    var repeated = Published(out AttackPublicationState repeatedState);
    repeated.FailAllProbes = true;
    Equal(AttackUnloadResult.ResidualFault,
        AttackPublicationPolicy.Unload(ref repeatedState, repeated, true));
    True(repeatedState.ResidualEvidence);

    var clearFault = Published(out AttackPublicationState clearFaultState);
    clearFault.RollbackFailuresRemaining = 10;
    Equal(AttackUnloadResult.ResidualFault,
        AttackPublicationPolicy.Unload(ref clearFaultState, clearFault, true));
    True(clearFaultState.ResidualEvidence);
}

static void UnloadMemoryUnavailable()
{
    _ = Published(out AttackPublicationState state);
    Equal(AttackUnloadResult.ResidualMemoryUnavailable,
        AttackPublicationPolicy.Unload(ref state, null, false));
    Equal(AttackPublicationPhase.ResidualStopped, state.Phase);
    True(state.ResidualEvidence);
}

static void UnloadMismatch()
{
    var fake = Published(out AttackPublicationState state);
    fake.Native = AttackSlotObservation.Reused;
    int before = fake.ClearWrites;
    Equal(AttackUnloadResult.OwnershipMismatch,
        AttackPublicationPolicy.Unload(ref state, fake, true));
    Equal(before, fake.ClearWrites);
    Equal(AttackPublicationPhase.MutationStopped, state.Phase);
}

static void ResetPreflightSuccess()
{
    var exact = ResetHarness.Published();
    True(exact.TryAutomation("session", 7, true));
    Equal(8, exact.Generation);
    Equal(1, exact.ResetApplications);
    Equal(AttackPublicationPhase.Empty, exact.Publication.Phase);
    exact.Adapter.AssertSlotCleared();

    var free = ResetHarness.Published();
    free.Adapter.SetExternallyFree();
    int freeClearWrites = free.Adapter.ClearWrites;
    True(free.TryAutomation("session", 7, true));
    Equal(8, free.Generation);
    Equal(freeClearWrites, free.Adapter.ClearWrites);
}

static void ResetPreflightMemoryUnavailable()
{
    var harness = ResetHarness.Published();
    int operations = harness.Adapter.Operations;
    False(harness.TryAutomation("wrong", 7, true));
    False(harness.TryAutomation("session", 6, true));
    Equal(operations, harness.Adapter.Operations);
    False(harness.TryAutomation("session", 7, false));
    Equal("session", harness.Session);
    Equal(7, harness.Generation);
    Equal(91, harness.DiagnosticProjection);
    Equal(0, harness.ResetApplications);
    Equal(operations, harness.Adapter.Operations);
    Equal(AttackPublicationPhase.Live, harness.Publication.Phase);
}

static void ResetPreflightRetry()
{
    var harness = ResetHarness.Published();
    harness.Adapter.RollbackFailuresRemaining = 3;
    False(harness.TryAutomation("session", 7, true));
    Equal(7, harness.Generation);
    Equal(91, harness.DiagnosticProjection);
    Equal(AttackPublicationPhase.RetryableQuarantine, harness.Publication.Phase);
    True(harness.CleanupAuthorityRetained);
    harness.Adapter.RollbackFailuresRemaining = 0;
    True(harness.TryAutomation("session", 7, true));
    Equal(8, harness.Generation);
    harness.Adapter.AssertSlotCleared();
}

static void ResetPreflightReuse()
{
    var harness = ResetHarness.Published();
    harness.Adapter.Native = AttackSlotObservation.Reused;
    int clearWrites = harness.Adapter.ClearWrites;
    True(harness.TryAutomation("session", 7, true));
    Equal(AttackPublicationPhase.MutationStopped, harness.Publication.Phase);
    Equal(clearWrites, harness.Adapter.ClearWrites);
    int operations = harness.Adapter.Operations;
    True(harness.TryAutomation("session", 8, true));
    Equal(operations, harness.Adapter.Operations);
    Equal(clearWrites, harness.Adapter.ClearWrites);
}

static void SharedResetExhaustion()
{
    var owned = SharedResetHarness.Owned();
    owned.Lease.Revision = ulong.MaxValue;
    owned.AssertRefusedWithoutNative();

    var terminal = SharedResetHarness.Reused();
    terminal.Lease.Revision = ulong.MaxValue;
    terminal.AssertRefusedWithoutNative();

    var publication = SharedResetHarness.Owned();
    publication.Publication.Revision = ulong.MaxValue;
    publication.AssertRefusedWithoutNative();

    var session = SharedResetHarness.Owned();
    SetPrivateRevision(session.Session, ulong.MaxValue);
    session.AssertRefusedWithoutNative();

    var jump = SharedResetHarness.Owned();
    SetPrivateUlong(jump.Jump, "_revision", ulong.MaxValue);
    jump.AssertRefusedWithoutNative();

    var stance = SharedResetHarness.Owned();
    SetPrivateUlong(stance.Stance, "_revision", ulong.MaxValue);
    stance.AssertRefusedWithoutNative();

    var locomotion = SharedResetHarness.Owned();
    SetPrivateUlong(locomotion.Locomotion, "_initializationRevision", ulong.MaxValue);
    locomotion.AssertRefusedWithoutNative();

    var reconstruction = SharedResetHarness.Owned();
    SetPrivateUlong(reconstruction.Reconstruction, "_revision", ulong.MaxValue);
    reconstruction.AssertRefusedWithoutNative();

    var combined = SharedResetHarness.Owned();
    combined.Publication.Revision = ulong.MaxValue;
    SetPrivateRevision(combined.Session, ulong.MaxValue);
    SetPrivateUlong(combined.Jump, "_revision", ulong.MaxValue);
    SetPrivateUlong(combined.Stance, "_revision", ulong.MaxValue);
    SetPrivateUlong(combined.Locomotion, "_initializationRevision", ulong.MaxValue);
    SetPrivateUlong(combined.Reconstruction, "_revision", ulong.MaxValue);
    combined.AssertRefusedWithoutNative();

    var diagnostic = SharedResetHarness.Owned();
    diagnostic.Generation = int.MaxValue;
    diagnostic.AssertRefusedWithoutNative();

    var negative = SharedResetHarness.Owned();
    negative.Generation = -1;
    negative.AssertRefusedWithoutNative();
}

static void SharedResetSuccess()
{
    var exact = SharedResetHarness.Owned();
    True(exact.TryReset(true));
    Equal(8, exact.Generation);
    Equal(AttackLeasePhase.Empty, exact.Lease.Phase);
    Equal(AttackPublicationPhase.Empty, exact.Publication.Phase);
    exact.Adapter.AssertSlotCleared();

    var free = SharedResetHarness.Owned();
    free.Adapter.SetExternallyFree();
    int writes = free.Adapter.ClearWrites;
    True(free.TryReset(true));
    Equal(writes, free.Adapter.ClearWrites);
    Equal(AttackLeasePhase.Empty, free.Lease.Phase);
}

static void SharedResetReuse()
{
    var reused = SharedResetHarness.Owned();
    reused.Adapter.Native = AttackSlotObservation.Reused;
    int writes = reused.Adapter.ClearWrites;
    True(reused.TryReset(true));
    Equal(AttackLeasePhase.MutationStopped, reused.Lease.Phase);
    Equal(AttackPublicationPhase.MutationStopped, reused.Publication.Phase);
    Equal(writes, reused.Adapter.ClearWrites);
    int operations = reused.Adapter.Operations;
    True(reused.TryReset(true));
    Equal(operations, reused.Adapter.Operations);
}

static void SharedResetPublicationAba()
{
    var harness = SharedResetHarness.Owned();
    AttackPublicationResetCommand stale = AttackResetPreflight.Prepare(harness.Publication);
    True(AttackPublicationPolicy.Observe(ref harness.Publication, harness.Adapter, 5));
    True(AttackPublicationPolicy.RepublishProjectile(ref harness.Publication, harness.Adapter, 5, 9));
    Equal(AttackPublicationPhase.Live, harness.Publication.Phase);
    int operations = harness.Adapter.Operations;
    Equal(AttackResetPreflightOutcome.RefusedResidualOwnership,
        AttackResetPreflight.RunPrepared(ref harness.Publication, harness.Adapter, true, stale));
    Equal(operations, harness.Adapter.Operations);
}

static void SetPrivateRevision(ManagedMovementSessionReducer session, ulong revision)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic;
    typeof(ManagedMovementSessionReducer).GetField("_revision", flags)!.SetValue(session, revision);
}

static void SetPrivateUlong(object reducer, string field, ulong value)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic;
    reducer.GetType().GetField(field, flags)!.SetValue(reducer, value);
}

static int MeasurePublishOperations()
{
    var fake = new FakeAdapter();
    AttackPublicationState state = AttackPublicationPolicy.Initial();
    AttackPublicationPolicy.Publish(ref state, fake, Tuple(), 4, 8);
    return fake.Operations;
}

static int MeasureProjectileOperations()
{
    var fake = Published(out AttackPublicationState state);
    AttackPublicationPolicy.Observe(ref state, fake, 5);
    int before = fake.Operations;
    AttackPublicationPolicy.RepublishProjectile(ref state, fake, 5, 9);
    return fake.Operations - before;
}

static FakeAdapter Published(out AttackPublicationState state)
{
    var fake = new FakeAdapter();
    state = AttackPublicationPolicy.Initial();
    True(AttackPublicationPolicy.Publish(ref state, fake, Tuple(), 4, 8));
    fake.PublishOperations = fake.Operations;
    return fake;
}

static AttackPublicationTuple Tuple() => new(17, 1, 0xABCD);
static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
static void False(bool value) { if (value) throw new InvalidOperationException("Expected false."); }
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

sealed class FakeAdapter : IAttackPublicationAdapter
{
    private const int SlotStart = 64;
    private const int SlotLength = 0xBC;
    private const int TargetHpAddress = 700;
    private const int TargetDeadAddress = 704;
    private const int TargetRewardAddress = 708;
    private readonly byte[] _memory = Enumerable.Repeat((byte)0xA5, 1024).ToArray();
    private readonly byte[] _outside;
    public readonly List<string> Log = [];
    public AttackSlotObservation Native = AttackSlotObservation.Free;
    public int Operations;
    public int PublishOperations;
    public int FailAt;
    public int FailAtSecondary;
    public int Observations;
    public int ClearWrites;
    public int RollbackFailuresRemaining;
    public string? FailAtName;
    public bool FailAllProbes;
    public int OperationsAfterPublish => Operations - PublishOperations;

    public FakeAdapter()
    {
        Array.Clear(_memory, SlotStart, SlotLength);
        _outside = (byte[])_memory.Clone();
    }

    public AttackSlotObservation Probe(in AttackPublicationTuple tuple)
    {
        if (FailAllProbes) throw new InvalidOperationException("injected repeated probe fault");
        Step("probe-marker"); Step("probe-generation"); Step("probe-room");
        Step("probe-id"); Step("probe-update"); Step("probe-hit-state");
        return Native;
    }

    public void ClearReservedSlot(in AttackPublicationTuple tuple)
    {
        for (int word = 0; word < 47; word++) { Step($"clear-{word}"); Write32(word * 4, 0); ClearWrites++; }
    }
    public void WriteOwnershipField(in AttackPublicationTuple tuple, int field)
    {
        Step($"tuple-{field}");
        if (field == 0) Write32(0x7C, 0x50324B43);
        else if (field == 1) Write32(0x80, tuple.Generation);
        else if (field == 2) { Write32(0x84, tuple.RoomHash); Native = AttackSlotObservation.Exact; }
    }
    public void WritePayload(in AttackPublicationTuple tuple)
    {
        Step("payload-scroll-x"); Step("payload-scroll-y");
        int[] offsets = [0, 4, 0x10, 0x12, 0x14, 0x2C, 0x32, 0x34, 0x40, 0x42, 0x46, 0x47, 0x49, 0x58, 0x6A];
        for (int index = 0; index < offsets.Length; index++) { Step($"payload-{index}"); _memory[SlotStart + offsets[index]] = (byte)(index + 1); }
    }
    public void WriteLiveField(in AttackPublicationTuple tuple, int field)
    {
        Step($"live-{field}");
        if (field == 0) Write16(0x26, 0x3E);
        else if (field == 1) Write32(0x28, 0x8011A4C8);
        else Write16(0x3C, 0x20);
    }
    public void CallGuest(in AttackPublicationTuple tuple) { Step("guest"); _memory[SlotStart + 0x3A] = 3; }
    public void ReadPostCall(in AttackPublicationTuple tuple)
    {
        Step("post-enemy-id"); Step("capture-scroll-x"); Step("capture-scroll-y");
        for (int target = 0; target < 128; target++)
        { Step($"target-{target}-update"); Step($"target-{target}-id"); Step($"target-{target}-hit-state"); }
    }
    public void ObserveNative(in AttackPublicationTuple tuple) { Step("observe-hit"); Observations++; _ = _memory[SlotStart + 0x48]; }
    public void DeactivateLiveField(in AttackPublicationTuple tuple, int field)
    {
        Step($"deactivate-{field}");
        if (field == 0) Write32(0x28, 0); else if (field == 1) Write16(0x3C, 0); else Write16(0x26, 0);
    }
    public void MutateProjectile(in AttackPublicationTuple tuple)
    {
        Step("mutate-scroll-x"); Step("mutate-scroll-y");
        Step("mutate-x"); Write32(0, 0x99); Step("mutate-y"); Write32(4, 0x88);
        Step("mutate-hit"); _memory[SlotStart + 0x48] = 0; Step("mutate-state"); Write16(0x44, 0);
        Step("capture-scroll-x"); Step("capture-scroll-y");
        for (int target = 0; target < 128; target++)
        { Step($"target-{target}-update"); Step($"target-{target}-id"); Step($"target-{target}-hit-state"); }
    }
    public void ClearOwnedSlot(in AttackPublicationTuple tuple)
    {
        if (RollbackFailuresRemaining > 0)
        {
            RollbackFailuresRemaining--;
            Step("rollback-fault");
            throw new InvalidOperationException("injected rollback fault");
        }
        for (int word = 0; word < 47; word++) { Step($"rollback-{word}"); Write32(word * 4, 0); ClearWrites++; }
        Native = AttackSlotObservation.Free;
    }

    public void AssertProtected()
    {
        for (int i = 0; i < _memory.Length; i++)
            if ((i < SlotStart || i >= SlotStart + SlotLength) && _memory[i] != _outside[i])
                throw new InvalidOperationException($"Byte {i} outside reserved slot changed.");
        // The fake target HP/dead/reward region is deliberately outside the reserved slot.
        for (int i = 700; i < 760; i++)
            if (_memory[i] != 0xA5) throw new InvalidOperationException("Target HP/dead/reward memory changed.");
        EqualProtected(TargetHpAddress, 2, "HP");
        EqualProtected(TargetDeadAddress, 4, "dead flags");
        EqualProtected(TargetRewardAddress, 8, "reward");
    }

    private void EqualProtected(int address, int length, string name)
    {
        for (int index = 0; index < length; index++)
            if (_memory[address + index] != 0xA5)
                throw new InvalidOperationException($"Target {name} address changed.");
    }

    public void AssertSlotCleared()
    {
        for (int i = SlotStart; i < SlotStart + SlotLength; i++)
            if (_memory[i] != 0) throw new InvalidOperationException($"Reserved slot byte {i - SlotStart:X} was not rolled back.");
    }

    public void SetExternallyFree()
    {
        Write16(0x26, 0); Write32(0x28, 0); Write16(0x3C, 0);
        Native = AttackSlotObservation.Free;
    }

    private void Write32(int offset, uint value)
    {
        for (int i = 0; i < 4; i++) _memory[SlotStart + offset + i] = (byte)(value >> (i * 8));
    }
    private void Write16(int offset, ushort value)
    {
        _memory[SlotStart + offset] = (byte)value;
        _memory[SlotStart + offset + 1] = (byte)(value >> 8);
    }
    private void Step(string operation)
    {
        Operations++;
        Log.Add(operation);
        if (FailAtName == operation) { FailAtName = null; throw new InvalidOperationException("injected named fault"); }
        if (FailAt == Operations || FailAtSecondary == Operations)
            throw new InvalidOperationException("injected operation fault");
    }
}

sealed class ResetHarness
{
    public string Session = "session";
    public int Generation = 7;
    public int DiagnosticProjection = 91;
    public int ResetApplications;
    public bool CleanupAuthorityRetained;
    public AttackPublicationState Publication;
    public readonly FakeAdapter Adapter = new();

    public static ResetHarness Published()
    {
        var harness = new ResetHarness { Publication = AttackPublicationPolicy.Initial() };
        var tuple = new AttackPublicationTuple(17, 1, 0xABCD);
        if (!AttackPublicationPolicy.Publish(ref harness.Publication, harness.Adapter, tuple, 4, 8))
            throw new InvalidOperationException("Harness publication failed.");
        return harness;
    }

    public bool TryAutomation(string session, int expectedGeneration, bool memoryAvailable)
    {
        if (session != Session || expectedGeneration != Generation) return false;
        AttackResetPreflightOutcome outcome = AttackResetPreflight.Run(ref Publication,
            memoryAvailable ? Adapter : null, memoryAvailable);
        if (!AttackResetPreflight.AllowsReset(outcome))
        {
            if (outcome == AttackResetPreflightOutcome.RefusedCleanupFault &&
                Publication.Phase == AttackPublicationPhase.RetryableQuarantine)
                CleanupAuthorityRetained = true;
            return false;
        }
        Generation++;
        DiagnosticProjection = 0;
        ResetApplications++;
        return true;
    }
}

sealed class SharedResetHarness
{
    public int Generation = 7;
    public readonly ManagedMovementSessionReducer Session;
    public readonly JumpForgivenessReducer Jump = new();
    public readonly ManagedStanceReducer Stance = new();
    public readonly ManagedLocomotionReducer Locomotion = new();
    public readonly ReconstructionPolicyReducer Reconstruction = new();
    public AttackLeaseState Lease;
    public AttackPublicationState Publication;
    public readonly FakeAdapter Adapter = new();

    private SharedResetHarness()
    {
        Session = new ManagedMovementSessionReducer(new RoomEpochTracker());
        Session.Load();
        Lease = AttackLeaseMachine.Initial();
        Publication = AttackPublicationPolicy.Initial();
    }

    public static SharedResetHarness Owned()
    {
        var harness = new SharedResetHarness();
        harness.Lease = AttackLeaseMachine.Reserve(harness.Lease, 17, 0xABCD);
        var tuple = new AttackPublicationTuple(17, harness.Lease.OwnedGeneration, 0xABCD);
        if (!AttackPublicationPolicy.Publish(ref harness.Publication, harness.Adapter, tuple, 4, 8))
            throw new InvalidOperationException("Shared reset publication failed.");
        return harness;
    }

    public static SharedResetHarness Reused()
    {
        SharedResetHarness harness = Owned();
        harness.Adapter.Native = AttackSlotObservation.Reused;
        _ = AttackPublicationPolicy.Observe(ref harness.Publication, harness.Adapter, 5);
        AttackLeaseCommand probe = AttackLeaseMachine.RequestOwnedCleanup(harness.Lease);
        harness.Lease = AttackLeaseMachine.ProbeReused(harness.Lease, probe);
        return harness;
    }

    public bool TryReset(bool memoryAvailable)
    {
        if (!DiagnosticResetPreparationPolicy.TryPrepare(Generation, Session, Lease,
            Publication, Jump, Stance, Locomotion, Reconstruction,
            out DiagnosticResetPreparation preparation)) return false;
        AttackResetPreflightOutcome outcome = AttackResetPreflight.RunPrepared(ref Publication,
            memoryAvailable ? Adapter : null, memoryAvailable, preparation.Publication);
        if (!AttackResetPreflight.AllowsReset(outcome)) return false;
        if (!DiagnosticResetPreparationPolicy.CommitPreparedReducers(preparation, outcome,
            ref Lease, Session, Jump, Stance, Locomotion, Reconstruction)) return false;
        Generation = preparation.NextDiagnosticGeneration;
        return true;
    }

    public void AssertRefusedWithoutNative()
    {
        int operations = Adapter.Operations;
        int generation = Generation;
        byte[] session = StateBytes.Of(Session.State);
        byte[] lease = StateBytes.Of(Lease);
        byte[] publication = StateBytes.Of(Publication);
        byte[] jump = StateBytes.Of(Jump.State);
        byte[] stance = StateBytes.Of(Stance.State);
        byte[] locomotion = StateBytes.Of(Locomotion.State);
        byte[] reconstruction = StateBytes.Of(Reconstruction.State);
        if (TryReset(true) || operations != Adapter.Operations || generation != Generation ||
            !session.SequenceEqual(StateBytes.Of(Session.State)) ||
            !lease.SequenceEqual(StateBytes.Of(Lease)) ||
            !publication.SequenceEqual(StateBytes.Of(Publication)) ||
            !jump.SequenceEqual(StateBytes.Of(Jump.State)) ||
            !stance.SequenceEqual(StateBytes.Of(Stance.State)) ||
            !locomotion.SequenceEqual(StateBytes.Of(Locomotion.State)) ||
            !reconstruction.SequenceEqual(StateBytes.Of(Reconstruction.State)))
            throw new InvalidOperationException("Reset refusal mutated native or managed state.");
    }
}

sealed class AllocationFreeAdapter : IAttackPublicationAdapter, IAttackTargetReadAdapter
{
    private const int Length = 0xBC;
    private readonly byte[] _slot = new byte[Length];
    private readonly AttackTargetCaptureState _targets = new();
    public AttackSlotObservation Probe(in AttackPublicationTuple tuple)
    {
        uint marker = Read32(0x7C), generation = Read32(0x80), room = Read32(0x84);
        ushort id = Read16(0x26), hit = Read16(0x3C);
        uint update = Read32(0x28);
        if (marker == 0x50324B43 && generation == tuple.Generation && room == tuple.RoomHash)
            return AttackSlotObservation.Exact;
        return id == 0 && update == 0 && hit == 0 ? AttackSlotObservation.Free : AttackSlotObservation.Reused;
    }
    public void ClearReservedSlot(in AttackPublicationTuple tuple)
    {
        for (int word = 0; word < 47; word++) Write32(word * 4, 0);
    }
    public void WriteOwnershipField(in AttackPublicationTuple tuple, int field)
    {
        Write32(0x7C + field * 4, field == 0 ? 0x50324B43 : field == 1 ? tuple.Generation : tuple.RoomHash);
    }
    public void WritePayload(in AttackPublicationTuple tuple)
    {
        Write32(0, 1); Write32(4, 2); Write16(0x10, 3); Write16(0x12, 4);
        Write16(0x14, 5); Write16(0x2C, 6); Write16(0x32, 7); Write32(0x34, 8);
        Write16(0x40, 9); Write16(0x42, 10); _slot[0x46] = 11; _slot[0x47] = 12;
        _slot[0x49] = 13; Write16(0x58, 14); Write16(0x6A, 15);
    }
    public void WriteLiveField(in AttackPublicationTuple tuple, int field)
    {
        if (field == 0) Write16(0x26, 0x3E); else if (field == 1) Write32(0x28, 0x8011A4C8); else Write16(0x3C, 0x20);
    }
    public void CallGuest(in AttackPublicationTuple tuple) => Write16(0x3A, 3);
    public void ReadPostCall(in AttackPublicationTuple tuple)
    {
        _ = Read16(0x3A);
        _targets.Clear();
        var input = new AttackTargetCaptureInput(100 << 16, 100 << 16, false, false, 10, 10, 0x20, 3);
        AttackTargetObservationPolicy.Capture(_targets, this, input);
    }
    public void ObserveNative(in AttackPublicationTuple tuple) =>
        _ = AttackTargetObservationPolicy.Observe(_targets, this, 3);
    public void DeactivateLiveField(in AttackPublicationTuple tuple, int field)
    {
        if (field == 0) Write32(0x28, 0); else if (field == 1) Write16(0x3C, 0); else Write16(0x26, 0);
    }
    public void MutateProjectile(in AttackPublicationTuple tuple)
    {
        _ = Read32(0); _ = Read32(4); Write32(0, 4); Write32(4, 5); _slot[0x48] = 0; Write16(0x44, 0);
        var input = new AttackTargetCaptureInput(100 << 16, 100 << 16, false, false, 10, 10, 0x20, 3);
        AttackTargetObservationPolicy.Capture(_targets, this, input);
    }
    public void ClearOwnedSlot(in AttackPublicationTuple tuple)
    {
        for (int word = 0; word < 47; word++) Write32(word * 4, 0);
    }
    private ushort Read16(int offset) => (ushort)(_slot[offset] | _slot[offset + 1] << 8);
    private uint Read32(int offset) => (uint)(_slot[offset] | _slot[offset + 1] << 8 |
        _slot[offset + 2] << 16 | _slot[offset + 3] << 24);
    private void Write16(int offset, ushort value)
    {
        _slot[offset] = (byte)value; _slot[offset + 1] = (byte)(value >> 8);
    }
    private void Write32(int offset, uint value)
    {
        _slot[offset] = (byte)value; _slot[offset + 1] = (byte)(value >> 8);
        _slot[offset + 2] = (byte)(value >> 16); _slot[offset + 3] = (byte)(value >> 24);
    }
    public int ReadScrollX() => 0;
    public int ReadScrollY() => 0;
    public uint TargetAddress(int slot) => (uint)slot;
    public byte ReadAttackU8(uint offset) => _slot[(int)offset];
    public byte ReadTargetU8(uint address, uint offset) => 0;
    public ushort ReadTargetU16(uint address, uint offset) => 0;
    public uint ReadTargetU32(uint address, uint offset) => 0;
}

enum TargetReject { None, Body, HitMismatch, Dead, Width, Height, ScreenX, ScreenY, OverlapX, OverlapY }
enum TargetObserveMode { NoHit, IdentityMismatch, NoCooldown, HpDeath, DeadFlag }

sealed class TargetReadFake : IAttackTargetReadAdapter
{
    private readonly int _targets;
    private readonly TargetReject _reject;
    private readonly byte[] _slot = Enumerable.Repeat((byte)0xA5, 0xBC).ToArray();
    private readonly byte[] _protected = Enumerable.Repeat((byte)0x5A, 64).ToArray();
    public TargetObserveMode ObserveMode;
    public int Operations;
    public int FailAt;
    private bool _observing;

    public TargetReadFake(int targets, TargetReject reject) { _targets = targets; _reject = reject; }
    public TargetReadFake Clone() => new(_targets, _reject) { ObserveMode = ObserveMode };
    public void ResetOperations() { Operations = 0; FailAt = 0; _observing = false; }
    public int ReadScrollX() { Step(); return 0; }
    public int ReadScrollY() { Step(); return 0; }
    public uint TargetAddress(int slot) => (uint)slot;
    public byte ReadAttackU8(uint offset) { Step(); _observing = true; return ObserveMode == TargetObserveMode.NoHit ? (byte)0 : (byte)1; }

    public byte ReadTargetU8(uint address, uint offset)
    {
        Step(); bool configured = IsConfigured(address);
        if (offset == 0x46) return configured && _reject != TargetReject.Width ? (byte)5 : (byte)0;
        if (offset == 0x47) return configured && _reject != TargetReject.Height ? (byte)5 : (byte)0;
        if (offset >= 0x6D) return !_observing || ObserveMode == TargetObserveMode.NoCooldown ? (byte)0 : (byte)1;
        return 0;
    }

    public ushort ReadTargetU16(uint address, uint offset)
    {
        Step(); bool configured = IsConfigured(address);
        if (offset == 0x26) return (ushort)(configured ? 1 : 0);
        if (offset == 0x3C) return configured
            ? _reject == TargetReject.Body ? (ushort)0 : _reject == TargetReject.HitMismatch ? (ushort)0x10 : (ushort)0x20
            : (ushort)0;
        if (offset == 0x02) return unchecked((ushort)(_reject == TargetReject.ScreenX ? -40 : _reject == TargetReject.OverlapX ? 200 : 114));
        if (offset == 0x06) return unchecked((ushort)(_reject == TargetReject.ScreenY ? -40 : _reject == TargetReject.OverlapY ? 200 : 92));
        if (offset is 0x10 or 0x12 or 0x14) return 0;
        if (offset == 0x3A)
        {
            if (_observing && ObserveMode == TargetObserveMode.IdentityMismatch) return 9;
            return 2;
        }
        if (offset == 0x3E)
            return _observing && ObserveMode == TargetObserveMode.HpDeath ? (ushort)0 : (ushort)10;
        return 0;
    }

    public uint ReadTargetU32(uint address, uint offset)
    {
        Step(); bool configured = IsConfigured(address);
        if (offset == 0x28) return configured ? address + 0x1000 : 0;
        if (offset == 0x34)
            return _reject == TargetReject.Dead || _observing && ObserveMode == TargetObserveMode.DeadFlag ? 0x100U : 0;
        return 0;
    }

    public void AssertUnchanged()
    {
        if (_slot.Any(value => value != 0xA5) || _protected.Any(value => value != 0x5A))
            throw new InvalidOperationException("Target reads changed slot or protected memory.");
    }

    private bool IsConfigured(uint address) => address >= 64 && address < 64 + _targets;
    private void Step()
    {
        Operations++;
        if (FailAt == Operations) throw new InvalidOperationException("injected target read fault");
    }
}

static class StateBytes
{
    public static byte[] Of<T>(T value) where T : unmanaged
    {
        ReadOnlySpan<T> values = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref value, 1);
        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(values).ToArray();
    }
}
