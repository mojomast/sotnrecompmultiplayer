using System;
using System.Threading;

namespace CoopFeasibilityMod;

public enum JumpForgivenessPhase : byte
{
    PrePhysics,
    PostPhysics,
}

public enum JumpForgivenessRequest : byte
{
    None,
    Normal,
    Coyote,
    Buffered,
}

public readonly struct JumpForgivenessState
{
    public readonly ulong Revision;
    public readonly int CoyoteUpdates;
    public readonly int BufferUpdates;
    public readonly JumpForgivenessPhase Phase;

    internal JumpForgivenessState(ulong revision, int coyoteUpdates, int bufferUpdates,
        JumpForgivenessPhase phase)
    {
        Revision = revision;
        CoyoteUpdates = coyoteUpdates;
        BufferUpdates = bufferUpdates;
        Phase = phase;
    }
}

public readonly struct JumpForgivenessContinuation
{
    internal readonly long OwnerId;
    public readonly ulong Revision;

    internal JumpForgivenessContinuation(long ownerId, ulong revision)
    {
        OwnerId = ownerId;
        Revision = revision;
    }
}

public readonly struct JumpForgivenessTransition
{
    public readonly JumpForgivenessState State;
    public readonly JumpForgivenessRequest Request;
    public readonly JumpForgivenessContinuation Continuation;

    internal JumpForgivenessTransition(JumpForgivenessState state, JumpForgivenessRequest request,
        JumpForgivenessContinuation continuation = default)
    {
        State = state;
        Request = request;
        Continuation = continuation;
    }
}

public readonly struct JumpForgivenessClearPreparation
{
    internal readonly long OwnerId;
    internal readonly ulong ExpectedRevision;
    internal readonly ulong NextRevision;
    internal JumpForgivenessClearPreparation(long ownerId, ulong expectedRevision, ulong nextRevision)
    {
        OwnerId = ownerId;
        ExpectedRevision = expectedRevision;
        NextRevision = nextRevision;
    }
}

// This reducer owns the authoritative phase and revision. State snapshots and continuation tokens are
// values, but only the reducer that issued a token can consume it, exactly once.
public sealed class JumpForgivenessReducer
{
    public const int CoyoteWindowUpdates = 4;
    public const int BufferWindowUpdates = 4;

    private static long _lastOwnerId;

    private readonly long _ownerId = AllocateOwnerId();
    private ulong _revision = 1;
    private int _coyoteUpdates;
    private int _bufferUpdates;
    private JumpForgivenessPhase _phase;
    private bool _wasGrounded;
    private bool _jumpStarted;

    public JumpForgivenessState State => new(_revision, _coyoteUpdates, _bufferUpdates, _phase);
    public int CoyoteUpdates => _coyoteUpdates;
    public int BufferUpdates => _bufferUpdates;

    public JumpForgivenessState Clear()
    {
        JumpForgivenessClearPreparation preparation = PrepareClear();
        if (!CommitClear(preparation))
            throw new InvalidOperationException("Jump forgiveness clear became stale.");
        return State;
    }

    public JumpForgivenessClearPreparation PrepareClear()
    {
        ValidateReady(_phase);
        return new JumpForgivenessClearPreparation(_ownerId, _revision, NextRevision(_revision));
    }

    public bool CanCommitClear(JumpForgivenessClearPreparation preparation) =>
        preparation.OwnerId == _ownerId && preparation.ExpectedRevision != 0 &&
        _revision != ulong.MaxValue && preparation.ExpectedRevision == _revision &&
        preparation.NextRevision == _revision + 1;

    public bool CommitClear(JumpForgivenessClearPreparation preparation)
    {
        if (!CanCommitClear(preparation)) return false;
        CommitPreparedClear(preparation);
        return true;
    }

