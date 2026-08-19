using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("reset state", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        Equal(100, state.Hp);
        Equal(-1, state.LastDamageSlot);
    }),
    ("nonlethal hit and same-update timers", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        ManagedDamageTransition hit = ManagedHealthMachine.ApplyIncomingHit(state, 7, 64, 2, true);
        True(hit.Applied && !hit.Lethal && hit.State.CompactHurt);
        state = ManagedHealthMachine.AdvanceTimers(hit.State, true, true);
        Equal(93, state.Hp);
        Equal(59, state.Invulnerability);
        Equal(17, state.HurtLock);
    }),
    ("invulnerability suppresses hit", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.ApplyIncomingHit(ManagedHealthMachine.Reset(),
            5, 64, 0, false).State;
        ManagedDamageTransition second = ManagedHealthMachine.ApplyIncomingHit(state, 40, 65, 0, false);
        True(!second.Applied);
        Equal(1, second.State.SuppressedInvulnerability);
        Equal(1, second.State.SuppressedHitInvulnerability);
    }),
    ("lethal hit downs", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        state.Hp = 5;
        ManagedDamageTransition hit = ManagedHealthMachine.ApplyIncomingHit(state, 40, 70, 3, true);
        True(hit.Applied && hit.Lethal && hit.State.Downed);
        Equal(0, hit.State.Hp);
        Equal(0, hit.State.HurtLock);
    }),
    ("revive cancellation and completion", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        state.Hp = 1;
        state = ManagedHealthMachine.ApplyIncomingHit(state, 1, 64, 0, false).State;
        state = ManagedHealthMachine.ApplyRevive(state, true);
        Equal(1, state.ReviveStarts);
        state = ManagedHealthMachine.ApplyRevive(state, false);
        Equal(1, state.ReviveCancels);
        for (int i = 0; i < 120; i++) state = ManagedHealthMachine.ApplyRevive(state, true);
        Equal(50, state.Hp);
        Equal(1, state.Revives);
        Equal(1, state.ReviveRecoveries);
    }),
    ("revive eligibility boundaries", () =>
    {
        True(Observation(24, 32).Eligible);
        True(Observation(-24, -32).Eligible);
        True(!Observation(25, 32).Eligible);
        True(!Observation(24, 33).Eligible);
        True(!Observation(24, 32, playerOneDown: false).Eligible);
        True(!Observation(24, 32, playerTwoCircle: false).Eligible);
        True(!Observation(24, 32, roomStable: false).Eligible);
    }),
    ("revive completion preserves timer ordering", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        state.Hp = 1;
        state = ManagedHealthMachine.ApplyIncomingHit(state, 1, 64, 0, false).State;
        for (int i = 0; i < 120; i++)
        {
            bool decrementInvulnerability = state.Invulnerability > 0;
            bool decrementHurt = state.HurtLock > 0;
            state = ManagedHealthMachine.ApplyRevive(state, true);
            state = ManagedHealthMachine.AdvanceTimers(state, decrementInvulnerability, decrementHurt);
        }
        Equal(120, state.Invulnerability);
    }),
    ("reconstruction protection", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        state = ManagedHealthMachine.Reconstructed(state);
        Equal(60, state.Invulnerability);
        state.Downed = true;
        state.Hp = 0;
        state.Invulnerability = 10;
        state = ManagedHealthMachine.Reconstructed(state);
        Equal(10, state.Invulnerability);
    }),
    ("timer expiry clears hit invulnerability", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        state.Invulnerability = 1;
        state.HitInvulnerabilityActive = true;
        state = ManagedHealthMachine.AdvanceTimers(state, true, false);
        Equal(0, state.Invulnerability);
        True(!state.HitInvulnerabilityActive);
    }),
    ("downed residual protection suppresses then ignores", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        state.Hp = 1;
        state = ManagedHealthMachine.ApplyIncomingHit(state, 1, 64, 0, false).State;
        ManagedDamageTransition suppressed = ManagedHealthMachine.ApplyIncomingHit(state, 10, 65, 0, false);
        Equal(1, suppressed.State.SuppressedInvulnerability);
        state = suppressed.State;
        for (int i = 0; i < 60; i++) state = ManagedHealthMachine.AdvanceTimers(state, true, false);
        ManagedDamageTransition ignored = ManagedHealthMachine.ApplyIncomingHit(state, 10, 65, 0, false);
        True(!ignored.Applied);
        Equal(1, ignored.State.SuppressedInvulnerability);
    }),
    ("damage clamp and invalid damage", () =>
    {
        ManagedDamageTransition hit = ManagedHealthMachine.ApplyIncomingHit(ManagedHealthMachine.Reset(),
            999, 64, 0, false);
        Equal(60, hit.State.Hp);
        Reject(() => ManagedHealthMachine.ApplyIncomingHit(ManagedHealthMachine.Reset(), 0, 64, 0, false));
    }),
    ("checked counter overflow fails closed", () =>
    {
        ManagedHealthState state = ManagedHealthMachine.Reset();
        state.DamageConsumed = int.MaxValue;
        RejectOverflow(() => ManagedHealthMachine.ConsumeOpportunity(state));
    }),
    ("deterministic generated sequence", () =>
    {
        ManagedHealthState first = Replay(12345);
        ManagedHealthState second = Replay(12345);
        EqualState(first, second);
    })
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopManagedHealth: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static ManagedHealthState Replay(int seed)
{
    var random = new Random(seed);
    ManagedHealthState state = ManagedHealthMachine.Reset();
    for (int index = 0; index < 500; index++)
    {
        switch (random.Next(4))
        {
            case 0:
                state = ManagedHealthMachine.ConsumeOpportunity(state);
                state = ManagedHealthMachine.ApplyIncomingHit(state, random.Next(1, 80), random.Next(64, 192),
                    (ushort)random.Next(0, 16), random.Next(2) != 0).State;
                break;
            case 1:
                state = ManagedHealthMachine.ApplyRevive(state, random.Next(2) != 0);
                break;
            case 2:
                state = ManagedHealthMachine.AdvanceTimers(state, state.Invulnerability > 0, state.HurtLock > 0);
                break;
            default:
                state = ManagedHealthMachine.Reconstructed(state);
                break;
        }
        state = ManagedHealthMachine.Validate(state);
    }
    return state;
}

