namespace CoopFeasibilityMod;

public readonly record struct RouteTransitionAggregateState(int ValidObservations, bool Stopped,
    bool Complete, int ExpectedFrom, int ExpectedTo);

// Game-free release checkpoint consumer. It accepts exactly 25 consecutive observations in the
// declared candidate route order and stops permanently on the first failed or mismatched segment.
public sealed class RouteTransitionAggregateReducer
{
    public const int RequiredObservations = 25;
    private static readonly byte[] From = [9, 10, 5, 6, 5, 10, 9, 19, 11, 19];
    private static readonly byte[] To = [10, 5, 6, 5, 10, 9, 19, 11, 19, 9];
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