    internal void CommitPreparedClear(JumpForgivenessClearPreparation preparation)
    {
        _revision = preparation.NextRevision;
        _coyoteUpdates = 0;
        _bufferUpdates = 0;
        _phase = JumpForgivenessPhase.PrePhysics;
        _wasGrounded = false;
        _jumpStarted = false;
    }

    public JumpForgivenessTransition BeginUpdate(bool jumpTapped, bool grounded, bool crouched)
    {
        ValidateReady(JumpForgivenessPhase.PrePhysics);
        _revision = NextRevision(_revision);
        if (jumpTapped) _bufferUpdates = BufferWindowUpdates;
        JumpForgivenessRequest request = JumpForgivenessRequest.None;
        _jumpStarted = false;

        // Grounded wins when both windows are live. Crouching suppresses only this pre-physics path;
        // the historical post-physics buffered landing edge is deliberately preserved below.
        if (!crouched && _bufferUpdates > 0 && (grounded || _coyoteUpdates > 0))
        {
            request = grounded ? JumpForgivenessRequest.Normal : JumpForgivenessRequest.Coyote;
            _coyoteUpdates = 0;
            _bufferUpdates = 0;
            _jumpStarted = true;
        }

        _wasGrounded = grounded;
        _phase = JumpForgivenessPhase.PostPhysics;
        var continuation = new JumpForgivenessContinuation(_ownerId, _revision);
        return new JumpForgivenessTransition(State, request, continuation);
    }

    public JumpForgivenessTransition CompleteUpdate(JumpForgivenessContinuation continuation,
        bool grounded)
    {
        ValidateReady(JumpForgivenessPhase.PostPhysics);
        if (continuation.OwnerId != _ownerId || continuation.Revision == 0 ||
            continuation.Revision != _revision)
            throw new InvalidOperationException("Jump forgiveness continuation is stale or invalid.");

        bool coyoteRefreshed = false;
        JumpForgivenessRequest request = JumpForgivenessRequest.None;
        if (!_jumpStarted && _wasGrounded && !grounded)
        {
            _coyoteUpdates = CoyoteWindowUpdates;
            coyoteRefreshed = true;
        }

        // This intentionally has no crouch predicate: current behavior consumes a buffered jump
        // after the grounded refresh even when crouching suppressed it before physics.
        if (!_jumpStarted && grounded && _bufferUpdates > 0)
        {
            request = JumpForgivenessRequest.Buffered;
            _coyoteUpdates = 0;
            _bufferUpdates = 0;
            _jumpStarted = true;
        }

        if (!_jumpStarted && !coyoteRefreshed && !grounded && _coyoteUpdates > 0) _coyoteUpdates--;
        if (!_jumpStarted && _bufferUpdates > 0) _bufferUpdates--;

        _phase = JumpForgivenessPhase.PrePhysics;
        _wasGrounded = false;
        _jumpStarted = false;
        return new JumpForgivenessTransition(State, request);
    }

    private void ValidateReady(JumpForgivenessPhase expected)
    {
        if (_revision == 0) throw new InvalidOperationException("Jump forgiveness revision is invalid.");
        if (_phase != expected)
            throw new InvalidOperationException($"Jump forgiveness expected {expected}, got {_phase}.");
        if (_coyoteUpdates is < 0 or > CoyoteWindowUpdates ||
            _bufferUpdates is < 0 or > BufferWindowUpdates)
            throw new InvalidOperationException("Jump forgiveness timers are outside their windows.");
    }

    private static ulong NextRevision(ulong revision)
    {
        if (revision == ulong.MaxValue)
            throw new InvalidOperationException("Jump forgiveness revision is exhausted.");
        return revision + 1;
    }

    private static long AllocateOwnerId()
    {
        while (true)
        {
            long current = Volatile.Read(ref _lastOwnerId);
            if (current == long.MaxValue)
                throw new InvalidOperationException("Jump forgiveness owner identity is exhausted.");
            long next = current + 1;
            if (Interlocked.CompareExchange(ref _lastOwnerId, next, current) == current) return next;
        }
    }
}
