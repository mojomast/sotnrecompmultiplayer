using System;
using System.Threading;

namespace CoopFeasibilityMod;

// These values are part of the P2D4 and managed-replay contracts. Do not reorder them.
public enum ManagedLocomotion : byte
{
    Idle = 0,
    Walk = 1,
    Rising = 2,
    Falling = 3,
    Crouched = 4,
    Attacking = 5,
    Hurt = 6,
    Downed = 7,
}

// These values are part of the P2D4 and managed-replay contracts. Do not reorder them.
public enum ManagedAnimation : byte
{
    Idle = 0,
    Walk = 1,
    JumpRise = 2,
    Fall = 3,
    Landing = 4,
    CrouchEnter = 5,
    CrouchHold = 6,
    CrouchExit = 7,
    Hurt = 8,
    CompactHurt = 9,
    AttackStartup = 10,
    AttackActive = 11,
    AttackRecovery = 12,
    Downed = 13,
}

// Logical timing is shared by the pure reducer and the native pose adapter. Native frame,
// sprite, and hurtbox payload deliberately remain outside this catalog.
public static class ManagedLocomotionCatalog
{
    public const int HurtUpdates = 18;
    public const int AttackStartupUpdates = 8;
    public const int AttackActiveUpdates = 4;
    public const int AttackRecoveryUpdates = 10;
    public const int AttackTotalUpdates =
        AttackStartupUpdates + AttackActiveUpdates + AttackRecoveryUpdates;

    public static int FrameCount(ManagedAnimation animation) => animation switch
    {
        ManagedAnimation.Idle => 4,
        ManagedAnimation.Walk => 8,
        ManagedAnimation.JumpRise => 2,
        ManagedAnimation.Fall => 2,
        ManagedAnimation.Landing => 5,
        ManagedAnimation.CrouchEnter => 13,
        ManagedAnimation.CrouchHold => 1,
        ManagedAnimation.CrouchExit => 2,
        ManagedAnimation.Hurt => 1,
        ManagedAnimation.CompactHurt => 1,
        ManagedAnimation.AttackStartup => 1,
        ManagedAnimation.AttackActive => 1,
        ManagedAnimation.AttackRecovery => 1,
        ManagedAnimation.Downed => 1,
        _ => 1,
    };

    public static bool Loops(ManagedAnimation animation) =>
        animation is ManagedAnimation.Idle or ManagedAnimation.Walk or ManagedAnimation.JumpRise or
            ManagedAnimation.Fall or ManagedAnimation.CrouchHold or ManagedAnimation.Downed;

    public static bool TryGetDuration(ManagedAnimation animation, int frame, out int duration)
    {
        duration = animation switch
        {
            ManagedAnimation.Idle when frame is >= 0 and < 4 => 12,
            ManagedAnimation.Walk when frame is >= 0 and < 8 => 4,
            ManagedAnimation.JumpRise when frame is >= 0 and < 2 => 6,
            ManagedAnimation.Fall when frame is >= 0 and < 2 => 6,
            ManagedAnimation.Landing when frame is >= 0 and < 5 => 5,
            ManagedAnimation.CrouchEnter when frame == 0 => 2,
            ManagedAnimation.CrouchEnter when frame is > 0 and < 13 => 4,
            ManagedAnimation.CrouchHold when frame == 0 => 255,
            ManagedAnimation.CrouchExit when frame is >= 0 and < 2 => 3,
            ManagedAnimation.Hurt when frame == 0 => HurtUpdates,
            ManagedAnimation.CompactHurt when frame == 0 => HurtUpdates,
            ManagedAnimation.AttackStartup when frame == 0 => AttackStartupUpdates,
            ManagedAnimation.AttackActive when frame == 0 => AttackActiveUpdates,
            ManagedAnimation.AttackRecovery when frame == 0 => AttackRecoveryUpdates,
            ManagedAnimation.Downed when frame == 0 => 255,
            _ => 0,
        };
        return duration != 0;
    }
}

public readonly struct ManagedLocomotionObservation
{
    public readonly bool Downed;
    public readonly bool Hurt;
    public readonly bool CompactHurt;
    public readonly int AttackTimer;
    public readonly bool Crouched;
    public readonly bool Grounded;
    public readonly bool HorizontalIntent;
    public readonly int VelocityX;
    public readonly int VelocityY;
    public readonly bool LandedThisUpdate;

    public ManagedLocomotionObservation(bool downed, bool hurt, bool compactHurt, int attackTimer,
        bool crouched, bool grounded, bool horizontalIntent, int velocityX, int velocityY,
        bool landedThisUpdate)
    {
        if (attackTimer < 0) throw new ArgumentOutOfRangeException(nameof(attackTimer));
        Downed = downed;
        Hurt = hurt;
        CompactHurt = compactHurt;
        AttackTimer = attackTimer;
        Crouched = crouched;
        Grounded = grounded;
        HorizontalIntent = horizontalIntent;
        VelocityX = velocityX;
        VelocityY = velocityY;
        LandedThisUpdate = landedThisUpdate;
    }
}

