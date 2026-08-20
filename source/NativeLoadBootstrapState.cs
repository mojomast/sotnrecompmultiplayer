namespace CoopFeasibilityMod;

// Adapter-owned classification for unordered native load callbacks. It deliberately has no
// room or retry counters: reconstruction policy remains the bounded authority for retries.
public enum NativeLoadBootstrapPhase : byte
{
    Closed,
    Armed,
    BaselineObserved,
    Suspended,
}

public sealed class NativeLoadBootstrapState
{
    public NativeLoadBootstrapPhase Phase { get; private set; }
    public bool Armed => Phase != NativeLoadBootstrapPhase.Closed;

    public void Arm() => Phase = NativeLoadBootstrapPhase.Armed;

    // True exactly once per load, when the first fully-gated post-update room becomes the
    // reducer baseline rather than evidence of a transition.
    public bool ConsumeSafeBaseline()
    {
        if (Phase != NativeLoadBootstrapPhase.Armed) return false;
        Phase = NativeLoadBootstrapPhase.BaselineObserved;
        return true;
    }

    public void CompleteReconstruction(ManagedMovementReconstructionResult result)
    {
        if (!Armed) return;
        Phase = result == ManagedMovementReconstructionResult.Selected
            ? NativeLoadBootstrapPhase.Closed
            : NativeLoadBootstrapPhase.Suspended;
    }
}
