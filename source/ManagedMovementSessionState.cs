using System;
using System.Threading;

namespace CoopFeasibilityMod;

public enum ManagedMovementSessionPhase : byte
{
    // Stable replay-wire values. Zero deliberately remains invalid so default snapshots fail closed.
    Dormant = 1,
    WaitingForSafeUpdate = 2,
    Stabilizing = 3,
    ReconstructionPending = 4,
    Active = 5,
    TransitionPending = 6,
    HardFailure = 7,
    Fatal = 8,
    Unloaded = 9,
}

public enum ManagedMovementReconstructionResult : byte
{
    Selected,
    CollisionFault,
    NoSafeCandidate,
    AdapterFault,
}

public enum ManagedMovementRecoveryKind : byte
{
    Tether,
    Collision,
}

public readonly struct ManagedMovementReconstructionContinuation
{
    public readonly ulong Revision;
    internal readonly long OwnerId;

    internal ManagedMovementReconstructionContinuation(long ownerId, ulong revision)
    {
        OwnerId = ownerId;
        Revision = revision;
    }
}

public readonly struct ManagedMovementSessionState
{
    public readonly ulong Revision;
    public readonly ManagedMovementSessionPhase Phase;
    public readonly bool RoomKnown;
    public readonly ManagedRoomKey Room;
    public readonly ManagedRoomKey TransitionOrigin;
    public readonly int SafeUpdates;
    public readonly int RoomStableUpdates;
    public readonly bool TransitionPending;
    public readonly bool ProxyInitialized;
    public readonly bool ManualResetPending;
    public readonly bool ReconstructionHardFailure;
    public readonly bool AwaitingPostTransitionMovement;
    public readonly bool PostTransitionMoved;
    public readonly long PostTransitionCommandedRaw;
    public readonly int ReconstructionAttempts;
    public readonly int ReconstructionSuccesses;
    public readonly int ReconstructionFailures;
    public readonly int TetherRecoveries;
    public readonly int ManualResetRequests;
    public readonly int ManualResetCompletions;
    public readonly int CompletedTransitions;
    public readonly int PassedTransitions;
    public readonly int RoomLayerEvents;
    public readonly int TransitionPendingUpdates;
    public readonly int TransitionPendingMaxUpdates;
    public readonly int PostTransitionAbandonments;
    public readonly int TransitionReconstructionFailures;

    internal ManagedMovementSessionState(ulong revision, ManagedMovementSessionPhase phase,
        bool roomKnown, ManagedRoomKey room, ManagedRoomKey transitionOrigin, int safeUpdates, int roomStableUpdates,
        bool transitionPending, bool proxyInitialized, bool manualResetPending,
        bool reconstructionHardFailure, bool awaitingPostTransitionMovement,
        bool postTransitionMoved, long postTransitionCommandedRaw, int reconstructionAttempts,
        int reconstructionSuccesses, int reconstructionFailures, int tetherRecoveries,
        int manualResetRequests, int manualResetCompletions, int completedTransitions,
        int passedTransitions, int roomLayerEvents, int transitionPendingUpdates,
        int transitionPendingMaxUpdates, int postTransitionAbandonments,
        int transitionReconstructionFailures)
    {
        Revision = revision;
        Phase = phase;
        RoomKnown = roomKnown;
        Room = room;
        TransitionOrigin = transitionOrigin;
        SafeUpdates = safeUpdates;
        RoomStableUpdates = roomStableUpdates;
        TransitionPending = transitionPending;
        ProxyInitialized = proxyInitialized;
        ManualResetPending = manualResetPending;
        ReconstructionHardFailure = reconstructionHardFailure;
        AwaitingPostTransitionMovement = awaitingPostTransitionMovement;
        PostTransitionMoved = postTransitionMoved;
        PostTransitionCommandedRaw = postTransitionCommandedRaw;
        ReconstructionAttempts = reconstructionAttempts;
        ReconstructionSuccesses = reconstructionSuccesses;
        ReconstructionFailures = reconstructionFailures;
        TetherRecoveries = tetherRecoveries;
        ManualResetRequests = manualResetRequests;
        ManualResetCompletions = manualResetCompletions;
        CompletedTransitions = completedTransitions;
        PassedTransitions = passedTransitions;
        RoomLayerEvents = roomLayerEvents;
        TransitionPendingUpdates = transitionPendingUpdates;
        TransitionPendingMaxUpdates = transitionPendingMaxUpdates;
        PostTransitionAbandonments = postTransitionAbandonments;
        TransitionReconstructionFailures = transitionReconstructionFailures;
    }
}