public readonly struct ManagedLocomotionState
{
    public readonly bool Valid;
    public readonly ManagedLocomotion Locomotion;
    public readonly ManagedAnimation Animation;
    public readonly int Frame;
    public readonly int Tick;
    public readonly int Transitions;
    public readonly long Advances;
    public readonly int StatesSeen;
    public readonly int AdvanceStatesSeen;
    public readonly int AttackPhaseCompletionMask;

    internal ManagedLocomotionState(bool valid, ManagedLocomotion locomotion,
        ManagedAnimation animation, int frame, int tick, int transitions, long advances,
        int statesSeen, int advanceStatesSeen, int attackPhaseCompletionMask)
    {
        Valid = valid;
        Locomotion = locomotion;
        Animation = animation;
        Frame = frame;
        Tick = tick;
        Transitions = transitions;
        Advances = advances;
        StatesSeen = statesSeen;
        AdvanceStatesSeen = advanceStatesSeen;
        AttackPhaseCompletionMask = attackPhaseCompletionMask;
    }
}

public readonly struct ManagedAttackAdvance
{
    public readonly int Timer;
    public readonly bool EnteredActive;
    public readonly bool EnteredRecovery;
    public readonly bool Completed;

    internal ManagedAttackAdvance(int timer, bool enteredActive, bool enteredRecovery, bool completed)
    {
        Timer = timer;
        EnteredActive = enteredActive;
        EnteredRecovery = enteredRecovery;
        Completed = completed;
    }
}

public readonly struct ManagedLocomotionInitialization
{
    internal readonly long OwnerId;
    internal readonly ulong ExpectedRevision;
    internal readonly ulong NextRevision;
    internal readonly ManagedLocomotionState ExpectedState;
    internal ManagedLocomotionInitialization(long ownerId, ulong expectedRevision, ulong nextRevision,
        ManagedLocomotionState expectedState)
    {
        OwnerId = ownerId;
        ExpectedRevision = expectedRevision;
        NextRevision = nextRevision;
        ExpectedState = expectedState;
    }
}

public readonly struct ManagedLocomotionDiagnosticReset
{
    internal readonly long OwnerId;
    internal readonly ulong ExpectedRevision;
    internal readonly ulong NextRevision;
    internal readonly ManagedLocomotionState ExpectedState;

    internal ManagedLocomotionDiagnosticReset(long ownerId, ulong expectedRevision,
        ulong nextRevision, ManagedLocomotionState expectedState)
    {
        OwnerId = ownerId;
        ExpectedRevision = expectedRevision;
        NextRevision = nextRevision;
        ExpectedState = expectedState;
    }
}

// Owns logical pose selection, timing, evidence, and forced attack-phase transitions only.
// Physics, native pose payload, rendering, and hurtbox evidence remain adapter responsibilities.
public sealed class ManagedLocomotionReducer
{
    private static long _lastOwnerId;
    private readonly long _ownerId = AllocateOwnerId();
    private ulong _initializationRevision = 1;
    private bool _valid;
    private ManagedLocomotion _locomotion;
    private ManagedAnimation _animation;
    private int _frame;
    private int _tick;
    private int _transitions;
    private long _advances;
    private int _statesSeen;
    private int _advanceStatesSeen;
    private int _attackPhaseCompletionMask;

    public ManagedLocomotionState State => new(_valid, _locomotion, _animation, _frame, _tick,
        _transitions, _advances, _statesSeen, _advanceStatesSeen, _attackPhaseCompletionMask);

    public ManagedLocomotionState Update(in ManagedLocomotionObservation observation)
    {
        Select(observation, out ManagedLocomotion locomotion, out ManagedAnimation animation);
        if (!_valid)
        {
            SetAnimation(locomotion, animation);
            _valid = true;
        }
        else if (_locomotion != locomotion || _animation != animation)
        {
            SetAnimation(locomotion, animation);
            _transitions = unchecked(_transitions + 1);
        }
        else
        {
            _tick = unchecked(_tick + 1);
            if (!ManagedLocomotionCatalog.TryGetDuration(animation, _frame, out int duration))
            {
                // Preserve the legacy partial update: tick advances, then the pose becomes invalid,
                // and no state/advance evidence is credited for that update.
                _valid = false;
                return State;
            }
            if (_tick >= duration)
            {
                _tick = 0;
                int count = ManagedLocomotionCatalog.FrameCount(animation);
                if (_frame + 1 < count) _frame++;
                else if (ManagedLocomotionCatalog.Loops(animation)) _frame = 0;
                else _tick = duration; // Terminal one-shots hold and account again if forced to remain.
                _advances = unchecked(_advances + 1);
                _advanceStatesSeen |= 1 << (int)animation;
            }
        }
        _statesSeen |= 1 << (int)animation;
        return State;
    }

