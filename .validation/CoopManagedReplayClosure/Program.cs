using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("session phase wire ordinals are explicit stable bytes", () =>
    {
        Equal((byte)1, (byte)ManagedMovementSessionPhase.Dormant);
        Equal((byte)2, (byte)ManagedMovementSessionPhase.WaitingForSafeUpdate);
        Equal((byte)3, (byte)ManagedMovementSessionPhase.Stabilizing);
        Equal((byte)4, (byte)ManagedMovementSessionPhase.ReconstructionPending);
        Equal((byte)5, (byte)ManagedMovementSessionPhase.Active);
        Equal((byte)6, (byte)ManagedMovementSessionPhase.TransitionPending);
        Equal((byte)7, (byte)ManagedMovementSessionPhase.HardFailure);
        Equal((byte)8, (byte)ManagedMovementSessionPhase.Fatal);
        Equal((byte)9, (byte)ManagedMovementSessionPhase.Unloaded);
    }),
    ("canonical phase identity is stable and distinct", () =>
    {
        ManagedRoomKey room = Room(1);
        var input = new ManagedInputFrame(1, 1, 0, 0, true);
        ManagedProxySnapshot active = Snapshot(input, ManagedMovementSessionPhase.Active, room);
        ManagedProxySnapshot transition = Snapshot(input, ManagedMovementSessionPhase.TransitionPending, room);
        byte[] activeBytes = ManagedStateCodec.WriteCanonical(input, active);
        byte[] transitionBytes = ManagedStateCodec.WriteCanonical(input, transition);
        Equal(116, activeBytes.Length);
        Equal((byte)2, activeBytes[19]);
        Equal((byte)5, activeBytes[57]);
        Equal((byte)6, transitionBytes[57]);
        True(!activeBytes.AsSpan().SequenceEqual(transitionBytes));
        True(ManagedStateCodec.Hash(input, active) != ManagedStateCodec.Hash(input, transition));
    }),
    ("full reducer replay matches every state snapshot byte and hash", () =>
    {
        ReplayCheckpoint[] first = new IntegratedReplay().Run(perturbCrouchInput: false);
        ReplayCheckpoint[] second = new IntegratedReplay().Run(perturbCrouchInput: false);
        Equal(IntegratedReplay.FrameCount, first.Length);
        Equal(first.Length, second.Length);
        int snapshots = 0;
        for (int index = 0; index < first.Length; index++)
        {
            EqualCheckpoint(first[index], second[index], index);
            if (first[index].HasSnapshot) snapshots++;
        }
        Equal(308, snapshots);
        Equal(ManagedMovementSessionPhase.Active, first[^1].Session.Phase);
        Equal(2UL, first[^1].Input.RoomEpoch);
        Equal(2, first[^1].Session.ReconstructionSuccesses);
        Equal(1, first[^1].Session.CompletedTransitions);
        Equal(1, first[^1].Session.PassedTransitions);
        Equal(1, first[^1].Health.DownedCount);
        Equal(1, first[^1].Health.Revives);
        Equal(7, first[^1].Locomotion.AttackPhaseCompletionMask);
        Console.WriteLine($"EVIDENCE frames={first.Length} snapshots={snapshots} final={first[^1].Hash:x16}");
    }),
    ("one processed input perturbation proves state snapshot and hash divergence", () =>
    {
        ReplayCheckpoint[] baseline = new IntegratedReplay().Run(perturbCrouchInput: false);
        ReplayCheckpoint[] changed = new IntegratedReplay().Run(perturbCrouchInput: true);
        for (int index = 0; index < 3; index++) EqualCheckpoint(baseline[index], changed[index], index);
        ReplayCheckpoint expected = baseline[3];
        ReplayCheckpoint actual = changed[3];
        True(expected.Stance.Crouched);
        True(!actual.Stance.Crouched);
        True(expected.HasSnapshot && actual.HasSnapshot);
        True(expected.Hash != actual.Hash);
        Span<byte> expectedBytes = stackalloc byte[ManagedStateCodec.CanonicalLength];
        Span<byte> actualBytes = stackalloc byte[ManagedStateCodec.CanonicalLength];
        ManagedStateCodec.WriteCanonical(expected.Input, expected.Snapshot, expectedBytes);
        ManagedStateCodec.WriteCanonical(actual.Input, actual.Snapshot, actualBytes);
        True(!expectedBytes.SequenceEqual(actualBytes));
        Console.WriteLine($"EVIDENCE divergence-frame=4 baseline={expected.Hash:x16} perturbed={actual.Hash:x16}");
    }),
    ("warmed complete reducer reconstruction and live hash path allocates nothing", () =>
    {
        ManagedRoomKey room = Room(1);
        var session = new ManagedMovementSessionReducer(new RoomEpochTracker());
        session.Load();
        var policy = new ReconstructionPolicyReducer();
        var jump = new JumpForgivenessReducer();
        var stance = new ManagedStanceReducer();
        var locomotion = new ManagedLocomotionReducer();
        ManagedHealthState health = ManagedHealthMachine.Reset();
        InitializeActive(session, policy, jump, stance, locomotion, ref health, room);
        ulong digest = 0;
        for (int index = 1; index <= 1_000; index++)
            digest ^= AllocationCycle(session, policy, jump, stance, locomotion, ref health, room, index);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 1; index <= 50_000; index++)
            digest ^= AllocationCycle(session, policy, jump, stance, locomotion, ref health, room, index);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated);
        GC.KeepAlive(digest);
        Console.WriteLine($"EVIDENCE allocation-iterations=50000 bytes={allocated}");
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopManagedReplayClosure: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static ManagedProxySnapshot Snapshot(ManagedInputFrame input, ManagedMovementSessionPhase phase,
    ManagedRoomKey room) => new(input.UpdateId, input.RoomEpoch, phase, room, 0x180000, 0x200000,
    0, 0, true, true, false, false, false, 0, 0,
    (byte)ManagedLocomotion.Idle, (byte)ManagedAnimation.Idle, 0, 0);