public readonly struct ManagedMovementSessionTransition
{
    public readonly ManagedMovementSessionState State;
    public readonly ManagedMovementReconstructionContinuation Reconstruction;

    internal ManagedMovementSessionTransition(ManagedMovementSessionState state,
        ManagedMovementReconstructionContinuation reconstruction = default)
    {
        State = state;
        Reconstruction = reconstruction;
    }

    public bool ReconstructionRequested => Reconstruction.Revision != 0;
}

public readonly struct ManagedMovementReconstructionCompletion
{
    internal readonly long OwnerId;
    internal readonly ulong ExpectedRevision;
    internal readonly ulong NextRevision;
    internal readonly ManagedMovementReconstructionResult Result;
    internal readonly int Successes;
    internal readonly int Failures;
    internal readonly int ResetCompletions;
    internal readonly int CompletedTransitions;
    internal readonly bool CompleteTransition;
    internal readonly bool ChangedTransition;
    internal readonly int TransitionReconstructionFailures;

    internal ManagedMovementReconstructionCompletion(long ownerId, ulong expectedRevision,
        ulong nextRevision, ManagedMovementReconstructionResult result, int successes, int failures,
        int resetCompletions, int completedTransitions, bool completeTransition,
        bool changedTransition, int transitionReconstructionFailures)
    {
        OwnerId = ownerId;
        ExpectedRevision = expectedRevision;
        NextRevision = nextRevision;
        Result = result;
        Successes = successes;
        Failures = failures;
        ResetCompletions = resetCompletions;
        CompletedTransitions = completedTransitions;
        CompleteTransition = completeTransition;
        ChangedTransition = changedTransition;
        TransitionReconstructionFailures = transitionReconstructionFailures;
    }
}

public readonly struct ManagedMovementDiagnosticResetCommand
{
    internal readonly long OwnerId;
    internal readonly ulong Revision;
    internal readonly ulong NextRevision;

    internal ManagedMovementDiagnosticResetCommand(long ownerId, ulong revision, ulong nextRevision)
    {
        OwnerId = ownerId;
        Revision = revision;
        NextRevision = nextRevision;
    }
}

// Allocation-free owner for the managed proxy's lifecycle. Native safety and room reads stay in
// the caller; this reducer owns only the result of those observations and command authorization.
public sealed class ManagedMovementSessionReducer
{
    public const int StabilizationUpdates = 3;
    public const int TransitionStableUpdates = 30;
    public const long PostTransitionAcceptanceRaw = 8L * 0x10000;

    private static long _lastOwnerId;
    private readonly long _ownerId = AllocateOwnerId();
    private readonly RoomEpochTracker _roomEpoch;
    private ulong _revision = 1;
    private ManagedMovementSessionPhase _phase = ManagedMovementSessionPhase.Dormant;
    private bool _roomKnown;
    private ManagedRoomKey _room;
    private ManagedRoomKey _transitionOrigin;
    private int _safeUpdates;
    private int _roomStableUpdates;
    private bool _transitionPending;
    private bool _proxyInitialized;
    private bool _manualResetPending;
    private bool _reconstructionHardFailure;
    private bool _awaitingPostTransitionMovement;
    private bool _postTransitionMoved;
    private long _postTransitionCommandedRaw;
    private int _reconstructionAttempts;
    private int _reconstructionSuccesses;
    private int _reconstructionFailures;
    private int _tetherRecoveries;
    private int _manualResetRequests;
    private int _manualResetCompletions;
    private int _completedTransitions;
    private int _passedTransitions;
    private int _roomLayerEvents;
    private int _transitionPendingUpdates;
    private int _transitionPendingMaxUpdates;
    private int _postTransitionAbandonments;
    private int _transitionReconstructionFailures;
    private ulong _pendingReconstructionRevision;

