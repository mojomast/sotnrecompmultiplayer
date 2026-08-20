using System;

namespace CoopFeasibilityMod;

public enum ReconstructionRetryCommand : byte
{
    None,
    Suppress,
    Retry,
}

public readonly record struct ReconstructionRetryState(bool Active, bool Terminal,
    int CooldownRemaining, ulong Retries, ulong SuppressedAttempts, TetherReason Reason);

public readonly record struct ReconstructionRetryTransition(ReconstructionRetryState State,
    ReconstructionRetryCommand Command);

// Reconstruction probes are intentionally sparse after a nonfatal terrain failure. The first
// failed probe arms 30 safe updates; updates 1..29 suppress work and update 30 permits one retry.
// Counters never wrap, and no reducer operation allocates or mutates native state.
public static class ReconstructionRetryPolicy
{
    public const int CooldownSafeUpdates = 30;

    public static ReconstructionRetryState Initial =>
        new(false, false, 0, 0, 0, TetherReason.None);

    public static ReconstructionRetryState RetryableFault(in ReconstructionRetryState state,
        TetherReason reason)
    {
        Validate(state);
        if (reason is not (TetherReason.UnsupportedTerrain or TetherReason.Reconstruction or
            TetherReason.HardBoundary))
            throw new ArgumentOutOfRangeException(nameof(reason));
        return state with { Active = true, Terminal = false, CooldownRemaining = CooldownSafeUpdates,
            Reason = reason };
    }

    public static ReconstructionRetryState TerminalFault(in ReconstructionRetryState state,
        TetherReason reason)
    {
        Validate(state);
        if (reason is not (TetherReason.Collision or TetherReason.Fatal))
            throw new ArgumentOutOfRangeException(nameof(reason));
        return state with { Active = true, Terminal = true, CooldownRemaining = 0, Reason = reason };
    }

    public static ReconstructionRetryTransition SafeUpdate(in ReconstructionRetryState state)
    {
        Validate(state);
        if (!state.Active || state.Terminal)
            return new(state, state.Active ? ReconstructionRetryCommand.Suppress : ReconstructionRetryCommand.None);

        if (state.CooldownRemaining > 1)
        {
            ulong suppressed = Increment(state.SuppressedAttempts);
            return new(state with { CooldownRemaining = state.CooldownRemaining - 1,
                SuppressedAttempts = suppressed }, ReconstructionRetryCommand.Suppress);
        }

        if (state.CooldownRemaining == 1)
        {
            ulong retries = Increment(state.Retries);
            return new(state with { CooldownRemaining = 0, Retries = retries },
                ReconstructionRetryCommand.Retry);
        }

        // A retry command must be consumed synchronously. Suppress if an adapter ever fails to
        // report its result rather than allowing an accidental per-frame probe loop.
        return new(state, ReconstructionRetryCommand.Suppress);
    }

    public static ReconstructionRetryState ClearCurrent(in ReconstructionRetryState state)
    {
        Validate(state);
        return state with { Active = false, Terminal = false, CooldownRemaining = 0,
            Reason = TetherReason.None };
    }

    public static ReconstructionRetryState ResetDiagnostics() => Initial;

    private static void Validate(in ReconstructionRetryState state)
    {
        if (state.CooldownRemaining is < 0 or > CooldownSafeUpdates ||
            state.Reason is < TetherReason.None or > TetherReason.Downed ||
            (!state.Active && (state.Terminal || state.CooldownRemaining != 0 || state.Reason != TetherReason.None)) ||
            (state.Terminal && state.CooldownRemaining != 0))
            throw new ArgumentOutOfRangeException(nameof(state));
    }

    private static ulong Increment(ulong value) => value == ulong.MaxValue
        ? throw new InvalidOperationException("Reconstruction retry counter exhausted.") : value + 1;
}