static void InitializeActive(ManagedMovementSessionReducer session, ReconstructionPolicyReducer policy,
    JumpForgivenessReducer jump, ManagedStanceReducer stance, ManagedLocomotionReducer locomotion,
    ref ManagedHealthState health, ManagedRoomKey room)
{
    ManagedMovementSessionTransition transition = default;
    while (!transition.ReconstructionRequested) transition = session.ObserveSafeRoom(room);
    CompleteReconstruction(session, transition, policy, jump, stance, locomotion, ref health, 0);
}

static ulong AllocationCycle(ManagedMovementSessionReducer session, ReconstructionPolicyReducer policy,
    JumpForgivenessReducer jump, ManagedStanceReducer stance, ManagedLocomotionReducer locomotion,
    ref ManagedHealthState health, ManagedRoomKey room, int index)
{
    session.BeginRecovery(ManagedMovementRecoveryKind.Collision);
    session.ObserveSafeRoom(room);
    session.ObserveSafeRoom(room);
    ManagedMovementSessionTransition transition = session.ObserveSafeRoom(room);
    CompleteReconstruction(session, transition, policy, jump, stance, locomotion, ref health, 0);
    JumpForgivenessTransition begin = jump.BeginUpdate(false, true, false);
    jump.CompleteUpdate(begin.Continuation, true);
    stance.Observe(true, true, false);
    health = ManagedHealthMachine.AdvanceTimers(health, true, true);
    ManagedLocomotionState pose = locomotion.Update(new ManagedLocomotionObservation(false, false,
        false, 0, false, true, false, 0, 0, false));
    var input = new ManagedInputFrame(index, session.RoomEpoch, 0, 0, true);
    var snapshot = new ManagedProxySnapshot(index, session.RoomEpoch, session.State.Phase, room,
        index, -index, 0, 0, true, true, false, stance.Crouched, stance.StandBlocked,
        jump.CoyoteUpdates, jump.BufferUpdates, (byte)pose.Locomotion, (byte)pose.Animation,
        pose.Frame, pose.Tick);
    return ManagedStateCodec.Hash(input, snapshot);
}

static void CompleteReconstruction(ManagedMovementSessionReducer session,
    ManagedMovementSessionTransition transition, ReconstructionPolicyReducer policy,
    JumpForgivenessReducer jump, ManagedStanceReducer stance, ManagedLocomotionReducer locomotion,
    ref ManagedHealthState health, int selectedIndex)
{
    if (!transition.ReconstructionRequested) throw new InvalidOperationException("Reconstruction was not requested.");
    var adapter = new ReplayReconstructionAdapter(selectedIndex);
    Equal(ReconstructionRunResult.Selected,
        ReconstructionPolicyOrchestration.Run(policy, 100, 200, ref adapter));
    session.CompleteReconstruction(transition.Reconstruction, ManagedMovementReconstructionResult.Selected);
    stance.Initialize(adapter.SelectedCrouched);
    jump.Clear();
    locomotion.Initialize();
    health = ManagedHealthMachine.Reconstructed(health);
}

