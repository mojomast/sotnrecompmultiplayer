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
    // This exceeds the observed 28-frame stale-identity interval after file-select reconstruction.
    public const int RequiredQuietSettleSamples = 60;
    public const int RequiredQualifyingSamples = 2;

    public NativeLoadBootstrapPhase Phase { get; private set; }
    public bool Armed => Phase != NativeLoadBootstrapPhase.Closed;
    public int ConsecutiveQualifyingSamples { get; private set; }
    public int ConsecutiveQuietSamples { get; private set; }
    public bool Stable => ConsecutiveQualifyingSamples >= RequiredQualifyingSamples;
    private bool _provisionalRoomKnown;
    private bool _identityChanged;
    private ManagedRoomKey _provisionalRoom;

    public void Arm()
    {
        _provisionalRoomKnown = false;
        _identityChanged = false;
        ConsecutiveQualifyingSamples = 0;
        ConsecutiveQuietSamples = 0;
        Phase = NativeLoadBootstrapPhase.Armed;
    }

    // Returns true when consecutive post-update samples make baseline and reconstruction eligible.
    public bool ObserveQualifyingPostUpdate(ManagedRoomKey observedRoom)
    {
        if (!Armed) return false;
        if (ConsecutiveQualifyingSamples < RequiredQualifyingSamples) ConsecutiveQualifyingSamples++;
        if (Phase == NativeLoadBootstrapPhase.ProvisionalSelected)
        {
            if (!_provisionalRoom.SameRoomAs(observedRoom))
            {
                ConsecutiveQuietSamples = 0;
                _identityChanged = true;
            }
            else if (!_identityChanged && ConsecutiveQuietSamples < RequiredQuietSettleSamples)
            {
                ConsecutiveQuietSamples++;
            }
        }
        return Phase == NativeLoadBootstrapPhase.Armed && Stable;
    }

    public void ObserveNonQualifyingPostUpdate()
    {
        if (!Armed) return;
        ConsecutiveQualifyingSamples = 0;
        ConsecutiveQuietSamples = 0;
        if (Phase == NativeLoadBootstrapPhase.BaselineObserved)
            Phase = NativeLoadBootstrapPhase.Armed;
    }

    public void ObserveLayer()
    {
        if (Armed) ConsecutiveQuietSamples = 0;
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
            ConsecutiveQuietSamples = 0;
            Phase = NativeLoadBootstrapPhase.Suspended;
            return false;
        }
        if (!_provisionalRoomKnown)
        {
            _provisionalRoom = reconstructedRoom;
            _provisionalRoomKnown = true;
            _identityChanged = false;
            ConsecutiveQuietSamples = 0;
            Phase = NativeLoadBootstrapPhase.ProvisionalSelected;
            return false;
        }
        if (_provisionalRoom.SameRoomAs(reconstructedRoom))
        {
            Phase = NativeLoadBootstrapPhase.ProvisionalSelected;
            return false;
        }
        return CloseCore();
    }

    public bool CompleteQuietSettle()
    {
        if (Phase != NativeLoadBootstrapPhase.ProvisionalSelected || _identityChanged ||
            ConsecutiveQuietSamples < RequiredQuietSettleSamples)
            return false;
        return CloseCore();
    }

    private bool CloseCore()
    {
        Phase = NativeLoadBootstrapPhase.Closed;
        _provisionalRoomKnown = false;
        _identityChanged = false;
        ConsecutiveQualifyingSamples = 0;
        ConsecutiveQuietSamples = 0;
        return true;
    }

    public bool Close()
    {
        if (!Armed) return false;
        return CloseCore();
    }
}