    public ManagedMovementSessionReducer(RoomEpochTracker roomEpoch)
    {
        _roomEpoch = roomEpoch ?? throw new ArgumentNullException(nameof(roomEpoch));
    }

    public ManagedMovementSessionState State => new(_revision, _phase, _roomKnown, _room, _transitionOrigin,
        _safeUpdates, _roomStableUpdates, _transitionPending, _proxyInitialized,
        _manualResetPending, _reconstructionHardFailure, _awaitingPostTransitionMovement,
        _postTransitionMoved, _postTransitionCommandedRaw, _reconstructionAttempts,
        _reconstructionSuccesses, _reconstructionFailures, _tetherRecoveries,
        _manualResetRequests, _manualResetCompletions, _completedTransitions,
        _passedTransitions, _roomLayerEvents, _transitionPendingUpdates,
        _transitionPendingMaxUpdates, _postTransitionAbandonments,
        _transitionReconstructionFailures);

    public ulong RoomEpoch => _roomEpoch.Epoch;
    public bool SnapshotEligible => _phase == ManagedMovementSessionPhase.Active &&
        _proxyInitialized && !_transitionPending && _pendingReconstructionRevision == 0;

    public void Load()
    {
        RequirePhase(ManagedMovementSessionPhase.Dormant);
        AdvanceRevision();
        _phase = ManagedMovementSessionPhase.WaitingForSafeUpdate;
    }

    public ManagedMovementSessionTransition ObserveSafeRoom(ManagedRoomKey room)
    {
        RequireOperational();
        if (_pendingReconstructionRevision != 0)
            throw new InvalidOperationException("A reconstruction result is still pending.");
        bool roomChanges = _roomKnown && !_room.SameRoomAs(room);
        int projectedSafe = roomChanges ? 1 : IncrementSaturating(_safeUpdates);
        bool willRequest = (!_proxyInitialized || _manualResetPending) &&
            projectedSafe >= StabilizationUpdates;
        if (willRequest) _ = IncrementChecked(_reconstructionAttempts);
        if (WillCompleteChangedTransition(room, roomChanges, _proxyInitialized))
            _ = IncrementChecked(_completedTransitions);
        if (_transitionPending) _ = IncrementChecked(_transitionPendingUpdates);
        if (roomChanges && _awaitingPostTransitionMovement)
            _ = IncrementChecked(_postTransitionAbandonments);
        if (roomChanges && !_transitionPending && _roomEpoch.Epoch == ulong.MaxValue)
            throw new InvalidOperationException("Room epoch exhausted.");
        AdvanceRevision();
        ObserveRoom(room);
        CountTransitionPendingUpdate();
        _safeUpdates = IncrementSaturating(_safeUpdates);
        CompleteTransitionIfReady();
        if ((!_proxyInitialized || _manualResetPending) && _safeUpdates >= StabilizationUpdates)
        {
            _phase = ManagedMovementSessionPhase.ReconstructionPending;
            _pendingReconstructionRevision = _revision;
            _reconstructionAttempts = IncrementChecked(_reconstructionAttempts);
            return new ManagedMovementSessionTransition(State,
                new ManagedMovementReconstructionContinuation(_ownerId, _revision));
        }
        _phase = _transitionPending ? ManagedMovementSessionPhase.TransitionPending :
            _proxyInitialized ? ManagedMovementSessionPhase.Active : ManagedMovementSessionPhase.Stabilizing;
        return new ManagedMovementSessionTransition(State);
    }