static void EqualCheckpoint(ReplayCheckpoint expected, ReplayCheckpoint actual, int index)
{
    try
    {
        Equal(expected.Input.UpdateId, actual.Input.UpdateId);
        Equal(expected.Input.RoomEpoch, actual.Input.RoomEpoch);
        Equal(expected.Input.Pressed, actual.Input.Pressed);
        Equal(expected.Input.Tapped, actual.Input.Tapped);
        Equal(expected.Input.CanControl, actual.Input.CanControl);
        EqualSession(expected.Session, actual.Session);
        Equal(expected.Jump.Revision, actual.Jump.Revision);
        Equal(expected.Jump.CoyoteUpdates, actual.Jump.CoyoteUpdates);
        Equal(expected.Jump.BufferUpdates, actual.Jump.BufferUpdates);
        Equal(expected.Jump.Phase, actual.Jump.Phase);
        Equal(expected.Stance.Revision, actual.Stance.Revision);
        Equal(expected.Stance.Crouched, actual.Stance.Crouched);
        Equal(expected.Stance.StandBlocked, actual.Stance.StandBlocked);
        EqualLocomotion(expected.Locomotion, actual.Locomotion);
        EqualHealth(expected.Health, actual.Health);
        Equal(expected.Reconstruction.Revision, actual.Reconstruction.Revision);
        Equal(expected.Reconstruction.Phase, actual.Reconstruction.Phase);
        Equal(expected.Reconstruction.CandidateIndex, actual.Reconstruction.CandidateIndex);
        Equal(expected.HasSnapshot, actual.HasSnapshot);
        Equal(expected.Hash, actual.Hash);
        if (!expected.HasSnapshot) return;
        EqualSnapshot(expected.Snapshot, actual.Snapshot);
        Span<byte> expectedBytes = stackalloc byte[ManagedStateCodec.CanonicalLength];
        Span<byte> actualBytes = stackalloc byte[ManagedStateCodec.CanonicalLength];
        ManagedStateCodec.WriteCanonical(expected.Input, expected.Snapshot, expectedBytes);
        ManagedStateCodec.WriteCanonical(actual.Input, actual.Snapshot, actualBytes);
        True(expectedBytes.SequenceEqual(actualBytes));
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Replay checkpoint {index + 1} differs: {ex.Message}", ex);
    }
}

static void EqualSession(ManagedMovementSessionState a, ManagedMovementSessionState b)
{
    Equal(a.Revision, b.Revision); Equal(a.Phase, b.Phase); Equal(a.RoomKnown, b.RoomKnown);
    EqualRoom(a.Room, b.Room); Equal(a.SafeUpdates, b.SafeUpdates); Equal(a.RoomStableUpdates, b.RoomStableUpdates);
    Equal(a.TransitionPending, b.TransitionPending); Equal(a.ProxyInitialized, b.ProxyInitialized);
    Equal(a.ManualResetPending, b.ManualResetPending); Equal(a.ReconstructionHardFailure, b.ReconstructionHardFailure);
    Equal(a.AwaitingPostTransitionMovement, b.AwaitingPostTransitionMovement);
    Equal(a.PostTransitionMoved, b.PostTransitionMoved); Equal(a.PostTransitionCommandedRaw, b.PostTransitionCommandedRaw);
    Equal(a.ReconstructionAttempts, b.ReconstructionAttempts); Equal(a.ReconstructionSuccesses, b.ReconstructionSuccesses);
    Equal(a.ReconstructionFailures, b.ReconstructionFailures); Equal(a.TetherRecoveries, b.TetherRecoveries);
    Equal(a.ManualResetRequests, b.ManualResetRequests); Equal(a.ManualResetCompletions, b.ManualResetCompletions);
    Equal(a.CompletedTransitions, b.CompletedTransitions); Equal(a.PassedTransitions, b.PassedTransitions);
    Equal(a.RoomLayerEvents, b.RoomLayerEvents);
}

