using System;
using System.Threading;

namespace CoopFeasibilityMod;

public enum ReconstructionPolicyPhase : byte
{
    Idle,
    Probing,
    Selected,
    NoSafeCandidate,
    Suspended,
}

public enum ReconstructionCommandKind : byte
{
    None,
    ProbeCandidate,
}

public enum ReconstructionObservation : byte
{
    Blocked,
    Valid,
    AdapterFault,
}

public readonly struct ReconstructionCandidate
{
    public readonly int Index;
    public readonly int OffsetX;
    public readonly int OffsetY;
    public readonly bool Crouched;

    internal ReconstructionCandidate(int index, int offsetX, int offsetY, bool crouched)
    {
        Index = index;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Crouched = crouched;
    }
}

public readonly struct ReconstructionPolicyState
{
    public readonly ulong Revision;
    public readonly ReconstructionPolicyPhase Phase;
    public readonly int CandidateIndex;

    internal ReconstructionPolicyState(ulong revision, ReconstructionPolicyPhase phase, int candidateIndex)
    {
        Revision = revision;
        Phase = phase;
        CandidateIndex = candidateIndex;
    }
}

public readonly struct ReconstructionCommand
{
    public readonly ReconstructionCommandKind Kind;
    public readonly ulong Revision;
    public readonly ReconstructionCandidate Candidate;
    internal readonly long OwnerId;

    internal ReconstructionCommand(long ownerId, ulong revision, ReconstructionCandidate candidate)
    {
        Kind = ReconstructionCommandKind.ProbeCandidate;
        OwnerId = ownerId;
        Revision = revision;
        Candidate = candidate;
    }
}

public readonly struct ReconstructionPolicyTransition
{
    public readonly ReconstructionPolicyState State;
    public readonly ReconstructionCommand Command;

    internal ReconstructionPolicyTransition(ReconstructionPolicyState state,
        ReconstructionCommand command = default)
    {
        State = state;
        Command = command;
    }
}

public readonly struct ReconstructionPolicyReset
{
    internal readonly long OwnerId;
    internal readonly ulong ExpectedRevision;
    internal readonly ulong NextRevision;

    internal ReconstructionPolicyReset(long ownerId, ulong expectedRevision, ulong nextRevision)
    {
        OwnerId = ownerId;
        ExpectedRevision = expectedRevision;
        NextRevision = nextRevision;
    }
}

public enum ReconstructionRunResult : byte
{
    Selected,
    NoSafeCandidate,
    AdapterFault,
}

// Implementations retain all environment-specific collision, state mutation, and diagnostic effects.
public interface IReconstructionPolicyAdapter
{
    ReconstructionObservation ProbeCandidate(int worldX, int worldY, bool crouched);
    // Preparation must not publish proxy/session success. Rollback must restore every projection
    // if any later preparation or the final commit faults.
    void PrepareInitialization(int worldX, int worldY, bool crouched);
    void PreparePoseProjection();
    void PrepareHealthProjection();
    void PrepareSuccessDiagnostics(ReconstructionCandidate candidate);
    void CommitPreparedSuccess();
    void CommitCollisionFault();
    void CommitNoSafeCandidate();
}

// Shared runtime/test orchestration. Generic dispatch avoids boxing struct adapters in the hot path.
public static class ReconstructionPolicyOrchestration
{
    public static ReconstructionRunResult Run<TAdapter>(ReconstructionPolicyReducer policy,
        int playerX, int playerY, ref TAdapter adapter) where TAdapter : IReconstructionPolicyAdapter
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        ReconstructionPolicyTransition transition = policy.Begin();
        while (transition.Command.Kind == ReconstructionCommandKind.ProbeCandidate)
        {
            ReconstructionCommand command = transition.Command;
            ReconstructionCandidate candidate = command.Candidate;
            int candidateX = playerX + candidate.OffsetX;
            int candidateY = playerY + candidate.OffsetY;
            ReconstructionObservation observation;
            try
            {
                observation = adapter.ProbeCandidate(candidateX, candidateY, candidate.Crouched);
            }
            catch
            {
                return ReconstructionRunResult.AdapterFault;
            }
            transition = policy.Observe(command, observation);
            if (observation == ReconstructionObservation.Blocked) continue;
            if (observation == ReconstructionObservation.AdapterFault)
            {
                try { adapter.CommitCollisionFault(); }
                catch { }
                return ReconstructionRunResult.AdapterFault;
            }

            try
            {
                // Preserve the legacy initialization/pose/health ordering while keeping all three
                // preparatory: authoritative state and success diagnostics publish together last.
                adapter.PrepareInitialization(candidateX, candidateY, candidate.Crouched);
                adapter.PreparePoseProjection();
                adapter.PrepareHealthProjection();
                adapter.PrepareSuccessDiagnostics(candidate);
                adapter.CommitPreparedSuccess();
                return ReconstructionRunResult.Selected;
            }
            catch
            {
                return ReconstructionRunResult.AdapterFault;
            }
        }

        if (transition.State.Phase != ReconstructionPolicyPhase.NoSafeCandidate)
            throw new InvalidOperationException("Reconstruction policy terminated without a candidate result.");
        try { adapter.CommitNoSafeCandidate(); }
        catch { return ReconstructionRunResult.AdapterFault; }
        return ReconstructionRunResult.NoSafeCandidate;
    }
}