    public void ObserveUnsafe()
    {
        RequireOperational();
        if (_pendingReconstructionRevision != 0)
            throw new InvalidOperationException("A reconstruction result is still pending.");
        if (_transitionPending) _ = IncrementChecked(_transitionPendingUpdates);
        AdvanceRevision();
        CountTransitionPendingUpdate();
        _safeUpdates = 0;
        _phase = ManagedMovementSessionPhase.WaitingForSafeUpdate;
    }

    public void CompleteReconstruction(ManagedMovementReconstructionContinuation continuation,
        ManagedMovementReconstructionResult result)
    {
        ManagedMovementReconstructionCompletion completion =
            PrepareReconstructionCompletion(continuation, result);
        if (!CommitReconstructionCompletion(completion))
            throw new InvalidOperationException("Reconstruction completion became stale.");
    }

    public ManagedMovementReconstructionCompletion PrepareReconstructionCompletion(
        ManagedMovementReconstructionContinuation continuation,
        ManagedMovementReconstructionResult result)
    {
        RequireOperational();
        if (result is < ManagedMovementReconstructionResult.Selected or > ManagedMovementReconstructionResult.AdapterFault)
            throw new ArgumentOutOfRangeException(nameof(result));
        if (_phase != ManagedMovementSessionPhase.ReconstructionPending ||
            _pendingReconstructionRevision == 0 || continuation.OwnerId != _ownerId ||
            continuation.Revision == 0 || continuation.Revision != _pendingReconstructionRevision ||
            continuation.Revision != _revision)
            throw new InvalidOperationException("Reconstruction completion is stale, duplicated, or belongs to another session.");

        ulong nextRevision = NextRevision(_revision);
        int successes = _reconstructionSuccesses;
        int failures = _reconstructionFailures;
        int resetCompletions = _manualResetCompletions;
        int completedTransitions = _completedTransitions;
        int transitionReconstructionFailures = _transitionReconstructionFailures;
        bool completeTransition = result == ManagedMovementReconstructionResult.Selected &&
            _transitionPending && _roomStableUpdates >= TransitionStableUpdates;
        bool changedTransition = completeTransition && !_transitionOrigin.SameRoomAs(_room);
        if (result == ManagedMovementReconstructionResult.Selected)
        {
            successes = IncrementChecked(successes);
            if (_manualResetPending) resetCompletions = IncrementChecked(resetCompletions);
            if (changedTransition) completedTransitions = IncrementChecked(completedTransitions);
        }
        else if (result == ManagedMovementReconstructionResult.NoSafeCandidate)
            failures = IncrementChecked(failures);
        if (result != ManagedMovementReconstructionResult.Selected && _transitionPending)
            transitionReconstructionFailures = IncrementChecked(transitionReconstructionFailures);
        return new ManagedMovementReconstructionCompletion(_ownerId, _revision, nextRevision, result,
            successes, failures, resetCompletions, completedTransitions, completeTransition,
            changedTransition, transitionReconstructionFailures);
    }

    public bool CanCommitReconstructionCompletion(ManagedMovementReconstructionCompletion completion) =>
        completion.OwnerId == _ownerId && completion.ExpectedRevision != 0 &&
        _revision != ulong.MaxValue && completion.ExpectedRevision == _revision &&
        completion.NextRevision == _revision + 1 &&
        _phase == ManagedMovementSessionPhase.ReconstructionPending &&
        _pendingReconstructionRevision == _revision;

    public bool CommitReconstructionCompletion(ManagedMovementReconstructionCompletion completion)
    {
        if (!CanCommitReconstructionCompletion(completion)) return false;
        CommitPreparedReconstructionCompletion(completion);
        return true;
    }

