namespace CoopFeasibilityMod;

// Adapter-owned classification for unordered native load callbacks. It deliberately has no
// room or retry counters: reconstruction policy remains the bounded authority for retries.
public enum NativeLoadBootstrapPhase : byte
{
    Closed,
    Armed,
    BaselineObserved,
    ProvisionalSelected,
    Suspended,
}

public sealed class NativeLoadBootstrapState
{
    public NativeLoadBootstrapPhase Phase { get; private set; }
    public bool Armed => Phase != NativeLoadBootstrapPhase.Closed;
    private bool _preLoadRoomKnown;
    private ManagedRoomKey _preLoadRoom;

    public void Arm()
    {
        _preLoadRoomKnown = false;
        Phase = NativeLoadBootstrapPhase.Armed;
    }

    public void Arm(ManagedRoomKey preLoadRoom)
    {
        _preLoadRoom = preLoadRoom;
        _preLoadRoomKnown = true;
        Phase = NativeLoadBootstrapPhase.Armed;
    }

    // True exactly once per load, when the first fully-gated post-update room becomes the
    // reducer baseline rather than evidence of a transition.
    public bool ConsumeSafeBaseline()
    {
        if (Phase != NativeLoadBootstrapPhase.Armed) return false;
        Phase = NativeLoadBootstrapPhase.BaselineObserved;
        return true;
    }

    // A selection of the room retained across PlayerLoaded is provisional: the engine can still
    // publish that stale identity before the destination room. Selection otherwise proves the
    // reducer's fully-gated stabilization of the post-load identity.
    public bool CompleteReconstruction(ManagedMovementReconstructionResult result,
        ManagedRoomKey reconstructedRoom)
    {
        if (!Armed) return false;
        if (result != ManagedMovementReconstructionResult.Selected)
        {
            Phase = NativeLoadBootstrapPhase.Suspended;
            return false;
        }
        if (_preLoadRoomKnown && _preLoadRoom.SameRoomAs(reconstructedRoom))
        {
            Phase = NativeLoadBootstrapPhase.ProvisionalSelected;
            return false;
        }
        Phase = NativeLoadBootstrapPhase.Closed;
        return true;
    }

    public bool CompleteReconstruction(ManagedMovementReconstructionResult result) =>
        CompleteReconstruction(result, default);
}
