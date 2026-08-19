using System;
using System.Threading;

namespace CoopFeasibilityMod;

public enum AttackLeasePhase { Empty, Owned, CleanupPending, MutationStopped }
public enum AttackLeaseCommandKind { None, ProbeOwned, ContinueOwnedWork, ProbeQuarantine, ClearQuarantine }

public readonly struct AttackLeaseTuple
{
    public readonly int Slot;
    public readonly uint Generation;
    public readonly uint RoomHash;

    public AttackLeaseTuple(int slot, uint generation, uint roomHash)
    {
        if (slot is < 17 or >= 48) throw new ArgumentOutOfRangeException(nameof(slot));
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        Slot = slot;
        Generation = generation;
        RoomHash = roomHash;
    }
}

public readonly struct AttackLeaseCommand
{
    public readonly AttackLeaseCommandKind Kind;
    public readonly AttackLeaseTuple Lease;
    internal readonly ulong MachineOwner;
    internal readonly ulong Revision;

    internal AttackLeaseCommand(AttackLeaseCommandKind kind, AttackLeaseTuple lease, ulong machineOwner, ulong revision)
    {
        Kind = kind;
        Lease = lease;
        MachineOwner = machineOwner;
        Revision = revision;
    }
}

public readonly struct AttackLeasePreparedTransition
{
    internal readonly ulong MachineOwner;
    internal readonly ulong Revision;
    internal readonly AttackLeaseState Result;

    internal AttackLeasePreparedTransition(AttackLeaseState source, AttackLeaseState result)
    {
        MachineOwner = source.MachineOwner;
        Revision = source.Revision;
        Result = result;
    }
}

public struct AttackLeaseState
{
    public int OwnedSlot;
    public uint OwnedGeneration;
    public uint OwnedRoomHash;
    public bool CleanupPending;
    public int QuarantineSlot;
    public uint QuarantineGeneration;
    public uint QuarantineRoomHash;
    public bool MutationStopped;
    internal ulong MachineOwner;
    internal ulong Revision;

    public AttackLeasePhase Phase => MutationStopped ? AttackLeasePhase.MutationStopped :
        CleanupPending ? AttackLeasePhase.CleanupPending : OwnedSlot >= 0 ? AttackLeasePhase.Owned : AttackLeasePhase.Empty;
}

public static class AttackLeaseMachine
{
    private static long s_nextMachineOwner;

    public static AttackLeaseState Initial() => new()
    {
        OwnedSlot = -1,
        QuarantineSlot = -1,
        MachineOwner = NextMachineOwner(),
        Revision = 1
    };

    public static AttackLeaseState Reserve(AttackLeaseState state, int slot, uint roomHash)
    {
        Validate(state);
        RequirePhase(state, AttackLeasePhase.Empty);
        uint generation = unchecked(state.OwnedGeneration + 1);
        if (generation == 0) generation = 1;
        _ = new AttackLeaseTuple(slot, generation, roomHash);
        state.OwnedSlot = slot;
        state.OwnedGeneration = generation;
        state.OwnedRoomHash = roomHash;
        state.Revision = NextRevision(state.Revision);
        Validate(state);
        return state;
    }

    public static AttackLeaseCommand RequestOwnedCleanup(AttackLeaseState state)
    {
        Validate(state);
        return state.Phase is AttackLeasePhase.Owned or AttackLeasePhase.CleanupPending
            ? Command(AttackLeaseCommandKind.ProbeOwned, OwnedTuple(state), state)
            : default;
    }

    public static AttackLeaseCommand RequestQuarantineRetry(AttackLeaseState state)
    {
        Validate(state);
        return state.Phase == AttackLeasePhase.CleanupPending
            ? Command(AttackLeaseCommandKind.ProbeQuarantine, QuarantineTuple(state), state)
            : default;
    }

    public static AttackLeaseCommand OwnedExact(AttackLeaseState state, AttackLeaseCommand probe)
    {
        RequireCommand(state, probe, AttackLeaseCommandKind.ProbeOwned);
        return Command(AttackLeaseCommandKind.ContinueOwnedWork, OwnedTuple(state), state);
    }

    public static AttackLeaseCommand RetryExact(AttackLeaseState state, AttackLeaseCommand probe)
    {
        RequirePhase(state, AttackLeasePhase.CleanupPending);
        RequireCommand(state, probe, AttackLeaseCommandKind.ProbeQuarantine);
        return Command(AttackLeaseCommandKind.ClearQuarantine, QuarantineTuple(state), state);
    }

    public static AttackLeaseState ProbeFault(AttackLeaseState state, AttackLeaseCommand probe)
    {
        RequireProbe(state, probe);
        AttackLeaseTuple lease = probe.Lease;
        state.QuarantineSlot = lease.Slot;
        state.QuarantineGeneration = lease.Generation;
        state.QuarantineRoomHash = lease.RoomHash;
        state.CleanupPending = true;
        state.MutationStopped = false;
        state.Revision = NextRevision(state.Revision);
        Validate(state);
        return state;
    }