    internal void CommitPreparedReconstructionCompletion(
        ManagedMovementReconstructionCompletion completion)
    {
        _revision = completion.NextRevision;
        _pendingReconstructionRevision = 0;
        _reconstructionSuccesses = completion.Successes;
        _reconstructionFailures = completion.Failures;
        _manualResetCompletions = completion.ResetCompletions;
        _completedTransitions = completion.CompletedTransitions;
        _transitionReconstructionFailures = completion.TransitionReconstructionFailures;
        if (completion.Result == ManagedMovementReconstructionResult.Selected)
        {
            _proxyInitialized = true;
            _reconstructionHardFailure = false;
            if (_manualResetPending)
            {
                _manualResetPending = false;
            }
            if (_awaitingPostTransitionMovement) _postTransitionCommandedRaw = 0;
            if (completion.CompleteTransition)
            {
                _transitionPending = false;
                _transitionPendingUpdates = 0;
                _roomEpoch.Complete(_room);
                if (completion.ChangedTransition)
                {
                    _postTransitionCommandedRaw = 0;
                    _awaitingPostTransitionMovement = true;
                    _postTransitionMoved = false;
                }
            }
            _phase = _transitionPending ? ManagedMovementSessionPhase.TransitionPending :
                ManagedMovementSessionPhase.Active;
            return;
        }

        _proxyInitialized = false;
        if (completion.Result == ManagedMovementReconstructionResult.NoSafeCandidate)
        {
            _reconstructionHardFailure = true;
            _phase = ManagedMovementSessionPhase.HardFailure;
        }
        else _phase = ManagedMovementSessionPhase.Stabilizing;
    }

    public void RequestManualReset()
    {
        RequireOperational();
        int requests = IncrementChecked(_manualResetRequests);
        AdvanceRevision();
        _manualResetRequests = requests;
        _manualResetPending = true;
    }

    public void BeginRecovery(ManagedMovementRecoveryKind kind)
    {
        RequireOperational();
        if (kind is < ManagedMovementRecoveryKind.Tether or > ManagedMovementRecoveryKind.Collision)
            throw new ArgumentOutOfRangeException(nameof(kind));
        int tetherRecoveries = kind == ManagedMovementRecoveryKind.Tether
            ? IncrementChecked(_tetherRecoveries) : _tetherRecoveries;
        AdvanceRevision();
        _tetherRecoveries = tetherRecoveries;
        _proxyInitialized = false;
        _safeUpdates = 0;
        _pendingReconstructionRevision = 0;
        _phase = ManagedMovementSessionPhase.Stabilizing;
    }

    public bool ObservePostTransitionMovement(long acceptedCommandedRaw)
    {
        RequireOperational();
        if (acceptedCommandedRaw < 0) throw new ArgumentOutOfRangeException(nameof(acceptedCommandedRaw));
        if (!_awaitingPostTransitionMovement || acceptedCommandedRaw == 0) return false;
        long commanded = SaturatingAdd(_postTransitionCommandedRaw, acceptedCommandedRaw);
        int passed = commanded >= PostTransitionAcceptanceRaw
            ? IncrementChecked(_passedTransitions) : _passedTransitions;
        AdvanceRevision();
        _postTransitionCommandedRaw = commanded;
        if (_postTransitionCommandedRaw < PostTransitionAcceptanceRaw) return false;
        _postTransitionMoved = true;
        _awaitingPostTransitionMovement = false;
        _passedTransitions = passed;
        return true;
    }

    public void RoomLayerLoaded()
    {
        RequireOperational();
        int events = IncrementChecked(_roomLayerEvents);
        if (_awaitingPostTransitionMovement) _ = IncrementChecked(_postTransitionAbandonments);
        if (_roomKnown && !_transitionPending && _roomEpoch.Epoch == ulong.MaxValue)
            throw new InvalidOperationException("Room epoch exhausted.");
        AdvanceRevision();
        _roomLayerEvents = events;
        if (_roomKnown) BeginTransition();
        InvalidateProxyForTransition();
    }

    public void PlayerReloaded()
    {
        RequireOperational();
        if (_roomEpoch.Epoch == ulong.MaxValue)
            throw new InvalidOperationException("Room epoch exhausted.");
        AdvanceRevision();
        _roomEpoch.InvalidateForPlayerReload();
        _roomKnown = false;
        _room = default;
        _transitionPending = false;
        _transitionPendingUpdates = 0;
        _awaitingPostTransitionMovement = false;
        _postTransitionMoved = false;
        InvalidateProxyForTransition();
        _phase = ManagedMovementSessionPhase.WaitingForSafeUpdate;
    }

