using System;
using System.Threading;

namespace CoopFeasibilityMod;

public enum ManagedStanceCommandKind : byte
{
    None,
    ProbeStandingHull,
}

public readonly struct ManagedStanceState
{
    public readonly ulong Revision;
    public readonly bool Crouched;
    public readonly bool StandBlocked;

    internal ManagedStanceState(ulong revision, bool crouched, bool standBlocked)
    {
        Revision = revision;
        Crouched = crouched;
        StandBlocked = standBlocked;
    }
}

public readonly struct ManagedStanceCommand
{
    public readonly ManagedStanceCommandKind Kind;
    public readonly ulong Revision;
    internal readonly long OwnerId;

    internal ManagedStanceCommand(ManagedStanceCommandKind kind, long ownerId, ulong revision)
    {
        Kind = kind;
        OwnerId = ownerId;
        Revision = revision;
    }
}

public readonly struct ManagedStanceTransition
{
    public readonly ManagedStanceState State;
    public readonly ManagedStanceCommand Command;

    internal ManagedStanceTransition(ManagedStanceState state, ManagedStanceCommand command = default)
    {
        State = state;
        Command = command;
    }
}

public readonly struct ManagedStanceInitialization
{
    internal readonly long OwnerId;
    internal readonly ulong ExpectedRevision;
    internal readonly ulong NextRevision;
    internal readonly bool Crouched;

    internal ManagedStanceInitialization(long ownerId, ulong expectedRevision, ulong nextRevision,
        bool crouched)
    {
        OwnerId = ownerId;
        ExpectedRevision = expectedRevision;
        NextRevision = nextRevision;
        Crouched = crouched;
    }
}

// Owns only stance policy. Native hull queries and all movement/visual consequences stay in the adapter.
public sealed class ManagedStanceReducer
{
    private static long _lastOwnerId;

    private readonly long _ownerId = AllocateOwnerId();
    private ulong _revision = 1;
    private bool _crouched;
    private bool _standBlocked;

    public ManagedStanceState State => new(_revision, _crouched, _standBlocked);
    public bool Crouched => _crouched;
    public bool StandBlocked => _standBlocked;

    public ManagedStanceTransition Observe(bool canAct, bool grounded, bool downPressed)
    {
        Validate();
        if (!canAct || !grounded) return new ManagedStanceTransition(State);

        if (downPressed)
        {
            // Reasserting crouch deliberately preserves a previous blocked-stand diagnostic.
            if (!_crouched)
            {
                ulong revision = NextRevision(_revision);
                _crouched = true;
                _revision = revision;
            }
            return new ManagedStanceTransition(State);
        }

        if (!_crouched) return new ManagedStanceTransition(State);
        return new ManagedStanceTransition(State,
            new ManagedStanceCommand(ManagedStanceCommandKind.ProbeStandingHull, _ownerId, _revision));
    }

    public ManagedStanceState CompleteStandingProbe(ManagedStanceCommand command, bool clear)
    {
        RequireCommand(command);
        ulong revision = NextRevision(_revision);
        _crouched = !clear;
        _standBlocked = !clear;
        _revision = revision;
        return State;
    }

    public ManagedStanceState ApplyLethalDamage()
    {
        Validate();
        if (!_crouched) return State;
        ulong revision = NextRevision(_revision);
        _crouched = false;
        // Legacy lethal damage stood the proxy without changing standBlocked.
        _revision = revision;
        return State;
    }

    public ManagedStanceState Initialize(bool crouched)
    {
        ManagedStanceInitialization initialization = PrepareInitialization(crouched);
        if (!CommitInitialization(initialization))
            throw new InvalidOperationException("Managed stance initialization became stale.");
        return State;
    }

    public ManagedStanceInitialization PrepareInitialization(bool crouched)
    {
        Validate();
        return new ManagedStanceInitialization(_ownerId, _revision, NextRevision(_revision), crouched);
    }

    public bool CanCommitInitialization(ManagedStanceInitialization initialization) =>
        initialization.OwnerId == _ownerId && initialization.ExpectedRevision != 0 &&
        _revision != ulong.MaxValue && initialization.ExpectedRevision == _revision &&
        initialization.NextRevision == _revision + 1;

    public bool CommitInitialization(ManagedStanceInitialization initialization)
    {
        if (!CanCommitInitialization(initialization)) return false;
        CommitPreparedInitialization(initialization);
        return true;
    }

    internal void CommitPreparedInitialization(ManagedStanceInitialization initialization)
    {
        _crouched = initialization.Crouched;
        _standBlocked = false;
        _revision = initialization.NextRevision;
    }

    private void RequireCommand(ManagedStanceCommand command)
    {
        Validate();
        if (command.Kind != ManagedStanceCommandKind.ProbeStandingHull ||
            command.OwnerId != _ownerId || command.Revision == 0 || command.Revision != _revision ||
            !_crouched)
            throw new InvalidOperationException("Standing-hull command is stale or belongs to another reducer.");
    }

    private void Validate()
    {
        if (_revision == 0) throw new InvalidOperationException("Managed stance revision is invalid.");
    }

    private static ulong NextRevision(ulong revision)
    {
        if (revision == ulong.MaxValue)
            throw new InvalidOperationException("Managed stance revision is exhausted.");
        return revision + 1;
    }

    private static long AllocateOwnerId()
    {
        while (true)
        {
            long current = Volatile.Read(ref _lastOwnerId);
            if (current == long.MaxValue)
                throw new InvalidOperationException("Managed stance owner identity is exhausted.");
            long next = current + 1;
            if (Interlocked.CompareExchange(ref _lastOwnerId, next, current) == current) return next;
        }
    }
}
