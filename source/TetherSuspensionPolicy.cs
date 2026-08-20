using System;

namespace CoopFeasibilityMod;

public enum TetherPhase : byte
{
    Active = 1,
    Warning = 2,
    Resistance = 3,
    Reconstructing = 4,
    Suspended = 5,
}

public enum TetherMovementIntent : byte
{
    None,
    Inward,
    Outward,
}

public enum TetherLifecycle : byte
{
    Active,
    Transition,
    Reconstructing,
    Inactive,
    Fatal,
}

// Stable numeric values are exposed through p2d4/2. Keep this closed and bounded.
public enum TetherReason : byte
{
    None,
    ComfortBoundary,
    ResistanceBoundary,
    HardBoundary,
    Transition,
    Reconstruction,
    Lifecycle,
    UnsupportedTerrain,
    Collision,
    Fatal,
    Downed,
}

public enum TetherMovementCommand : byte
{
    Allow,
    BlockOutward,
}

public readonly record struct TetherObservation(int DeltaX, int DeltaY,
    TetherMovementIntent MovementIntent, TetherLifecycle Lifecycle, TetherReason UnsafeReason);

public readonly record struct TetherCommand(TetherPhase Phase, TetherMovementCommand Movement,
    bool BeginReconstruction, bool ShowStatus, TetherReason Reason);

public readonly record struct TetherDiagnostics(TetherPhase Phase, TetherReason Reason,
    ulong WarningEntries, ulong ResistanceEntries, ulong ReconstructionEntries,
    ulong SuspensionEntries, ulong WarningFrames, ulong WarningMaxConsecutive,
    ulong ResistanceFrames, ulong ResistanceMaxConsecutive, ulong ReconstructionFrames,
    ulong ReconstructionMaxConsecutive, ulong SuspensionFrames, ulong SuspensionMaxConsecutive,
    bool OutwardResistance, ulong StatusEligible, ulong StatusSubmitted, ulong HardRecoveries);

// P1 remains the sole camera/exit owner. The reducer emits only bounded P2 commands and never
// positions either player. Comfort (160x112) leaves 64x48 pixels before resistance (224x160),
// which itself leaves 32x32 before the existing strict hard reconstruction bound (256x192).
public sealed class TetherSuspensionReducer
{
    public const int WarningX = 160;
    public const int WarningY = 112;
    public const int ResistanceX = 224;
    public const int ResistanceY = 160;
    public const int HardX = 256;
    public const int HardY = 192;

    private TetherPhase _phase = TetherPhase.Suspended;
    private TetherReason _reason = TetherReason.Lifecycle;
    private ulong _warningEntries, _resistanceEntries, _reconstructionEntries, _suspensionEntries;
    private ulong _warningFrames, _warningRun, _warningMax;
    private ulong _resistanceFrames, _resistanceRun, _resistanceMax;
    private ulong _reconstructionFrames, _reconstructionRun, _reconstructionMax;
    private ulong _suspensionFrames, _suspensionRun, _suspensionMax;
    private ulong _statusEligible, _statusSubmitted, _hardRecoveries;
    private bool _outwardResistance;
    private bool _hardRecoveryLatched;
    private bool _hasObservation;

    public TetherDiagnostics Diagnostics => new(_phase, _reason, _warningEntries,
        _resistanceEntries, _reconstructionEntries, _suspensionEntries, _warningFrames,
        _warningMax, _resistanceFrames, _resistanceMax, _reconstructionFrames,
        _reconstructionMax, _suspensionFrames, _suspensionMax, _outwardResistance,
        _statusEligible, _statusSubmitted, _hardRecoveries);