    public void DiagnosticReset()
    {
        // Diagnostic reset is the sole authorized escape from Fatal. Dormant has never been
        // loaded, and Unloaded is terminal; both must remain closed.
        ManagedMovementDiagnosticResetCommand command = PrepareDiagnosticReset();
        if (!CommitDiagnosticReset(command))
            throw new InvalidOperationException("Movement diagnostic reset became stale.");
    }

    public ManagedMovementDiagnosticResetCommand PrepareDiagnosticReset()
    {
        ValidateDiagnosticReset();
        return new ManagedMovementDiagnosticResetCommand(_ownerId, _revision, NextRevision(_revision));
    }

    public bool CommitDiagnosticReset(in ManagedMovementDiagnosticResetCommand command)
    {
        if (!CanCommitDiagnosticReset(command)) return false;
        _revision = command.NextRevision;
        _roomEpoch.MarkDiagnosticReset();
        _roomKnown = false;
        _room = default;
        _roomStableUpdates = 0;
        _safeUpdates = 0;
        _transitionPending = false;
        _proxyInitialized = false;
        _manualResetPending = false;
        _reconstructionHardFailure = false;
        _awaitingPostTransitionMovement = false;
        _postTransitionMoved = false;
        _postTransitionCommandedRaw = 0;
        _reconstructionAttempts = _reconstructionSuccesses = _reconstructionFailures = 0;
        _tetherRecoveries = _manualResetRequests = _manualResetCompletions = 0;
        _completedTransitions = _passedTransitions = _roomLayerEvents = 0;
        _transitionPendingUpdates = _transitionPendingMaxUpdates = 0;
        _postTransitionAbandonments = _transitionReconstructionFailures = 0;
        _pendingReconstructionRevision = 0;
        _phase = ManagedMovementSessionPhase.WaitingForSafeUpdate;
        return true;
    }

    public bool CanCommitDiagnosticReset(in ManagedMovementDiagnosticResetCommand command) =>
        command.OwnerId == _ownerId && command.Revision == _revision &&
        command.NextRevision == _revision + 1;

    public void ValidateDiagnosticReset()
    {
        if (_phase is ManagedMovementSessionPhase.Dormant or ManagedMovementSessionPhase.Unloaded)
            throw new InvalidOperationException("Movement session cannot be diagnostically reset in its current phase.");
        if (_revision == ulong.MaxValue)
            throw new InvalidOperationException("Movement session revision is exhausted.");
    }

    public void Fatal()
    {
        if (_phase is ManagedMovementSessionPhase.Fatal or ManagedMovementSessionPhase.Unloaded) return;
        RequireOperational();
        AdvanceRevision();
        _proxyInitialized = false;
        _pendingReconstructionRevision = 0;
        _phase = ManagedMovementSessionPhase.Fatal;
    }

    public void Unload()
    {
        if (_phase == ManagedMovementSessionPhase.Unloaded) return;
        AdvanceRevision();
        _proxyInitialized = false;
        _pendingReconstructionRevision = 0;
        _phase = ManagedMovementSessionPhase.Unloaded;
    }

    private void ObserveRoom(ManagedRoomKey room)
    {
        if (!_roomKnown)
        {
            _room = room;
            _roomEpoch.ReconcileAfterDiagnosticReset(room);
            _roomKnown = true;
            _roomStableUpdates = 1;
            return;
        }
        if (!_room.SameRoomAs(room))
        {
            BeginTransition();
            _room = room;
            _roomEpoch.Observe(room);
            _roomStableUpdates = 1;
            _safeUpdates = 0;
            _proxyInitialized = false;
            _pendingReconstructionRevision = 0;
            return;
        }
        if (!_room.Equals(room))
        {
            // Bounds churn within one room (door scroll, camera drift) is not a transition, but
            // reconstruction must still wait for settled terrain before selecting a candidate.
            _room = room;
            _safeUpdates = 0;
        }
        _roomStableUpdates = IncrementSaturating(_roomStableUpdates);
    }