static ManagedReviveObservation Observation(int deltaX, int deltaY, bool playerOneDown = true,
    bool playerTwoCircle = true, bool roomStable = true) =>
    new(true, playerOneDown, playerTwoCircle, deltaX, deltaY, true, true, roomStable);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

static void Reject(Action action)
{
    try { action(); }
    catch (ArgumentOutOfRangeException) { return; }
    throw new InvalidOperationException("Invalid health input was accepted.");
}

static void RejectOverflow(Action action)
{
    try { action(); }
    catch (OverflowException) { return; }
    throw new InvalidOperationException("Counter overflow did not fail closed.");
}

static void EqualState(ManagedHealthState expected, ManagedHealthState actual)
{
    Equal(expected.Hp, actual.Hp);
    Equal(expected.Invulnerability, actual.Invulnerability);
    Equal(expected.HurtLock, actual.HurtLock);
    Equal(expected.Downed, actual.Downed);
    Equal(expected.DamageEvents, actual.DamageEvents);
    Equal(expected.DamageConsumed, actual.DamageConsumed);
    Equal(expected.SuppressedInvulnerability, actual.SuppressedInvulnerability);
    Equal(expected.SuppressedHitInvulnerability, actual.SuppressedHitInvulnerability);
    Equal(expected.HitInvulnerabilityActive, actual.HitInvulnerabilityActive);
    Equal(expected.DownedCount, actual.DownedCount);
    Equal(expected.ReviveStarts, actual.ReviveStarts);
    Equal(expected.ReviveCancels, actual.ReviveCancels);
    Equal(expected.ReviveRecoveries, actual.ReviveRecoveries);
    Equal(expected.InvariantFailures, actual.InvariantFailures);
    Equal(expected.CompactHurt, actual.CompactHurt);
    Equal(expected.LastDamage, actual.LastDamage);
    Equal(expected.LastDamageSlot, actual.LastDamageSlot);
    Equal(expected.LastDamageElement, actual.LastDamageElement);
    Equal(expected.ReviveProgress, actual.ReviveProgress);
    Equal(expected.Revives, actual.Revives);
}