    public TetherCommand Reduce(in TetherObservation observation)
    {
        Validate(observation);
        int x = AbsBounded(observation.DeltaX);
        int y = AbsBounded(observation.DeltaY);
        bool hard = x > HardX || y > HardY;
        TetherPhase next;
        TetherReason reason;

        if (observation.Lifecycle == TetherLifecycle.Fatal || observation.UnsafeReason == TetherReason.Fatal)
        {
            next = TetherPhase.Suspended;
            reason = TetherReason.Fatal;
        }
        else if (observation.UnsafeReason == TetherReason.Lifecycle ||
                 observation.Lifecycle == TetherLifecycle.Active && observation.UnsafeReason is
                     TetherReason.UnsupportedTerrain or TetherReason.Collision or
                     TetherReason.Reconstruction or TetherReason.HardBoundary)
        {
            next = TetherPhase.Suspended;
            reason = observation.UnsafeReason;
        }
        else if (observation.Lifecycle == TetherLifecycle.Transition)
        {
            next = TetherPhase.Reconstructing;
            reason = TetherReason.Transition;
        }
        else if (observation.Lifecycle == TetherLifecycle.Reconstructing)
        {
            next = TetherPhase.Reconstructing;
            reason = TetherReason.Reconstruction;
        }
        else if (observation.Lifecycle == TetherLifecycle.Inactive)
        {
            next = TetherPhase.Suspended;
            reason = observation.UnsafeReason == TetherReason.Downed ? TetherReason.Downed : TetherReason.Lifecycle;
        }
        else if (hard)
        {
            next = TetherPhase.Reconstructing;
            reason = TetherReason.HardBoundary;
        }
        else if (x >= ResistanceX || y >= ResistanceY)
        {
            next = TetherPhase.Resistance;
            reason = TetherReason.ResistanceBoundary;
        }
        else if (x >= WarningX || y >= WarningY)
        {
            next = TetherPhase.Warning;
            reason = TetherReason.ComfortBoundary;
        }
        else
        {
            next = TetherPhase.Active;
            reason = observation.UnsafeReason == TetherReason.Downed ? TetherReason.Downed : TetherReason.None;
        }

        bool entry = !_hasObservation || next != _phase;
        bool beginHardRecovery = hard && !_hardRecoveryLatched &&
            observation.Lifecycle == TetherLifecycle.Active && observation.UnsafeReason is
                TetherReason.None or TetherReason.Downed;
        ulong warningEntries = _warningEntries, resistanceEntries = _resistanceEntries;
        ulong reconstructionEntries = _reconstructionEntries, suspensionEntries = _suspensionEntries;
        if (entry)
        {
            if (next == TetherPhase.Warning) warningEntries = Increment(_warningEntries);
            else if (next == TetherPhase.Resistance) resistanceEntries = Increment(_resistanceEntries);
            else if (next == TetherPhase.Reconstructing) reconstructionEntries = Increment(_reconstructionEntries);
            else if (next == TetherPhase.Suspended) suspensionEntries = Increment(_suspensionEntries);
        }
        ulong warningFrames = _warningFrames, warningRun = 0, warningMax = _warningMax;
        ulong resistanceFrames = _resistanceFrames, resistanceRun = 0, resistanceMax = _resistanceMax;
        ulong reconstructionFrames = _reconstructionFrames, reconstructionRun = 0, reconstructionMax = _reconstructionMax;
        ulong suspensionFrames = _suspensionFrames, suspensionRun = 0, suspensionMax = _suspensionMax;
        Count(next, TetherPhase.Warning, ref warningFrames, ref warningRun, _warningRun, ref warningMax);
        Count(next, TetherPhase.Resistance, ref resistanceFrames, ref resistanceRun, _resistanceRun, ref resistanceMax);
        Count(next, TetherPhase.Reconstructing, ref reconstructionFrames, ref reconstructionRun, _reconstructionRun, ref reconstructionMax);
        Count(next, TetherPhase.Suspended, ref suspensionFrames, ref suspensionRun, _suspensionRun, ref suspensionMax);
        ulong hardRecoveries = beginHardRecovery ? Increment(_hardRecoveries) : _hardRecoveries;

        _phase = next; _reason = reason;
        _warningEntries = warningEntries; _resistanceEntries = resistanceEntries;
        _reconstructionEntries = reconstructionEntries; _suspensionEntries = suspensionEntries;
        _warningFrames = warningFrames; _warningRun = warningRun; _warningMax = warningMax;
        _resistanceFrames = resistanceFrames; _resistanceRun = resistanceRun; _resistanceMax = resistanceMax;
        _reconstructionFrames = reconstructionFrames; _reconstructionRun = reconstructionRun; _reconstructionMax = reconstructionMax;
        _suspensionFrames = suspensionFrames; _suspensionRun = suspensionRun; _suspensionMax = suspensionMax;
        _outwardResistance = next == TetherPhase.Resistance && observation.MovementIntent == TetherMovementIntent.Outward;
        _hardRecoveries = hardRecoveries;
        _hardRecoveryLatched = hard || observation.Lifecycle != TetherLifecycle.Active;
        _hasObservation = true;

        return new TetherCommand(next, _outwardResistance ? TetherMovementCommand.BlockOutward : TetherMovementCommand.Allow,
            beginHardRecovery, true, reason);
    }