    private void BeginTransition()
    {
        if (!_roomKnown)
        {
            if (!_roomEpoch.Known) return;
            if (!_transitionPending)
            {
                _transitionOrigin = _room;
                _roomEpoch.BeginTransition();
            }
            _transitionPending = true;
            return;
        }
        if (_awaitingPostTransitionMovement)
        {
            _postTransitionAbandonments = IncrementChecked(_postTransitionAbandonments);
            _awaitingPostTransitionMovement = false;
            _postTransitionMoved = false;
        }
        if (!_transitionPending)
        {
            _transitionOrigin = _room;
            _roomEpoch.BeginTransition();
        }
        _transitionPending = true;
    }

    private void CompleteTransitionIfReady()
    {
        if (!_transitionPending || _roomStableUpdates < TransitionStableUpdates || !_proxyInitialized) return;
        _transitionPending = false;
        _transitionPendingUpdates = 0;
        _roomEpoch.Complete(_room);
        if (_transitionOrigin.SameRoomAs(_room)) return;
        _completedTransitions = IncrementChecked(_completedTransitions);
        _postTransitionCommandedRaw = 0;
        _awaitingPostTransitionMovement = true;
        _postTransitionMoved = false;
    }

    private bool WillCompleteChangedTransition(ManagedRoomKey observedRoom, bool roomChanges,
        bool proxyInitialized)
    {
        if (!_transitionPending || !proxyInitialized) return false;
        int stable = roomChanges ? 1 : IncrementSaturating(_roomStableUpdates);
        return stable >= TransitionStableUpdates && !_transitionOrigin.SameRoomAs(observedRoom);
    }

    private void InvalidateProxyForTransition()
    {
        _proxyInitialized = false;
        _safeUpdates = 0;
        _roomStableUpdates = 0;
        _pendingReconstructionRevision = 0;
        _phase = ManagedMovementSessionPhase.TransitionPending;
    }

    private void CountTransitionPendingUpdate()
    {
        if (!_transitionPending) return;
        _transitionPendingUpdates = IncrementChecked(_transitionPendingUpdates);
        if (_transitionPendingUpdates > _transitionPendingMaxUpdates)
            _transitionPendingMaxUpdates = _transitionPendingUpdates;
    }

    private void RequireOperational()
    {
        if (_phase is ManagedMovementSessionPhase.Dormant or ManagedMovementSessionPhase.Fatal or
            ManagedMovementSessionPhase.Unloaded)
            throw new InvalidOperationException("Movement session is not operational.");
    }

    private void RequirePhase(ManagedMovementSessionPhase phase)
    {
        if (_phase != phase) throw new InvalidOperationException($"Movement session phase must be {phase}.");
    }

    private void AdvanceRevision()
    {
        if (_revision == ulong.MaxValue)
            throw new InvalidOperationException("Movement session revision is exhausted.");
        _revision++;
    }

    private static ulong NextRevision(ulong revision)
    {
        if (revision == ulong.MaxValue)
            throw new InvalidOperationException("Movement session revision is exhausted.");
        return revision + 1;
    }

    private static int IncrementChecked(int value) => checked(value + 1);
    private static int IncrementSaturating(int value) => value == int.MaxValue ? value : value + 1;
    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right
        ? long.MaxValue : left + right;

    private static long AllocateOwnerId()
    {
        while (true)
        {
            long current = Volatile.Read(ref _lastOwnerId);
            if (current == long.MaxValue)
                throw new InvalidOperationException("Movement session owner identity is exhausted.");
            long next = current + 1;
            if (Interlocked.CompareExchange(ref _lastOwnerId, next, current) == current) return next;
        }
    }
}
