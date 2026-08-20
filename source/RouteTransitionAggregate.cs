namespace CoopFeasibilityMod;

public readonly record struct RouteTransitionAggregateState(int ValidObservations, bool Stopped,
    bool Complete, int ExpectedFrom, int ExpectedTo);

// Game-free release checkpoint consumer. It accepts exactly 25 consecutive observations in the
// declared candidate route order and stops permanently on the first failed or mismatched segment.
// Route v2 uses live-observed telemetry room bytes: 140 is the NO0 clock-room junction (room table
// index 21, map cell 32,27) and 220 is the plain-door save room (index 31, map cell 31,27).
public sealed class RouteTransitionAggregateReducer
{
    public const int RequiredObservations = 25;
    private static readonly byte[] From = [140, 220];
    private static readonly byte[] To = [220, 140];
    private int _valid;
    private bool _stopped;

    public RouteTransitionAggregateState State
    {
        get
        {
            int index = _valid % From.Length;
            return new(_valid, _stopped, !_stopped && _valid == RequiredObservations,
                From[index], To[index]);
        }
    }

    public bool Observe(int from, int to, bool transitionPassed)
    {
        if (_stopped || _valid == RequiredObservations) return false;
        int index = _valid % From.Length;
        if (!transitionPassed || from != From[index] || to != To[index])
        {
            _stopped = true;
            return false;
        }
        _valid++;
        return _valid == RequiredObservations;
    }
}