    public static AttackLeaseState ProbeReused(AttackLeaseState state, AttackLeaseCommand probe)
    {
        RequireProbe(state, probe);
        AttackLeaseTuple lease = probe.Lease;
        state.QuarantineSlot = lease.Slot;
        state.QuarantineGeneration = lease.Generation;
        state.QuarantineRoomHash = lease.RoomHash;
        state.OwnedSlot = -1;
        state.CleanupPending = false;
        state.MutationStopped = true;
        state.Revision = NextRevision(state.Revision);
        Validate(state);
        return state;
    }

    public static AttackLeaseState TerminalFault(AttackLeaseState state, AttackLeaseCommand probe)
    {
        RequireProbe(state, probe);
        AttackLeaseTuple lease = probe.Lease;
        state.QuarantineSlot = lease.Slot;
        state.QuarantineGeneration = lease.Generation;
        state.QuarantineRoomHash = lease.RoomHash;
        state.OwnedSlot = -1;
        state.CleanupPending = false;
        state.MutationStopped = true;
        state.Revision = NextRevision(state.Revision);
        Validate(state);
        return state;
    }

    public static AttackLeaseState RetainOwned(AttackLeaseState state, AttackLeaseCommand authorization)
    {
        RequirePhase(state, AttackLeasePhase.Owned);
        RequireCommand(state, authorization, AttackLeaseCommandKind.ContinueOwnedWork);
        return state;
    }

    public static AttackLeaseState ClearSucceeded(AttackLeaseState state, AttackLeaseCommand authorization)
    {
        if (authorization.Kind == AttackLeaseCommandKind.ContinueOwnedWork)
            RequireCommand(state, authorization, AttackLeaseCommandKind.ContinueOwnedWork);
        else
        {
            RequirePhase(state, AttackLeasePhase.CleanupPending);
            RequireCommand(state, authorization, AttackLeaseCommandKind.ClearQuarantine);
        }
        state.OwnedSlot = -1;
        state.OwnedRoomHash = 0;
        state.CleanupPending = false;
        state.QuarantineSlot = -1;
        state.QuarantineGeneration = 0;
        state.QuarantineRoomHash = 0;
        state.MutationStopped = false;
        state.Revision = NextRevision(state.Revision);
        Validate(state);
        return state;
    }

    public static AttackLeaseState RetryFree(AttackLeaseState state, AttackLeaseCommand probe)
    {
        RequirePhase(state, AttackLeasePhase.CleanupPending);
        RequireCommand(state, probe, AttackLeaseCommandKind.ProbeQuarantine);
        AttackLeaseCommand clear = Command(AttackLeaseCommandKind.ClearQuarantine, QuarantineTuple(state), state);
        return ClearSucceeded(state, clear);
    }

    public static AttackLeaseState DiagnosticReset(AttackLeaseState state)
    {
        Validate(state);
        if (state.Phase == AttackLeasePhase.CleanupPending) return state;
        if (state.Phase == AttackLeasePhase.MutationStopped)
        {
            state.OwnedGeneration = 0;
            state.OwnedRoomHash = 0;
            state.Revision = NextRevision(state.Revision);
            Validate(state);
            return state;
        }
        return Initial();
    }

    public static AttackLeasePreparedTransition PrepareOwnedClear(AttackLeaseState state)
    {
        RequirePhase(state, AttackLeasePhase.Owned);
        AttackLeaseCommand probe = RequestOwnedCleanup(state);
        AttackLeaseCommand authorization = OwnedExact(state, probe);
        return new AttackLeasePreparedTransition(state, ClearSucceeded(state, authorization));
    }

    public static AttackLeasePreparedTransition PrepareQuarantineClear(AttackLeaseState state)
    {
        RequirePhase(state, AttackLeasePhase.CleanupPending);
        AttackLeaseCommand probe = RequestQuarantineRetry(state);
        return new AttackLeasePreparedTransition(state, RetryFree(state, probe));
    }

    public static AttackLeasePreparedTransition PrepareObservedReuse(AttackLeaseState state)
    {
        if (state.Phase is not (AttackLeasePhase.Owned or AttackLeasePhase.CleanupPending))
            throw new InvalidOperationException("Observed reuse preparation requires retained ownership.");
        AttackLeaseCommand probe = state.Phase == AttackLeasePhase.CleanupPending
            ? RequestQuarantineRetry(state) : RequestOwnedCleanup(state);
        return new AttackLeasePreparedTransition(state, ProbeReused(state, probe));
    }

    public static AttackLeasePreparedTransition PrepareDiagnosticReset(AttackLeaseState state) =>
        new(state, DiagnosticReset(state));