static void EqualLocomotion(ManagedLocomotionState a, ManagedLocomotionState b)
{
    Equal(a.Valid, b.Valid); Equal(a.Locomotion, b.Locomotion); Equal(a.Animation, b.Animation);
    Equal(a.Frame, b.Frame); Equal(a.Tick, b.Tick); Equal(a.Transitions, b.Transitions);
    Equal(a.Advances, b.Advances); Equal(a.StatesSeen, b.StatesSeen);
    Equal(a.AdvanceStatesSeen, b.AdvanceStatesSeen); Equal(a.AttackPhaseCompletionMask, b.AttackPhaseCompletionMask);
}

static void EqualHealth(ManagedHealthState a, ManagedHealthState b)
{
    Equal(a.Hp, b.Hp); Equal(a.Invulnerability, b.Invulnerability); Equal(a.HurtLock, b.HurtLock);
    Equal(a.Downed, b.Downed); Equal(a.DamageEvents, b.DamageEvents); Equal(a.DamageConsumed, b.DamageConsumed);
    Equal(a.SuppressedInvulnerability, b.SuppressedInvulnerability);
    Equal(a.SuppressedHitInvulnerability, b.SuppressedHitInvulnerability);
    Equal(a.HitInvulnerabilityActive, b.HitInvulnerabilityActive); Equal(a.DownedCount, b.DownedCount);
    Equal(a.ReviveStarts, b.ReviveStarts); Equal(a.ReviveCancels, b.ReviveCancels);
    Equal(a.ReviveRecoveries, b.ReviveRecoveries); Equal(a.InvariantFailures, b.InvariantFailures);
    Equal(a.CompactHurt, b.CompactHurt); Equal(a.LastDamage, b.LastDamage);
    Equal(a.LastDamageSlot, b.LastDamageSlot); Equal(a.LastDamageElement, b.LastDamageElement);
    Equal(a.ReviveProgress, b.ReviveProgress); Equal(a.Revives, b.Revives);
}

static void EqualSnapshot(ManagedProxySnapshot a, ManagedProxySnapshot b)
{
    Equal(a.UpdateId, b.UpdateId); Equal(a.RoomEpoch, b.RoomEpoch); Equal(a.SessionPhase, b.SessionPhase);
    EqualRoom(a.Room, b.Room); Equal(a.X, b.X); Equal(a.Y, b.Y); Equal(a.VelocityX, b.VelocityX);
    Equal(a.VelocityY, b.VelocityY); Equal(a.Initialized, b.Initialized); Equal(a.Grounded, b.Grounded);
    Equal(a.FacingLeft, b.FacingLeft); Equal(a.Crouched, b.Crouched); Equal(a.StandBlocked, b.StandBlocked);
    Equal(a.CoyoteUpdates, b.CoyoteUpdates); Equal(a.JumpBufferUpdates, b.JumpBufferUpdates);
    Equal(a.Locomotion, b.Locomotion); Equal(a.Animation, b.Animation);
    Equal(a.AnimationFrame, b.AnimationFrame); Equal(a.AnimationTick, b.AnimationTick);
}

static void EqualRoom(ManagedRoomKey a, ManagedRoomKey b)
{
    Equal(a.Stage, b.Stage); Equal(a.Room, b.Room); Equal(a.Area, b.Area); Equal(a.Left, b.Left);
    Equal(a.Top, b.Top); Equal(a.Right, b.Right); Equal(a.Bottom, b.Bottom);
}

static ManagedRoomKey Room(byte room) => new(1, room, 0, 0, 0, 256, 240);
static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

readonly struct ReplayCheckpoint
{
    public readonly ManagedInputFrame Input;
    public readonly ManagedMovementSessionState Session;
    public readonly JumpForgivenessState Jump;
    public readonly ManagedStanceState Stance;
    public readonly ManagedLocomotionState Locomotion;
    public readonly ManagedHealthState Health;
    public readonly ReconstructionPolicyState Reconstruction;
    public readonly bool HasSnapshot;
    public readonly ManagedProxySnapshot Snapshot;
    public readonly ulong Hash;

    public ReplayCheckpoint(ManagedInputFrame input, ManagedMovementSessionState session,
        JumpForgivenessState jump, ManagedStanceState stance, ManagedLocomotionState locomotion,
        ManagedHealthState health, ReconstructionPolicyState reconstruction, bool hasSnapshot,
        ManagedProxySnapshot snapshot, ulong hash)
    {
        Input = input; Session = session; Jump = jump; Stance = stance; Locomotion = locomotion;
        Health = health; Reconstruction = reconstruction; HasSnapshot = hasSnapshot;
        Snapshot = snapshot; Hash = hash;
    }
}

