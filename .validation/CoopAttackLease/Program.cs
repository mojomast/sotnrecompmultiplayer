using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("reserve clear and generation", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 0xAA);
        AttackLeaseCommand probe = AttackLeaseMachine.RequestOwnedCleanup(state);
        AttackLeaseCommand clear = AttackLeaseMachine.OwnedExact(state, probe);
        state = AttackLeaseMachine.ClearSucceeded(state, clear);
        state = AttackLeaseMachine.Reserve(state, 47, 0xBB);
        Equal(2U, state.OwnedGeneration);
    }),
    ("generation wrap skips zero", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Initial();
        state.OwnedGeneration = uint.MaxValue;
        state = AttackLeaseMachine.Reserve(state, 17, 1);
        Equal(1U, state.OwnedGeneration);
    }),
    ("probe fault and exact same-call retry", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        var native = new FakeNative { FailAt = 1 };
        CancelWithSameCallRetry(ref state, native);
        Equal(AttackLeasePhase.Empty, state.Phase);
        Equal(1, native.Clears);
    }),
    ("clear fault and free same-call retry", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        var native = new FakeNative { FailAt = 3 };
        CancelWithSameCallRetry(ref state, native);
        Equal(AttackLeasePhase.Empty, state.Phase);
        True(native.Writes > 0);
    }),
    ("mismatch permanently stops all adapter work", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        var native = new FakeNative { State = NativeLeaseState.Reused };
        CancelWithSameCallRetry(ref state, native);
        Equal(AttackLeasePhase.MutationStopped, state.Phase);
        int operations = native.Operations;
        CancelWithSameCallRetry(ref state, native);
        Equal(operations, native.Operations);
    }),
    ("stale command rejected", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        AttackLeaseCommand stale = AttackLeaseMachine.RequestOwnedCleanup(state);
        state = AttackLeaseMachine.ProbeFault(state, stale);
        Reject(() => AttackLeaseMachine.OwnedExact(state, stale));
    }),
    ("machine owner rejects reset ABA", () =>
    {
        AttackLeaseState first = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        AttackLeaseCommand stale = AttackLeaseMachine.RequestOwnedCleanup(first);
        AttackLeaseState second = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        Reject(() => AttackLeaseMachine.OwnedExact(second, stale));
    }),
    ("revision exhaustion cannot wrap into ABA", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Initial();
        state.Revision = ulong.MaxValue;
        Reject(() => AttackLeaseMachine.Reserve(state, 17, 1));
    }),
    ("prepared cleanup and reset refuse exhaustion before commit", () =>
    {
        AttackLeaseState owned = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        owned.Revision = ulong.MaxValue;
        Reject(() => AttackLeaseMachine.PrepareOwnedClear(owned));

        AttackLeaseState terminal = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        terminal = AttackLeaseMachine.ProbeReused(terminal,
            AttackLeaseMachine.RequestOwnedCleanup(terminal));
        terminal.Revision = ulong.MaxValue;
        Reject(() => AttackLeaseMachine.PrepareDiagnosticReset(terminal));
    }),
    ("prepared clear is owner revision bound and nonthrowing", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        AttackLeasePreparedTransition prepared = AttackLeaseMachine.PrepareOwnedClear(state);
        AttackLeaseState stale = AttackLeaseMachine.ProbeFault(state,
            AttackLeaseMachine.RequestOwnedCleanup(state));
        True(!AttackLeaseMachine.CommitPrepared(ref stale, prepared));
        True(AttackLeaseMachine.CommitPrepared(ref state, prepared));
        Equal(AttackLeasePhase.Empty, state.Phase);
    }),
    ("terminal quarantine cannot clear", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        AttackLeaseCommand probe = AttackLeaseMachine.RequestOwnedCleanup(state);
        state = AttackLeaseMachine.ProbeReused(state, probe);
        Reject(() => AttackLeaseMachine.ClearSucceeded(state, default));
    }),
    ("terminal fault retains evidence without reuse command", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 0xCAFE);
        AttackLeaseCommand probe = AttackLeaseMachine.RequestOwnedCleanup(state);
        state = AttackLeaseMachine.TerminalFault(state, probe);
        Equal(AttackLeasePhase.MutationStopped, state.Phase);
        Equal(17, state.QuarantineSlot);
        Equal(0xCAFEU, state.QuarantineRoomHash);
        Reject(() => AttackLeaseMachine.TerminalFault(state, probe));
    }),
    ("diagnostic reset preserves quarantine semantics", () =>
    {
        AttackLeaseState pending = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        pending = AttackLeaseMachine.ProbeFault(pending, AttackLeaseMachine.RequestOwnedCleanup(pending));
        AttackLeaseState pendingReset = AttackLeaseMachine.DiagnosticReset(pending);
        Equal(AttackLeasePhase.CleanupPending, pendingReset.Phase);
        Reject(() => AttackLeaseMachine.Reserve(pendingReset, 17, 2));
        AttackLeaseState stopped = AttackLeaseMachine.ProbeReused(pending,
            AttackLeaseMachine.RequestQuarantineRetry(pending));
        stopped = AttackLeaseMachine.DiagnosticReset(stopped);
        Equal(AttackLeasePhase.MutationStopped, stopped.Phase);
        Equal(0U, stopped.OwnedGeneration);
        Reject(() => AttackLeaseMachine.Reserve(stopped, 17, 2));
    }),
    ("reset without quarantine reuses generation one", () =>
    {
        AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
        AttackLeaseCommand clear = AttackLeaseMachine.OwnedExact(state, AttackLeaseMachine.RequestOwnedCleanup(state));
        state = AttackLeaseMachine.ClearSucceeded(state, clear);
        state = AttackLeaseMachine.DiagnosticReset(state);
        state = AttackLeaseMachine.Reserve(state, 17, 1);
        Equal(1U, state.OwnedGeneration);
    }),
    ("fault at every adapter operation remains safe", () =>
    {
        for (int failure = 1; failure <= 8; failure++)
        {
            AttackLeaseState state = AttackLeaseMachine.Reserve(AttackLeaseMachine.Initial(), 17, 1);
            var native = new FakeNative { FailAt = failure };
            CancelWithSameCallRetry(ref state, native);
            AttackLeaseMachine.Validate(state);
            if (state.Phase == AttackLeasePhase.MutationStopped) Equal(0, native.WritesAfterReuse);
        }
    }),
    ("generated sequences preserve invariants", () =>
    {
        for (uint seed = 1; seed <= 64; seed++) Replay(seed, 256);
    })
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopAttackLease: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static void CancelWithSameCallRetry(ref AttackLeaseState state, FakeNative native)
{
    AttackLeaseCommand probe = AttackLeaseMachine.RequestOwnedCleanup(state);
    if (probe.Kind != AttackLeaseCommandKind.None)
    {
        try
        {
            NativeLeaseState observation = native.Probe();
            if (observation == NativeLeaseState.Exact)
            {
                AttackLeaseCommand clear = AttackLeaseMachine.OwnedExact(state, probe);
                try
                {
                    native.Clear();
                    state = AttackLeaseMachine.ClearSucceeded(state, clear);
                }
                catch
                {
                    state = AttackLeaseMachine.ProbeFault(state, probe);
                }
            }
            else state = AttackLeaseMachine.ProbeReused(state, probe);
        }
        catch
        {
            state = AttackLeaseMachine.ProbeFault(state, probe);
        }
    }

    AttackLeaseCommand retry = AttackLeaseMachine.RequestQuarantineRetry(state);
    if (retry.Kind == AttackLeaseCommandKind.None) return;
    try
    {
        NativeLeaseState observation = native.Probe();
        if (observation == NativeLeaseState.Exact)
        {
            AttackLeaseCommand clear = AttackLeaseMachine.RetryExact(state, retry);
            try
            {
                native.Clear();
                state = AttackLeaseMachine.ClearSucceeded(state, clear);
            }
            catch
            {
                state = AttackLeaseMachine.ProbeFault(state, retry);
            }
        }
        else if (observation == NativeLeaseState.Free) state = AttackLeaseMachine.RetryFree(state, retry);
        else state = AttackLeaseMachine.ProbeReused(state, retry);
    }
    catch
    {
        state = AttackLeaseMachine.ProbeFault(state, retry);
    }
}