    public static AttackLeasePreparedTransition PrepareDiagnosticResetAfter(AttackLeaseState state,
        in AttackLeasePreparedTransition first)
    {
        if (first.MachineOwner != state.MachineOwner || first.Revision != state.Revision)
            throw new InvalidOperationException("Attack lease transition composition is stale.");
        return new AttackLeasePreparedTransition(state, DiagnosticReset(first.Result));
    }

    public static bool CommitPrepared(ref AttackLeaseState state,
        in AttackLeasePreparedTransition transition)
    {
        if (!CanCommitPrepared(state, transition)) return false;
        state = transition.Result;
        return true;
    }

    public static bool CanCommitPrepared(AttackLeaseState state,
        in AttackLeasePreparedTransition transition) => transition.MachineOwner != 0 &&
        state.MachineOwner == transition.MachineOwner && state.Revision == transition.Revision;

    public static void Validate(AttackLeaseState state)
    {
        if (state.MachineOwner == 0 || state.Revision == 0 || state.OwnedSlot < -1 || state.QuarantineSlot < -1)
            throw new InvalidOperationException("Attack lease sentinel or revision is invalid.");
        if (state.OwnedSlot >= 0) _ = OwnedTuple(state);
        if (state.QuarantineSlot >= 0) _ = QuarantineTuple(state);
        if (state.CleanupPending && (state.OwnedSlot < 0 || state.QuarantineSlot != state.OwnedSlot ||
            state.QuarantineGeneration != state.OwnedGeneration || state.QuarantineRoomHash != state.OwnedRoomHash || state.MutationStopped))
            throw new InvalidOperationException("Retryable quarantine must retain one exact tuple.");
        if (state.MutationStopped && (state.OwnedSlot >= 0 || state.QuarantineSlot < 0 || state.CleanupPending ||
            state.OwnedGeneration != 0 && state.OwnedGeneration != state.QuarantineGeneration))
            throw new InvalidOperationException("Mutation-stopped quarantine is inconsistent.");
        if (state.Phase == AttackLeasePhase.Empty && (state.QuarantineSlot >= 0 || state.OwnedRoomHash != 0 ||
            state.QuarantineGeneration != 0 || state.QuarantineRoomHash != 0))
            throw new InvalidOperationException("Empty lease contains stale metadata.");
        if (state.Phase == AttackLeasePhase.Owned && (state.QuarantineSlot >= 0 || state.QuarantineGeneration != 0 ||
            state.QuarantineRoomHash != 0))
            throw new InvalidOperationException("Owned lease contains stale quarantine metadata.");
    }

    private static void RequireProbe(AttackLeaseState state, AttackLeaseCommand command)
    {
        if (command.Kind is not (AttackLeaseCommandKind.ProbeOwned or AttackLeaseCommandKind.ProbeQuarantine))
            throw new InvalidOperationException("A probe command is required.");
        RequireCommand(state, command, command.Kind);
    }

    private static void RequireCommand(AttackLeaseState state, AttackLeaseCommand command, AttackLeaseCommandKind kind)
    {
        Validate(state);
        if (command.Kind != kind || command.MachineOwner != state.MachineOwner ||
            command.Revision != state.Revision || !Same(command.Lease,
            kind is AttackLeaseCommandKind.ProbeQuarantine or AttackLeaseCommandKind.ClearQuarantine
                ? QuarantineTuple(state) : OwnedTuple(state)))
            throw new InvalidOperationException("Attack lease command is stale or does not match the current tuple.");
    }

    private static void RequirePhase(AttackLeaseState state, AttackLeasePhase phase)
    {
        Validate(state);
        if (state.Phase != phase) throw new InvalidOperationException($"Attack lease must be {phase}.");
    }

    private static AttackLeaseCommand Command(AttackLeaseCommandKind kind, AttackLeaseTuple lease, AttackLeaseState state) =>
        new(kind, lease, state.MachineOwner, state.Revision);
    private static AttackLeaseTuple OwnedTuple(AttackLeaseState state) => new(state.OwnedSlot, state.OwnedGeneration, state.OwnedRoomHash);
    private static AttackLeaseTuple QuarantineTuple(AttackLeaseState state) => new(state.QuarantineSlot, state.QuarantineGeneration, state.QuarantineRoomHash);
    private static bool Same(AttackLeaseTuple left, AttackLeaseTuple right) =>
        left.Slot == right.Slot && left.Generation == right.Generation && left.RoomHash == right.RoomHash;
    // Revisions never wrap: an old authorization can therefore never become current again.
    private static ulong NextRevision(ulong value) => value == ulong.MaxValue
        ? throw new InvalidOperationException("Attack lease revision space is exhausted.")
        : value + 1;

    private static ulong NextMachineOwner()
    {
        long owner = Interlocked.Increment(ref s_nextMachineOwner);
        if (owner <= 0) throw new InvalidOperationException("Attack lease machine owner space is exhausted.");
        return unchecked((ulong)owner);
    }
}
