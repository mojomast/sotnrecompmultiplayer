using CoopFeasibilityMod;

var tests = new List<(string, Action)>
{
    ("exact warning resistance and hard boundaries", () =>
    {
        var policy = new TetherSuspensionReducer();
        Equal(TetherPhase.Active, Step(policy, 159, 111).Phase);
        Equal(TetherPhase.Warning, Step(policy, 160, 0).Phase);
        Equal(TetherPhase.Warning, Step(policy, 0, 159).Phase);
        Equal(TetherPhase.Resistance, Step(policy, 224, 0).Phase);
        Equal(TetherPhase.Resistance, Step(policy, 256, 192).Phase);
        TetherCommand hard = Step(policy, 257, 0);
        Equal(TetherPhase.Reconstructing, hard.Phase);
        True(hard.BeginReconstruction);
        Equal(1UL, policy.Diagnostics.HardRecoveries);
    }),
    ("resistance blocks only outward intent", () =>
    {
        var policy = new TetherSuspensionReducer();
        Equal(TetherMovementCommand.BlockOutward, Step(policy, 224, 0, TetherMovementIntent.Outward).Movement);
        Equal(TetherMovementCommand.Allow, Step(policy, 224, 0, TetherMovementIntent.Inward).Movement);
        Equal(TetherMovementCommand.Allow, Step(policy, 224, 0, TetherMovementIntent.None).Movement);
    }),
    ("transition reconstruction and suspension priorities", () =>
    {
        var policy = new TetherSuspensionReducer();
        Equal(TetherPhase.Reconstructing, Step(policy, 400, 0, lifecycle: TetherLifecycle.Transition).Phase);
        Equal(TetherReason.Transition, policy.Diagnostics.Reason);
        Equal(TetherPhase.Suspended, Step(policy, 400, 0, lifecycle: TetherLifecycle.Active,
            reason: TetherReason.UnsupportedTerrain).Phase);
        Equal(TetherPhase.Suspended, Step(policy, 0, 0, lifecycle: TetherLifecycle.Fatal).Phase);
    }),
    ("hard recovery latch prevents churn", () =>
    {
        var policy = new TetherSuspensionReducer();
        True(Step(policy, 300, 0).BeginReconstruction);
        False(Step(policy, 300, 0).BeginReconstruction);
        False(Step(policy, 0, 0, lifecycle: TetherLifecycle.Reconstructing).BeginReconstruction);
        False(Step(policy, 0, 0).BeginReconstruction);
        True(Step(policy, 300, 0).BeginReconstruction);
        Equal(2UL, policy.Diagnostics.HardRecoveries);
    }),
    ("entries totals maxima and diagnostic reset", () =>
    {
        var policy = new TetherSuspensionReducer();
        Step(policy, 160, 0); Step(policy, 161, 0); Step(policy, 0, 0); Step(policy, 160, 0);
        Equal(2UL, policy.Diagnostics.WarningEntries);
        Equal(3UL, policy.Diagnostics.WarningFrames);
        Equal(2UL, policy.Diagnostics.WarningMaxConsecutive);
        policy.RecordStatus(true); policy.RecordStatus(false);
        Equal(2UL, policy.Diagnostics.StatusEligible);
        Equal(1UL, policy.Diagnostics.StatusSubmitted);
        policy.ResetDiagnostics();
        Equal(0UL, policy.Diagnostics.WarningEntries);
        Equal(TetherPhase.Suspended, policy.Diagnostics.Phase);
    }),
    ("counter exhaustion is nonwrapping and atomic", () =>
    {
        var policy = new TetherSuspensionReducer();
        Set(policy, "_warningFrames", ulong.MaxValue);
        TetherDiagnostics before = policy.Diagnostics;
        Reject(() => Step(policy, 160, 0));
        Equal(before, policy.Diagnostics);
    }),
    ("status eligibility ignores proxy safety but excludes non-stage display", () =>
    {
        True(CoopStatusRenderPolicy.Eligible(true, true, true, false, false, false, true));
        False(CoopStatusRenderPolicy.AvatarEligible(true, false, true, true, false));
        False(CoopStatusRenderPolicy.AvatarEligible(true, true, true, true, true));
        True(CoopStatusRenderPolicy.AvatarEligible(true, true, true, true, false));
        False(CoopStatusRenderPolicy.Eligible(true, true, true, true, false, false, true));
        False(CoopStatusRenderPolicy.Eligible(true, true, true, false, false, false, false));
    }),
    ("fatal rendering circuit breaker prevents every later direct retry", () =>
    {
        int directCalls = 0;
        bool fatal = false;
        for (int frame = 0; frame < 20; frame++)
        {
            if (!CoopStatusRenderPolicy.DirectCallsAllowed(fatal)) continue;
            directCalls++;
            fatal = true;
        }
        Equal(1, directCalls);
        True(CoopStatusRenderPolicy.Eligible(true, true, true, false, false, false, true));
    }),
    ("deterministic generated sequence", () =>
    {
        for (uint seed = 1; seed <= 64; seed++) Equal(Replay(seed), Replay(seed));
    }),
    ("steady state allocates zero", () =>
    {
        var policy = new TetherSuspensionReducer();
        for (int i = 0; i < 100; i++) Step(policy, i % 240, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) Step(policy, i % 240, 0);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }),
    ("retry initial fault waits 29 and retries on update 30", () =>
    {
        ReconstructionRetryState state = ReconstructionRetryPolicy.RetryableFault(
            ReconstructionRetryPolicy.Initial, TetherReason.UnsupportedTerrain);
        for (int update = 1; update <= 29; update++)
        {
            ReconstructionRetryTransition transition = ReconstructionRetryPolicy.SafeUpdate(state);
            state = transition.State;
            Equal(ReconstructionRetryCommand.Suppress, transition.Command);
            Equal(30 - update, state.CooldownRemaining);
        }
        ReconstructionRetryTransition retry = ReconstructionRetryPolicy.SafeUpdate(state);
        Equal(ReconstructionRetryCommand.Retry, retry.Command);
        Equal(0, retry.State.CooldownRemaining);
        Equal(1UL, retry.State.Retries);
        Equal(29UL, retry.State.SuppressedAttempts);
    }),
    ("retry failure rearms and success or lifecycle clears", () =>
    {
        ReconstructionRetryState state = ReconstructionRetryPolicy.RetryableFault(
            ReconstructionRetryPolicy.Initial, TetherReason.Reconstruction);
        for (int i = 0; i < 30; i++) state = ReconstructionRetryPolicy.SafeUpdate(state).State;
        state = ReconstructionRetryPolicy.RetryableFault(state, TetherReason.UnsupportedTerrain);
        Equal(30, state.CooldownRemaining);
        Equal(1UL, state.Retries);
        state = ReconstructionRetryPolicy.ClearCurrent(state);
        False(state.Active);
        Equal(1UL, state.Retries);
        Equal(ReconstructionRetryPolicy.Initial, ReconstructionRetryPolicy.ResetDiagnostics());
    }),
    ("collision retry is terminal", () =>
    {
        ReconstructionRetryState state = ReconstructionRetryPolicy.TerminalFault(
            ReconstructionRetryPolicy.Initial, TetherReason.Collision);
        for (int i = 0; i < 100; i++)
        {
            ReconstructionRetryTransition transition = ReconstructionRetryPolicy.SafeUpdate(state);
            state = transition.State;
            Equal(ReconstructionRetryCommand.Suppress, transition.Command);
        }
        True(state.Terminal);
        Equal(0UL, state.Retries);
    }),
    ("retry adapter suppresses probes and status remains eligible", () =>
    {
        ReconstructionRetryState state = ReconstructionRetryPolicy.RetryableFault(
            ReconstructionRetryPolicy.Initial, TetherReason.UnsupportedTerrain);
        var tether = new TetherSuspensionReducer();
        Step(tether, 0, 0, lifecycle: TetherLifecycle.Reconstructing,
            reason: TetherReason.None);
        int reconstructionCalls = 0;
        for (int i = 0; i < 29; i++)
        {
            ReconstructionRetryTransition transition = ReconstructionRetryPolicy.SafeUpdate(state);
            state = transition.State;
            Step(tether, 0, 0, lifecycle: TetherLifecycle.Active, reason: state.Reason);
            if (transition.Command == ReconstructionRetryCommand.Retry) reconstructionCalls++;
            True(CoopStatusRenderPolicy.Eligible(true, true, true, false, false, false, true));
            False(CoopStatusRenderPolicy.AvatarEligible(true, false, false, false, false));
        }
        Equal(0, reconstructionCalls);
        ReconstructionRetryTransition due = ReconstructionRetryPolicy.SafeUpdate(state);
        Step(tether, 0, 0, lifecycle: TetherLifecycle.Active, reason: due.State.Reason);
        if (due.Command == ReconstructionRetryCommand.Retry) reconstructionCalls++;
        Equal(1, reconstructionCalls);
        Equal(1UL, tether.Diagnostics.SuspensionEntries);
        Equal(30UL, tether.Diagnostics.SuspensionFrames);
        Equal(30UL, tether.Diagnostics.SuspensionMaxConsecutive);
        Equal(TetherReason.UnsupportedTerrain, tether.Diagnostics.Reason);
    }),
    ("retry counter exhaustion is nonwrapping and atomic", () =>
    {
        var retryExhausted = new ReconstructionRetryState(true, false, 1, ulong.MaxValue, 0,
            TetherReason.UnsupportedTerrain);
        Reject(() => ReconstructionRetryPolicy.SafeUpdate(retryExhausted));
        var suppressExhausted = new ReconstructionRetryState(true, false, 2, 0, ulong.MaxValue,
            TetherReason.UnsupportedTerrain);
        Reject(() => ReconstructionRetryPolicy.SafeUpdate(suppressExhausted));
    }),
    ("retry deterministic generated sequence and zero allocation", () =>
    {
        for (uint seed = 1; seed <= 64; seed++) Equal(RetryReplay(seed), RetryReplay(seed));
        ReconstructionRetryState state = ReconstructionRetryPolicy.RetryableFault(
            ReconstructionRetryPolicy.Initial, TetherReason.UnsupportedTerrain);
        for (int i = 0; i < 100; i++) state = RetryCycle(state);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) state = RetryCycle(state);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }),
    ("route aggregate requires exact consecutive 25", () =>
    {
        int[] route = [140, 220, 140];
        var aggregate = new RouteTransitionAggregateReducer();
        for (int i = 0; i < 25; i++)
        {
            int index = i % 2;
            bool complete = aggregate.Observe(route[index], route[index + 1], true);
            Equal(i == 24, complete);
        }
        True(aggregate.State.Complete);
        Equal(25, aggregate.State.ValidObservations);
        var mismatch = new RouteTransitionAggregateReducer();
        False(mismatch.Observe(140, 52, true));
        True(mismatch.State.Stopped);
        False(mismatch.Observe(140, 220, true));
        Equal(0, mismatch.State.ValidObservations);
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopTetherPolicy: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static TetherCommand Step(TetherSuspensionReducer policy, int x, int y,
    TetherMovementIntent intent = TetherMovementIntent.None,
    TetherLifecycle lifecycle = TetherLifecycle.Active, TetherReason reason = TetherReason.None) =>
    policy.Reduce(new TetherObservation(x, y, intent, lifecycle, reason));
static ulong Replay(uint seed)
{
    var policy = new TetherSuspensionReducer();
    ulong hash = 14695981039346656037UL;
    for (int i = 0; i < 1000; i++)
    {
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        TetherCommand result = Step(policy, (int)(seed % 300), (int)((seed >> 9) % 220),
            (TetherMovementIntent)(seed % 3));
        hash = unchecked((hash ^ (byte)result.Phase) * 1099511628211UL);
        hash = unchecked((hash ^ (byte)result.Movement) * 1099511628211UL);
    }
    return hash;
}
static ReconstructionRetryState RetryCycle(ReconstructionRetryState state)
{
    ReconstructionRetryTransition transition = ReconstructionRetryPolicy.SafeUpdate(state);
    return transition.Command == ReconstructionRetryCommand.Retry
        ? ReconstructionRetryPolicy.RetryableFault(transition.State, TetherReason.UnsupportedTerrain)
        : transition.State;
}
static ulong RetryReplay(uint seed)
{
    ReconstructionRetryState state = ReconstructionRetryPolicy.Initial;
    ulong hash = 14695981039346656037UL;
    for (int i = 0; i < 1000; i++)
    {
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        if (!state.Active && (seed & 3) == 0)
            state = ReconstructionRetryPolicy.RetryableFault(state, TetherReason.UnsupportedTerrain);
        else if (state.Active && (seed & 31) == 1)
            state = ReconstructionRetryPolicy.ClearCurrent(state);
        else if (state.Active)
            state = RetryCycle(state);
        hash = unchecked((hash ^ (uint)state.CooldownRemaining) * 1099511628211UL);
        hash = unchecked((hash ^ state.Retries) * 1099511628211UL);
    }
    return hash;
}
static void Set(object value, string field, object replacement) => value.GetType().GetField(field,
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(value, replacement);
static void Reject(Action action)
{
    try { action(); } catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Expected rejection.");
}
static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
static void False(bool value) => True(!value);
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