static void Replay(uint seed, int steps)
{
    AttackLeaseState state = AttackLeaseMachine.Initial();
    var native = new FakeNative();
    for (int index = 0; index < steps; index++)
    {
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        if (state.Phase == AttackLeasePhase.Empty && (seed & 3) == 0)
        {
            state = AttackLeaseMachine.Reserve(state, 17 + (int)(seed % 31), seed);
            native = new FakeNative { State = NativeLeaseState.Exact, FailAt = (int)(seed % 9) };
        }
        else if (state.Phase != AttackLeasePhase.Empty) CancelWithSameCallRetry(ref state, native);
        else state = AttackLeaseMachine.DiagnosticReset(state);
        AttackLeaseMachine.Validate(state);
    }
}

static void Reject(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Invalid lease transition was accepted.");
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

enum NativeLeaseState { Exact, Free, Reused }

sealed class FakeNative
{
    public NativeLeaseState State = NativeLeaseState.Exact;
    public int FailAt;
    public int Operations;
    public int Writes;
    public int Clears;
    public int WritesAfterReuse;

    public NativeLeaseState Probe()
    {
        Step();
        return State;
    }

    public void Clear()
    {
        for (int index = 0; index < 3; index++)
        {
            Step();
            if (State == NativeLeaseState.Reused) WritesAfterReuse++;
            Writes++;
            if (index == 0) State = NativeLeaseState.Free;
        }
        Clears++;
    }

    private void Step()
    {
        Operations++;
        if (FailAt == Operations) throw new InvalidOperationException("injected adapter fault");
    }
}