    // Called after Update with the pre-decrement timer, preserving startup/active/recovery order.
    public ManagedAttackAdvance AdvanceAttackCountdown(int timer)
    {
        if (timer <= 0) throw new ArgumentOutOfRangeException(nameof(timer));
        int next = timer - 1;
        bool active = next == ManagedLocomotionCatalog.AttackActiveUpdates +
            ManagedLocomotionCatalog.AttackRecoveryUpdates;
        bool recovery = next == ManagedLocomotionCatalog.AttackRecoveryUpdates;
        bool completed = next == 0;
        if (active)
        {
            _attackPhaseCompletionMask |= 1;
            ForceAttackAnimation(ManagedAnimation.AttackActive);
        }
        else if (recovery)
        {
            _attackPhaseCompletionMask |= 2;
            ForceAttackAnimation(ManagedAnimation.AttackRecovery);
        }
        else if (completed)
        {
            _attackPhaseCompletionMask |= 4;
        }
        return new ManagedAttackAdvance(next, active, recovery, completed);
    }

    // Unload, room/layer changes, reconstruction, revive completion, and fatal paths preserve
    // accumulated evidence and the current pose while requiring fresh selection next update.
    public ManagedLocomotionState Invalidate()
    {
        _valid = false;
        return State;
    }

    // Reconstruction initialization additionally clears frame/tick just as the legacy adapter did.
    public ManagedLocomotionState Initialize()
    {
        ManagedLocomotionInitialization initialization = PrepareInitialization();
        if (!CommitInitialization(initialization))
            throw new InvalidOperationException("Managed locomotion initialization became stale.");
        return State;
    }

    public ManagedLocomotionInitialization PrepareInitialization()
    {
        if (_initializationRevision == ulong.MaxValue)
            throw new InvalidOperationException("Managed locomotion initialization revision is exhausted.");
        return new ManagedLocomotionInitialization(_ownerId, _initializationRevision,
            _initializationRevision + 1, State);
    }

    public bool CanCommitInitialization(ManagedLocomotionInitialization initialization) =>
        initialization.OwnerId == _ownerId && initialization.ExpectedRevision != 0 &&
        _initializationRevision != ulong.MaxValue &&
        initialization.ExpectedRevision == _initializationRevision &&
        initialization.NextRevision == _initializationRevision + 1 &&
        Same(initialization.ExpectedState, State);

    public bool CommitInitialization(ManagedLocomotionInitialization initialization)
    {
        if (!CanCommitInitialization(initialization)) return false;
        CommitPreparedInitialization(initialization);
        return true;
    }

    internal void CommitPreparedInitialization(ManagedLocomotionInitialization initialization)
    {
        _initializationRevision = initialization.NextRevision;
        _valid = false;
        _frame = 0;
        _tick = 0;
    }

    public ManagedLocomotionState DiagnosticReset()
    {
        ManagedLocomotionDiagnosticReset reset = PrepareDiagnosticReset();
        if (!CommitDiagnosticReset(reset))
            throw new InvalidOperationException("Managed locomotion diagnostic reset became stale.");
        return State;
    }

    public ManagedLocomotionDiagnosticReset PrepareDiagnosticReset()
    {
        if (_initializationRevision == ulong.MaxValue)
            throw new InvalidOperationException("Managed locomotion initialization revision is exhausted.");
        return new ManagedLocomotionDiagnosticReset(_ownerId, _initializationRevision,
            _initializationRevision + 1, State);
    }

    public bool CommitDiagnosticReset(in ManagedLocomotionDiagnosticReset reset)
    {
        if (!CanCommitDiagnosticReset(reset)) return false;
        _initializationRevision = reset.NextRevision;
        _valid = false;
        _locomotion = ManagedLocomotion.Falling;
        _animation = ManagedAnimation.Fall;
        _frame = 0;
        _tick = 0;
        _transitions = 0;
        _advances = 0;
        _statesSeen = 0;
        _advanceStatesSeen = 0;
        _attackPhaseCompletionMask = 0;
        return true;
    }

    public bool CanCommitDiagnosticReset(in ManagedLocomotionDiagnosticReset reset) =>
        reset.OwnerId == _ownerId && reset.ExpectedRevision == _initializationRevision &&
        reset.NextRevision == _initializationRevision + 1 && Same(reset.ExpectedState, State);

