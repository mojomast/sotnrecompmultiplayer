using System;

namespace CoopFeasibilityMod;

public readonly record struct ExactOwnedAttackLifetime
{
    public ExactOwnedAttackLifetime(long current, long maximum)
    {
        if (current < 0 || maximum < current)
            throw new ArgumentOutOfRangeException(nameof(current));
        Current = current;
        Maximum = maximum;
    }

    public long Current { get; }
    public long Maximum { get; }
}

/// <summary>Counts every native window for which the retained attack tuple is still exact.</summary>
public static class ExactOwnedAttackLifetimeReducer
{
    public static ExactOwnedAttackLifetime Observe(ExactOwnedAttackLifetime state, bool exactOwned)
    {
        if (!exactOwned) return new(0, state.Maximum);
        if (state.Current == long.MaxValue)
            throw new InvalidOperationException("Exact-owned attack lifetime is exhausted.");
        long current = state.Current + 1;
        return new(current, Math.Max(current, state.Maximum));
    }

    public static ExactOwnedAttackLifetime Reset() => default;
}