    public void RecordStatus(bool submitted)
    {
        ulong eligible = Increment(_statusEligible);
        ulong submissions = submitted ? Increment(_statusSubmitted) : _statusSubmitted;
        _statusEligible = eligible;
        _statusSubmitted = submissions;
    }

    public void ResetDiagnostics()
    {
        _phase = TetherPhase.Suspended;
        _reason = TetherReason.Lifecycle;
        _warningEntries = _resistanceEntries = _reconstructionEntries = _suspensionEntries = 0;
        _warningFrames = _warningRun = _warningMax = 0;
        _resistanceFrames = _resistanceRun = _resistanceMax = 0;
        _reconstructionFrames = _reconstructionRun = _reconstructionMax = 0;
        _suspensionFrames = _suspensionRun = _suspensionMax = 0;
        _statusEligible = _statusSubmitted = _hardRecoveries = 0;
        _outwardResistance = _hardRecoveryLatched = false;
        _hasObservation = false;
    }

    private static void Validate(in TetherObservation value)
    {
        if (value.MovementIntent is < TetherMovementIntent.None or > TetherMovementIntent.Outward ||
            value.Lifecycle is < TetherLifecycle.Active or > TetherLifecycle.Fatal ||
            value.UnsafeReason is < TetherReason.None or > TetherReason.Downed)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static int AbsBounded(int value) => value == int.MinValue ? int.MaxValue : Math.Abs(value);
    private static ulong Increment(ulong value) => value == ulong.MaxValue
        ? throw new InvalidOperationException("Tether diagnostic counter exhausted.") : value + 1;
    private static void Count(TetherPhase actual, TetherPhase expected, ref ulong total,
        ref ulong run, ulong previousRun, ref ulong maximum)
    {
        if (actual != expected) return;
        total = Increment(total);
        run = Increment(previousRun);
        if (run > maximum) maximum = run;
    }
}

public static class CoopStatusRenderPolicy
{
    // Fatal is a hard circuit breaker for every direct GPU or render-memory touch. Unsafe,
    // transition, and suspension are intentionally handled by Eligible instead.
    public static bool DirectCallsAllowed(bool fatal) => !fatal;

    // Safe-frame, proxy, transition, and suspension state deliberately are not inputs.
    public static bool Eligible(bool enabled, bool gameAvailable, bool inGame, bool loading,
        bool menuOpen, bool mapOpen, bool stageDisplay) =>
        enabled && gameAvailable && inGame && !loading && !menuOpen && !mapOpen && stageDisplay;

    public static bool AvatarEligible(bool statusEligible, bool safeFrame, bool proxyInitialized,
        bool animationValid, bool transitionPending) => statusEligible && safeFrame &&
        proxyInitialized && animationValid && !transitionPending;
}