    private void Select(in ManagedLocomotionObservation observation,
        out ManagedLocomotion locomotion, out ManagedAnimation animation)
    {
        if (observation.Downed)
        {
            locomotion = ManagedLocomotion.Downed;
            animation = ManagedAnimation.Downed;
        }
        else if (observation.Hurt)
        {
            locomotion = ManagedLocomotion.Hurt;
            animation = observation.CompactHurt ? ManagedAnimation.CompactHurt : ManagedAnimation.Hurt;
        }
        else if (observation.AttackTimer > 0)
        {
            locomotion = ManagedLocomotion.Attacking;
            animation = observation.AttackTimer > ManagedLocomotionCatalog.AttackActiveUpdates +
                ManagedLocomotionCatalog.AttackRecoveryUpdates
                ? ManagedAnimation.AttackStartup
                : observation.AttackTimer > ManagedLocomotionCatalog.AttackRecoveryUpdates
                    ? ManagedAnimation.AttackActive : ManagedAnimation.AttackRecovery;
        }
        else if (observation.Crouched)
        {
            locomotion = ManagedLocomotion.Crouched;
            animation = _animation == ManagedAnimation.CrouchEnter && IsOneShotInProgress(_animation)
                ? ManagedAnimation.CrouchEnter
                : _animation is ManagedAnimation.CrouchEnter or ManagedAnimation.CrouchHold
                    ? ManagedAnimation.CrouchHold : ManagedAnimation.CrouchEnter;
        }
        else if (_animation is ManagedAnimation.CrouchEnter or ManagedAnimation.CrouchHold)
        {
            locomotion = observation.HorizontalIntent ? ManagedLocomotion.Walk : ManagedLocomotion.Crouched;
            animation = observation.HorizontalIntent ? ManagedAnimation.Walk : ManagedAnimation.CrouchExit;
        }
        else if (observation.Grounded && !observation.HorizontalIntent &&
            _animation == ManagedAnimation.CrouchExit && IsOneShotInProgress(_animation))
        {
            locomotion = ManagedLocomotion.Crouched;
            animation = ManagedAnimation.CrouchExit;
        }
        else if (observation.Grounded && !observation.HorizontalIntent &&
            _animation == ManagedAnimation.Landing && IsOneShotInProgress(_animation))
        {
            locomotion = ManagedLocomotion.Idle;
            animation = ManagedAnimation.Landing;
        }
        else if (observation.LandedThisUpdate && !observation.HorizontalIntent)
        {
            // Intentionally does not require Grounded: a landing-buffered jump clears grounded
            // after collision but still presents one fresh landing pose on that update.
            locomotion = ManagedLocomotion.Idle;
            animation = ManagedAnimation.Landing;
        }
        else if (observation.Grounded)
        {
            locomotion = observation.VelocityX == 0 ? ManagedLocomotion.Idle : ManagedLocomotion.Walk;
            animation = observation.VelocityX == 0 ? ManagedAnimation.Idle : ManagedAnimation.Walk;
        }
        else
        {
            locomotion = observation.VelocityY < 0 ? ManagedLocomotion.Rising : ManagedLocomotion.Falling;
            animation = observation.VelocityY < 0 ? ManagedAnimation.JumpRise : ManagedAnimation.Fall;
        }
    }

    private bool IsOneShotInProgress(ManagedAnimation animation)
    {
        if (animation is not (ManagedAnimation.Landing or ManagedAnimation.CrouchEnter or
            ManagedAnimation.CrouchExit) ||
            !ManagedLocomotionCatalog.TryGetDuration(animation, _frame, out int duration)) return false;
        return _frame + 1 < ManagedLocomotionCatalog.FrameCount(animation) || _tick < duration;
    }

    private void ForceAttackAnimation(ManagedAnimation animation)
    {
        SetAnimation(ManagedLocomotion.Attacking, animation);
        _transitions = unchecked(_transitions + 1);
        _statesSeen |= 1 << (int)animation;
    }

    private void SetAnimation(ManagedLocomotion locomotion, ManagedAnimation animation)
    {
        _locomotion = locomotion;
        _animation = animation;
        _frame = 0;
        _tick = 0;
    }

    private static bool Same(ManagedLocomotionState left, ManagedLocomotionState right) =>
        left.Valid == right.Valid && left.Locomotion == right.Locomotion &&
        left.Animation == right.Animation && left.Frame == right.Frame && left.Tick == right.Tick &&
        left.Transitions == right.Transitions && left.Advances == right.Advances &&
        left.StatesSeen == right.StatesSeen && left.AdvanceStatesSeen == right.AdvanceStatesSeen &&
        left.AttackPhaseCompletionMask == right.AttackPhaseCompletionMask;

    private static long AllocateOwnerId()
    {
        while (true)
        {
            long current = Volatile.Read(ref _lastOwnerId);
            if (current == long.MaxValue)
                throw new InvalidOperationException("Managed locomotion owner identity is exhausted.");
            long next = current + 1;
            if (Interlocked.CompareExchange(ref _lastOwnerId, next, current) == current) return next;
        }
    }
}