sealed class IntegratedReplay
{
    public const int FrameCount = 339;
    private readonly ManagedMovementSessionReducer _session = new(new RoomEpochTracker());
    private readonly ReconstructionPolicyReducer _policy = new();
    private readonly JumpForgivenessReducer _jump = new();
    private readonly ManagedStanceReducer _stance = new();
    private readonly ManagedLocomotionReducer _locomotion = new();
    private ManagedHealthState _health = ManagedHealthMachine.Reset();
    private int _x = 100 * 0x10000;
    private int _y = 120 * 0x10000;
    private int _velocityX;
    private int _velocityY;
    private bool _grounded = true;
    private int _attackTimer;

    public IntegratedReplay() => _session.Load();

    public ReplayCheckpoint[] Run(bool perturbCrouchInput)
    {
        var result = new ReplayCheckpoint[FrameCount];
        for (int frame = 1; frame <= FrameCount; frame++) result[frame - 1] = Update(frame, perturbCrouchInput);
        return result;
    }

    private ReplayCheckpoint Update(int frame, bool perturbCrouchInput)
    {
        if (frame == 308)
        {
            _session.RoomLayerLoaded();
            _jump.Clear();
            _stance.Initialize(false);
            _locomotion.Invalidate();
        }
        ManagedRoomKey room = new(1, frame >= 308 ? (byte)2 : (byte)1, 0, 0, 0, 256, 240);
        ManagedMovementSessionTransition lifecycle = _session.ObserveSafeRoom(room);
        if (lifecycle.ReconstructionRequested) Reconstruct(lifecycle, frame < 308 ? 2 : 5);

        ushort pressed = 0;
        ushort tapped = 0;
        if (frame == 4 && !perturbCrouchInput) pressed |= ReplayButtons.Down;
        if (frame is >= 13 and <= 25 || frame == 338) pressed |= ReplayButtons.Right;
        if (frame is 7 or 9 or 10) tapped |= ReplayButtons.Jump;
        var input = new ManagedInputFrame(frame, _session.RoomEpoch, pressed, tapped, true);

        if (_session.SnapshotEligible) UpdateActive(frame, input);

        bool hasSnapshot = _session.SnapshotEligible;
        ManagedProxySnapshot snapshot = default;
        ulong hash = 0;
        if (hasSnapshot)
        {
            ManagedLocomotionState pose = _locomotion.State;
            snapshot = new ManagedProxySnapshot(frame, _session.RoomEpoch, _session.State.Phase, room,
                _x, _y, _velocityX, _velocityY, true, _grounded, false, _stance.Crouched,
                _stance.StandBlocked, _jump.CoyoteUpdates, _jump.BufferUpdates,
                (byte)pose.Locomotion, (byte)pose.Animation, pose.Frame, pose.Tick);
            hash = ManagedStateCodec.Hash(input, snapshot);
        }
        return new ReplayCheckpoint(input, _session.State, _jump.State, _stance.State,
            _locomotion.State, _health, _policy.State, hasSnapshot, snapshot, hash);
    }

    private void Reconstruct(ManagedMovementSessionTransition transition, int selectedIndex)
    {
        var adapter = new ReplayReconstructionAdapter(selectedIndex);
        if (ReconstructionPolicyOrchestration.Run(_policy, 100, 200, ref adapter) !=
            ReconstructionRunResult.Selected)
            throw new InvalidOperationException("Replay reconstruction did not select a candidate.");
        _session.CompleteReconstruction(transition.Reconstruction, ManagedMovementReconstructionResult.Selected);
        _stance.Initialize(adapter.SelectedCrouched);
        _jump.Clear();
        _locomotion.Initialize();
        _health = ManagedHealthMachine.Reconstructed(_health);
    }