// Owns only deterministic candidate order and selection. RAM and collision work remain in the adapter.
public sealed class ReconstructionPolicyReducer
{
    public const int CandidateCount = 80;

    private static long _lastOwnerId;
    private readonly long _ownerId = AllocateOwnerId();
    private ulong _revision = 1;
    private ReconstructionPolicyPhase _phase;
    private int _candidateIndex = -1;

    public ReconstructionPolicyState State => new(_revision, _phase, _candidateIndex);

    public ReconstructionPolicyTransition Begin()
    {
        ValidateState();
        ulong revision = NextRevision(_revision);
        _candidateIndex = 0;
        _phase = ReconstructionPolicyPhase.Probing;
        _revision = revision;
        return Issue();
    }

    public ReconstructionPolicyTransition Observe(ReconstructionCommand command,
        ReconstructionObservation observation)
    {
        RequireCommand(command);
        if (observation is < ReconstructionObservation.Blocked or > ReconstructionObservation.AdapterFault)
            throw new ArgumentOutOfRangeException(nameof(observation));

        ulong revision = NextRevision(_revision);
        switch (observation)
        {
            case ReconstructionObservation.Valid:
                _phase = ReconstructionPolicyPhase.Selected;
                break;
            case ReconstructionObservation.AdapterFault:
                _phase = ReconstructionPolicyPhase.Suspended;
                break;
            default:
                if (_candidateIndex == CandidateCount - 1)
                    _phase = ReconstructionPolicyPhase.NoSafeCandidate;
                else
                    _candidateIndex++;
                break;
        }
        _revision = revision;
        return _phase == ReconstructionPolicyPhase.Probing
            ? Issue()
            : new ReconstructionPolicyTransition(State);
    }

    public ReconstructionPolicyReset PrepareReset()
    {
        ValidateState();
        return new ReconstructionPolicyReset(_ownerId, _revision, NextRevision(_revision));
    }

    public bool CommitReset(in ReconstructionPolicyReset reset)
    {
        if (!CanCommitReset(reset)) return false;
        _revision = reset.NextRevision;
        _phase = ReconstructionPolicyPhase.Idle;
        _candidateIndex = -1;
        return true;
    }

    public bool CanCommitReset(in ReconstructionPolicyReset reset) =>
        reset.OwnerId == _ownerId && reset.ExpectedRevision == _revision &&
        reset.NextRevision == _revision + 1;

    private ReconstructionPolicyTransition Issue()
    {
        ReconstructionCandidate candidate = GetCandidate(_candidateIndex);
        return new ReconstructionPolicyTransition(State,
            new ReconstructionCommand(_ownerId, _revision, candidate));
    }

    private void RequireCommand(ReconstructionCommand command)
    {
        ValidateState();
        if (_phase != ReconstructionPolicyPhase.Probing ||
            command.Kind != ReconstructionCommandKind.ProbeCandidate ||
            command.OwnerId != _ownerId || command.Revision == 0 || command.Revision != _revision ||
            command.Candidate.Index != _candidateIndex)
            throw new InvalidOperationException("Reconstruction command is stale, out of order, or belongs to another reducer.");

        ReconstructionCandidate expected = GetCandidate(_candidateIndex);
        if (command.Candidate.OffsetX != expected.OffsetX || command.Candidate.OffsetY != expected.OffsetY ||
            command.Candidate.Crouched != expected.Crouched)
            throw new InvalidOperationException("Reconstruction command candidate is invalid.");
    }

    private void ValidateState()
    {
        if (_revision == 0 ||
            (_phase == ReconstructionPolicyPhase.Probing &&
             (_candidateIndex < 0 || _candidateIndex >= CandidateCount)))
            throw new InvalidOperationException("Reconstruction policy state is invalid.");
    }

    private static ReconstructionCandidate GetCandidate(int index)
    {
        if ((uint)index >= CandidateCount) throw new ArgumentOutOfRangeException(nameof(index));
        int pair = index >> 1;
        int xIndex = pair % 8;
        int yIndex = pair / 8;
        int offsetX = xIndex switch
        {
            0 => 24,
            1 => -24,
            2 => 32,
            3 => -32,
            4 => 40,
            5 => -40,
            6 => 48,
            _ => -48,
        };
        int offsetY = yIndex switch
        {
            0 => 0,
            1 => -8,
            2 => 8,
            3 => -16,
            _ => 16,
        };
        return new ReconstructionCandidate(index, offsetX, offsetY, (index & 1) != 0);
    }

    private static ulong NextRevision(ulong revision)
    {
        if (revision == ulong.MaxValue)
            throw new InvalidOperationException("Reconstruction policy revision is exhausted.");
        return revision + 1;
    }

    private static long AllocateOwnerId()
    {
        while (true)
        {
            long current = Volatile.Read(ref _lastOwnerId);
            if (current == long.MaxValue)
                throw new InvalidOperationException("Reconstruction policy owner identity is exhausted.");
            long next = current + 1;
            if (Interlocked.CompareExchange(ref _lastOwnerId, next, current) == current) return next;
        }
    }
}
