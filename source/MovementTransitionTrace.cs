using System;

namespace CoopFeasibilityMod;

public enum MovementTransitionTraceSource : byte
{
    SafeRoom,
    Unsafe,
    RoomLayer,
    Recovery,
    Reconstruction,
    Retry,
}

// Fixed-size lifecycle evidence for diagnosing transition ordering without retaining gameplay history.
public sealed class MovementTransitionTrace
{
    public const int Capacity = 24;

    private readonly MovementTransitionTraceEntry[] _entries = new MovementTransitionTraceEntry[Capacity];
    private int _next;
    private int _count;

    public void Record(long frame, MovementTransitionTraceSource source,
        ManagedMovementSessionState state, ManagedRoomKey current, string reconstruction,
        string retry)
    {
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        _entries[_next] = new MovementTransitionTraceEntry(frame, source, state.TransitionOrigin,
            current, state.TransitionPending, state.AwaitingPostTransitionMovement,
            reconstruction, retry);
        _next = (_next + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public MovementTransitionTraceEntry[] Snapshot()
    {
        var result = new MovementTransitionTraceEntry[_count];
        int start = (_next - _count + Capacity) % Capacity;
        for (int index = 0; index < _count; index++) result[index] = _entries[(start + index) % Capacity];
        return result;
    }
}

public readonly struct MovementTransitionTraceEntry
{
    public long Frame { get; }
    public MovementTransitionTraceSource EventSource { get; }
    public ManagedRoomKey Origin { get; }
    public ManagedRoomKey Current { get; }
    public bool TransitionPending { get; }
    public bool AwaitingPostTransitionMovement { get; }
    public string Reconstruction { get; }
    public string Retry { get; }

    internal MovementTransitionTraceEntry(long frame, MovementTransitionTraceSource eventSource,
        ManagedRoomKey origin, ManagedRoomKey current, bool transitionPending,
        bool awaitingPostTransitionMovement, string reconstruction, string retry)
    {
        Frame = frame;
        EventSource = eventSource;
        Origin = origin;
        Current = current;
        TransitionPending = transitionPending;
        AwaitingPostTransitionMovement = awaitingPostTransitionMovement;
        Reconstruction = reconstruction;
        Retry = retry;
    }
}
