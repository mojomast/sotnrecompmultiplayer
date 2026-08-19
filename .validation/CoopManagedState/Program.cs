using System.Globalization;
using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("room epoch lifecycle", () =>
    {
        var tracker = new RoomEpochTracker();
        ManagedRoomKey first = Room(1);
        ManagedRoomKey second = Room(2);
        tracker.Observe(first);
        Equal(1UL, tracker.Epoch);
        True(tracker.BeginTransition());
        Equal(2UL, tracker.Epoch);
        True(!tracker.BeginTransition());
        tracker.Observe(second);
        True(tracker.TransitionPending);
        tracker.Complete(second);
        True(!tracker.TransitionPending);
        True(tracker.BeginTransition());
        tracker.Observe(first);
        tracker.Complete(first);
        Equal(3UL, tracker.Epoch);
    }),
    ("player reload advances epoch", () =>
    {
        var tracker = new RoomEpochTracker();
        tracker.Observe(Room(1));
        tracker.InvalidateForPlayerReload();
        Equal(2UL, tracker.Epoch);
        True(!tracker.Known && tracker.TransitionPending);
        tracker.Observe(Room(1));
        tracker.Complete(Room(1));
        Equal(2UL, tracker.Epoch);
    }),
    ("diagnostic reset preserves same-room epoch", () =>
    {
        var tracker = new RoomEpochTracker();
        tracker.Observe(Room(1));
        tracker.MarkDiagnosticReset();
        tracker.ReconcileAfterDiagnosticReset(Room(1));
        Equal(1UL, tracker.Epoch);
    }),
    ("diagnostic reset advances changed room once", () =>
    {
        var tracker = new RoomEpochTracker();
        tracker.Observe(Room(1));
        tracker.MarkDiagnosticReset();
        tracker.ReconcileAfterDiagnosticReset(Room(2));
        Equal(2UL, tracker.Epoch);
        True(!tracker.TransitionPending);
    }),
    ("diagnostic reset during transition does not double advance", () =>
    {
        var tracker = new RoomEpochTracker();
        tracker.Observe(Room(1));
        True(tracker.BeginTransition());
        tracker.MarkDiagnosticReset();
        tracker.ReconcileAfterDiagnosticReset(Room(1));
        True(tracker.TransitionPending);
        Equal(2UL, tracker.Epoch);
        tracker.ReconcileAfterDiagnosticReset(Room(2));
        tracker.Complete(Room(2));
        Equal(2UL, tracker.Epoch);
    }),
    ("layer transition after reset advances same-room epoch", () =>
    {
        var tracker = new RoomEpochTracker();
        tracker.Observe(Room(1));
        tracker.MarkDiagnosticReset();
        True(tracker.BeginTransition());
        tracker.ReconcileAfterDiagnosticReset(Room(1));
        Equal(2UL, tracker.Epoch);
        True(tracker.TransitionPending);
        tracker.Complete(Room(1));
        True(!tracker.TransitionPending);
    }),
    ("canonical layout and golden hash", () =>
    {
        (ManagedInputFrame input, ManagedProxySnapshot snapshot) = Fixture();
        byte[] bytes = ManagedStateCodec.WriteCanonical(input, snapshot);
        Equal(ManagedStateCodec.CanonicalLength, bytes.Length);
        Equal((byte)'c', bytes[0]);
        Equal(ManagedStateCodec.SchemaVersion, bytes[19]);
        Equal((byte)5, bytes[57]);
        Equal(0xBB05D22920F8B29FUL, ManagedStateCodec.Hash(input, snapshot));
    }),
    ("span writer preserves canonical bytes", () =>
    {
        (ManagedInputFrame input, ManagedProxySnapshot snapshot) = Fixture();
        byte[] expected = ManagedStateCodec.WriteCanonical(input, snapshot);
        Span<byte> destination = stackalloc byte[ManagedStateCodec.CanonicalLength + 1];
        destination[^1] = 0xA5;
        ManagedStateCodec.WriteCanonical(input, snapshot, destination);
        True(expected.AsSpan().SequenceEqual(destination[..ManagedStateCodec.CanonicalLength]));
        Equal((byte)0xA5, destination[^1]);
        Reject(() => ManagedStateCodec.WriteCanonical(input, snapshot,
            new byte[ManagedStateCodec.CanonicalLength - 1]));
    }),
    ("hash is culture independent", () =>
    {
        (ManagedInputFrame input, ManagedProxySnapshot snapshot) = Fixture();
        ulong expected = ManagedStateCodec.Hash(input, snapshot);
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-EG");
            Equal(expected, ManagedStateCodec.Hash(input, snapshot));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }),
    ("field perturbation changes hash", () =>
    {
        (ManagedInputFrame input, ManagedProxySnapshot snapshot) = Fixture();
        ulong expected = ManagedStateCodec.Hash(input, snapshot);
        var changed = new ManagedProxySnapshot(snapshot.UpdateId, snapshot.RoomEpoch, snapshot.SessionPhase, snapshot.Room,
            snapshot.X + 1, snapshot.Y, snapshot.VelocityX, snapshot.VelocityY, snapshot.Initialized,
            snapshot.Grounded, snapshot.FacingLeft, snapshot.Crouched, snapshot.StandBlocked,
            snapshot.CoyoteUpdates, snapshot.JumpBufferUpdates, snapshot.Locomotion, snapshot.Animation,
            snapshot.AnimationFrame, snapshot.AnimationTick);
        True(expected != ManagedStateCodec.Hash(input, changed));
    }),
    ("identity mismatch rejected", () =>
    {
        (ManagedInputFrame input, ManagedProxySnapshot snapshot) = Fixture();
        var mismatched = new ManagedInputFrame(input.UpdateId + 1, input.RoomEpoch, input.Pressed, input.Tapped, input.CanControl);
        Reject(() => ManagedStateCodec.WriteCanonical(mismatched, snapshot));
    }),
    ("default state rejected", () => Reject(() => ManagedStateCodec.WriteCanonical(default, default))),
    ("invalid snapshot enum rejected", () =>
    {
        (ManagedInputFrame input, ManagedProxySnapshot snapshot) = Fixture();
        Reject(() => new ManagedProxySnapshot(input.UpdateId, input.RoomEpoch, ManagedMovementSessionPhase.Active,
            snapshot.Room, 0, 0, 0, 0,
            true, true, false, false, false, 0, 0, 8, 0, 0, 0));
    }),
    ("default and unknown snapshot phases rejected", () =>
    {
        (ManagedInputFrame input, ManagedProxySnapshot snapshot) = Fixture();
        Reject(() => new ManagedProxySnapshot(input.UpdateId, input.RoomEpoch, default,
            snapshot.Room, 0, 0, 0, 0, true, true, false, false, false, 0, 0, 0, 0, 0, 0));
        Reject(() => new ManagedProxySnapshot(input.UpdateId, input.RoomEpoch,
            (ManagedMovementSessionPhase)10, snapshot.Room, 0, 0, 0, 0, true, true, false,
            false, false, 0, 0, 0, 0, 0, 0));
        Reject(() => new ManagedProxySnapshot(input.UpdateId, input.RoomEpoch,
            (ManagedMovementSessionPhase)255, snapshot.Room, 0, 0, 0, 0, true, true, false,
            false, false, 0, 0, 0, 0, 0, 0));
    }),
    ("fixed replay is deterministic", () =>
    {
        ulong[] first = Replay();
        ulong[] second = Replay();
        True(first.SequenceEqual(second));
    }),
    ("warmed reducer and hashing integration allocates nothing", () =>
    {
        (ManagedInputFrame input, ManagedProxySnapshot snapshot) = Fixture();
        var reducer = new JumpForgivenessReducer();
        ulong digest = 0;
        for (int index = 0; index < 256; index++)
            digest ^= ReduceAndHash(reducer, input, snapshot, index);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++)
            digest ^= ReduceAndHash(reducer, input, snapshot, index);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        GC.KeepAlive(digest);
    })
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}
Console.WriteLine($"CoopManagedState: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static (ManagedInputFrame, ManagedProxySnapshot) Fixture()
{
    ManagedRoomKey room = new(1, 2, 3, -4, 5, 320, 240);
    var input = new ManagedInputFrame(7, 3, 0x1234, 0x0040, true);
    var snapshot = new ManagedProxySnapshot(7, 3, ManagedMovementSessionPhase.Active, room,
        0x12345678, -200, 0x18000, -0x48000,
        true, false, true, false, true, 4, 2, 2, 3, 1, 5);
    return (input, snapshot);
}

static ulong[] Replay()
{
    ManagedRoomKey room = Room(4);
    var hashes = new ulong[4];
    for (int index = 0; index < hashes.Length; index++)
    {
        long update = index + 1;
        var input = new ManagedInputFrame(update, 1, (ushort)(index << 4), (ushort)index, true);
        var snapshot = new ManagedProxySnapshot(update, 1, ManagedMovementSessionPhase.Active, room,
            index * 0x10000, 0, 0x18000, index,
            true, index == 0, false, false, false, 4 - index, index, 1, 1, index, index * 2);
        hashes[index] = ManagedStateCodec.Hash(input, snapshot);
    }
    return hashes;
}

static ulong ReduceAndHash(JumpForgivenessReducer reducer, ManagedInputFrame input,
    ManagedProxySnapshot snapshot, int index)
{
    JumpForgivenessTransition begin = reducer.BeginUpdate((index & 31) == 0, false, false);
    reducer.CompleteUpdate(begin.Continuation, false);
    var integrated = new ManagedProxySnapshot(snapshot.UpdateId, snapshot.RoomEpoch, snapshot.SessionPhase, snapshot.Room,
        snapshot.X, snapshot.Y, snapshot.VelocityX, snapshot.VelocityY, snapshot.Initialized,
        snapshot.Grounded, snapshot.FacingLeft, snapshot.Crouched, snapshot.StandBlocked,
        reducer.CoyoteUpdates, reducer.BufferUpdates, snapshot.Locomotion, snapshot.Animation,
        snapshot.AnimationFrame, snapshot.AnimationTick);
    return ManagedStateCodec.Hash(input, integrated);
}

static ManagedRoomKey Room(byte room) => new(1, room, 0, 0, 0, 256, 240);

static void Reject(Action action)
{
    try
    {
        action();
    }
    catch (ArgumentException)
    {
        return;
    }
    throw new InvalidOperationException("Invalid managed state was accepted.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}
