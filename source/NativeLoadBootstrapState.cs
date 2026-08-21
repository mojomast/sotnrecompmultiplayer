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
    public int ConsecutiveQualifyingSamples { get; private set; }
    public bool Stable => ConsecutiveQualifyingSamples >= RequiredQualifyingSamples;
    public const int RequiredQualifyingSamples = 2;

    public void Arm()
    {
        ConsecutiveQualifyingSamples = 0;
        Phase = NativeLoadBootstrapPhase.Armed;
    }

    // Returns true when consecutive post-update samples make baseline and reconstruction eligible.
    public bool ObserveQualifyingPostUpdate()
    {
        if (!Armed) return false;
        if (ConsecutiveQualifyingSamples < RequiredQualifyingSamples) ConsecutiveQualifyingSamples++;
        return Phase == NativeLoadBootstrapPhase.Armed && Stable;
    }

    public void ObserveNonQualifyingPostUpdate()
    {
        if (!Armed) return;
        ConsecutiveQualifyingSamples = 0;
        if (Phase == NativeLoadBootstrapPhase.BaselineObserved) Phase = NativeLoadBootstrapPhase.Armed;
    }

    // True exactly once per load, when a fully-gated post-update room becomes the
    // reducer baseline rather than evidence of a transition.
    public bool ConsumeSafeBaseline()
    {
        if (Phase != NativeLoadBootstrapPhase.Armed || !Stable) return false;
        Phase = NativeLoadBootstrapPhase.BaselineObserved;
        return true;
    }

    public bool CompleteReconstruction(ManagedMovementReconstructionResult result,
        ManagedRoomKey reconstructedRoom)
    {
        if (!Armed || !Stable) return false;
        if (result != ManagedMovementReconstructionResult.Selected)
        {
            Phase = NativeLoadBootstrapPhase.Suspended;
            return false;
        }
        Phase = NativeLoadBootstrapPhase.Closed;
        ConsecutiveQualifyingSamples = 0;
        return true;
    }

    public bool CompleteReconstruction(ManagedMovementReconstructionResult result) =>
        CompleteReconstruction(result, default);

    public bool Close()
    {
        if (!Armed) return false;
        ConsecutiveQualifyingSamples = 0;
        Phase = NativeLoadBootstrapPhase.Closed;
        return true;
    }
}