    private void UpdateActive(int frame, ManagedInputFrame input)
    {
        bool beforeGrounded = frame switch { 7 => true, 8 => true, 9 or 10 or 11 => false, _ => true };
        bool afterGrounded = frame switch { 7 or 8 or 9 or 10 => false, 11 => true, _ => true };
        ManagedStanceTransition stance = _stance.Observe(!_health.Downed, beforeGrounded,
            (input.Pressed & ReplayButtons.Down) != 0);
        if (stance.Command.Kind == ManagedStanceCommandKind.ProbeStandingHull)
            _stance.CompleteStandingProbe(stance.Command, clear: frame != 5);

        JumpForgivenessTransition jump = _jump.BeginUpdate((input.Tapped & ReplayButtons.Jump) != 0,
            beforeGrounded, _stance.Crouched);
        if (jump.Request != JumpForgivenessRequest.None)
        {
            afterGrounded = false;
            _velocityY = -0x48000;
        }
        JumpForgivenessTransition completed = _jump.CompleteUpdate(jump.Continuation, afterGrounded);
        if (completed.Request != JumpForgivenessRequest.None)
        {
            afterGrounded = false;
            _velocityY = -0x48000;
        }
        _grounded = afterGrounded;
        if (_grounded) _velocityY = 0;
        else if (_velocityY < 0) _velocityY += 0x10000;

        _velocityX = (input.Pressed & ReplayButtons.Right) != 0 ? 0x18000 : 0;
        _x = checked(_x + _velocityX);
        _y = checked(_y + _velocityY);
        if (frame == 338)
            _session.ObservePostTransitionMovement(ManagedMovementSessionReducer.PostTransitionAcceptanceRaw);

        _health = ManagedHealthMachine.AdvanceTimers(_health, true, true);
        if (frame is 65 or 125)
            _health = ManagedHealthMachine.ApplyIncomingHit(_health, 40, 3, 0x20, _stance.Crouched).State;
        if (frame == 185)
        {
            ManagedDamageTransition damage = ManagedHealthMachine.ApplyIncomingHit(_health, 100, 4, 0x40,
                _stance.Crouched);
            _health = damage.State;
            if (damage.Lethal) _stance.ApplyLethalDamage();
        }
        bool wasDowned = _health.Downed;
        if (frame is >= 186 and <= 305)
            _health = ManagedHealthMachine.ApplyRevive(_health, new ManagedReviveObservation(true,
                true, true, 0, 0, true, true, true));
        else _health = ManagedHealthMachine.ApplyRevive(_health, false);
        if (wasDowned && !_health.Downed) _locomotion.Initialize();
        _health = ManagedHealthMachine.Validate(_health);

        if (frame == 35) _attackTimer = ManagedLocomotionCatalog.AttackTotalUpdates;
        bool landed = frame == 11;
        ManagedLocomotionState pose = _locomotion.Update(new ManagedLocomotionObservation(
            _health.Downed, _health.HurtLock > 0, _health.CompactHurt, _attackTimer,
            _stance.Crouched, _grounded, (input.Pressed & ReplayButtons.Right) != 0, _velocityX,
            _velocityY, landed));
        if (_attackTimer > 0) _attackTimer = _locomotion.AdvanceAttackCountdown(_attackTimer).Timer;
        GC.KeepAlive(pose);
    }
}

struct ReplayReconstructionAdapter : IReconstructionPolicyAdapter
{
    private int _selectedIndex;
    public bool SelectedCrouched;
    public ReplayReconstructionAdapter(int selectedIndex) { _selectedIndex = selectedIndex; SelectedCrouched = false; }
    public ReconstructionObservation ProbeCandidate(int worldX, int worldY, bool crouched) =>
        _selectedIndex-- == 0 ? Select(crouched) : ReconstructionObservation.Blocked;
    private ReconstructionObservation Select(bool crouched)
    {
        SelectedCrouched = crouched;
        return ReconstructionObservation.Valid;
    }
    public void PrepareInitialization(int worldX, int worldY, bool crouched) { }
    public void PreparePoseProjection() { }
    public void PrepareHealthProjection() { }
    public void PrepareSuccessDiagnostics(ReconstructionCandidate candidate) { }
    public void CommitPreparedSuccess() { }
    public void CommitCollisionFault() { }
    public void CommitNoSafeCandidate() { }
}

static class ReplayButtons
{
    public const ushort Down = 0x4000;
    public const ushort Right = 0x2000;
    public const ushort Jump = 0x0040;
}
