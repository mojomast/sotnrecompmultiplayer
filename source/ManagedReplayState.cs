using System;
using System.Text;

namespace CoopFeasibilityMod;

public readonly struct ManagedRoomKey : IEquatable<ManagedRoomKey>
{
    public readonly byte Stage;
    public readonly byte Room;
    public readonly byte Area;
    public readonly int Left;
    public readonly int Top;
    public readonly int Right;
    public readonly int Bottom;

    public ManagedRoomKey(byte stage, byte room, byte area, int left, int top, int right, int bottom)
    {
        Stage = stage;
        Room = room;
        Area = area;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public bool Equals(ManagedRoomKey other) =>
        Stage == other.Stage && Room == other.Room && Area == other.Area && Left == other.Left &&
        Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public bool SameRoomAs(ManagedRoomKey other) =>
        Stage == other.Stage && Room == other.Room && Area == other.Area;

    public override bool Equals(object? obj) => obj is ManagedRoomKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Stage, Room, Area, Left, Top, Right, Bottom);
}

public sealed class RoomEpochTracker
{
    private bool _diagnosticResetPending;

    public ulong Epoch { get; private set; }
    public bool Known { get; private set; }
    public bool TransitionPending { get; private set; }
    public ManagedRoomKey Current { get; private set; }

    public void Observe(ManagedRoomKey room)
    {
        if (Epoch == 0) Epoch = 1;
        Current = room;
        Known = true;
    }

    public bool BeginTransition()
    {
        if (!Known || TransitionPending) return false;
        Epoch = Next(Epoch);
        TransitionPending = true;
        return true;
    }

    public void Complete(ManagedRoomKey room)
    {
        Observe(room);
        TransitionPending = false;
    }

    public void InvalidateForPlayerReload()
    {
        Epoch = Next(Epoch);
        Known = false;
        TransitionPending = true;
        Current = default;
    }

    public void MarkDiagnosticReset() => _diagnosticResetPending = true;

    public void ReconcileAfterDiagnosticReset(ManagedRoomKey room)
    {
        if (!_diagnosticResetPending)
        {
            Complete(room);
            return;
        }

        _diagnosticResetPending = false;
        if (!Known)
        {
            Observe(room);
            TransitionPending = false;
            return;
        }
        if (TransitionPending)
        {
            if (!Current.Equals(room)) Complete(room);
            return;
        }
        if (!Current.Equals(room)) Epoch = Next(Epoch);
        Complete(room);
    }

    private static ulong Next(ulong value) => value == ulong.MaxValue
        ? throw new InvalidOperationException("Room epoch exhausted.")
        : value + 1;
}

public readonly struct ManagedInputFrame
{
    public readonly long UpdateId;
    public readonly ulong RoomEpoch;
    public readonly ushort Pressed;
    public readonly ushort Tapped;
    public readonly bool CanControl;

    public ManagedInputFrame(long updateId, ulong roomEpoch, ushort pressed, ushort tapped, bool canControl)
    {
        if (updateId <= 0) throw new ArgumentOutOfRangeException(nameof(updateId));
        if (roomEpoch == 0) throw new ArgumentOutOfRangeException(nameof(roomEpoch));
        UpdateId = updateId;
        RoomEpoch = roomEpoch;
        Pressed = pressed;
        Tapped = tapped;
        CanControl = canControl;
    }
}

public readonly struct ManagedProxySnapshot
{
    public readonly long UpdateId;
    public readonly ulong RoomEpoch;
    public readonly ManagedMovementSessionPhase SessionPhase;
    public readonly ManagedRoomKey Room;
    public readonly int X;
    public readonly int Y;
    public readonly int VelocityX;
    public readonly int VelocityY;
    public readonly bool Initialized;
    public readonly bool Grounded;
    public readonly bool FacingLeft;
    public readonly bool Crouched;
    public readonly bool StandBlocked;
    public readonly int CoyoteUpdates;
    public readonly int JumpBufferUpdates;
    public readonly byte Locomotion;
    public readonly byte Animation;
    public readonly int AnimationFrame;
    public readonly int AnimationTick;

    public ManagedProxySnapshot(long updateId, ulong roomEpoch, ManagedMovementSessionPhase sessionPhase,
        ManagedRoomKey room,
        int x, int y, int velocityX, int velocityY, bool initialized, bool grounded, bool facingLeft,
        bool crouched, bool standBlocked, int coyoteUpdates, int jumpBufferUpdates, byte locomotion,
        byte animation, int animationFrame, int animationTick)
    {
        if (updateId <= 0) throw new ArgumentOutOfRangeException(nameof(updateId));
        if (roomEpoch == 0) throw new ArgumentOutOfRangeException(nameof(roomEpoch));
        if (sessionPhase is < ManagedMovementSessionPhase.Dormant or > ManagedMovementSessionPhase.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(sessionPhase));
        if (coyoteUpdates < 0 || jumpBufferUpdates < 0 || animationFrame < 0 || animationTick < 0)
            throw new ArgumentOutOfRangeException(nameof(coyoteUpdates));
        if (locomotion > 7 || animation > 13) throw new ArgumentOutOfRangeException(nameof(locomotion));
        UpdateId = updateId;
        RoomEpoch = roomEpoch;
        SessionPhase = sessionPhase;
        Room = room;
        X = x;
        Y = y;
        VelocityX = velocityX;
        VelocityY = velocityY;
        Initialized = initialized;
        Grounded = grounded;
        FacingLeft = facingLeft;
        Crouched = crouched;
        StandBlocked = standBlocked;
        CoyoteUpdates = coyoteUpdates;
        JumpBufferUpdates = jumpBufferUpdates;
        Locomotion = locomotion;
        Animation = animation;
        AnimationFrame = animationFrame;
        AnimationTick = animationTick;
    }
}

public static class ManagedStateCodec
{
    public const byte SchemaVersion = 2;
    public const int CanonicalLength = 116;
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("coop-managed-state");

    public static byte[] WriteCanonical(ManagedInputFrame input, ManagedProxySnapshot snapshot)
    {
        var bytes = new byte[CanonicalLength];
        WriteCanonical(input, snapshot, bytes);
        return bytes;
    }

    public static void WriteCanonical(ManagedInputFrame input, ManagedProxySnapshot snapshot,
        Span<byte> destination)
    {
        if (input.UpdateId <= 0 || input.RoomEpoch == 0 || snapshot.UpdateId <= 0 || snapshot.RoomEpoch == 0 ||
            snapshot.CoyoteUpdates < 0 || snapshot.JumpBufferUpdates < 0 || snapshot.AnimationFrame < 0 ||
            snapshot.AnimationTick < 0 || snapshot.Locomotion > 7 || snapshot.Animation > 13 ||
            snapshot.SessionPhase is < ManagedMovementSessionPhase.Dormant or > ManagedMovementSessionPhase.Unloaded)
            throw new ArgumentException("Managed input or snapshot contains invalid values.");
        if (input.UpdateId != snapshot.UpdateId || input.RoomEpoch != snapshot.RoomEpoch)
            throw new ArgumentException("Input and snapshot identities must match.");
        if (destination.Length < CanonicalLength)
            throw new ArgumentException($"Canonical destination must be at least {CanonicalLength} bytes.",
                nameof(destination));

        Span<byte> bytes = destination[..CanonicalLength];
        int offset = 0;
        Domain.CopyTo(bytes);
        offset += Domain.Length;
        bytes[offset++] = 0;
        bytes[offset++] = SchemaVersion;
        WriteInt64(bytes, ref offset, input.UpdateId);
        WriteUInt64(bytes, ref offset, input.RoomEpoch);
        WriteUInt16(bytes, ref offset, input.Pressed);
        WriteUInt16(bytes, ref offset, input.Tapped);
        WriteBool(bytes, ref offset, input.CanControl);
        WriteInt64(bytes, ref offset, snapshot.UpdateId);
        WriteUInt64(bytes, ref offset, snapshot.RoomEpoch);
        bytes[offset++] = (byte)snapshot.SessionPhase;
        bytes[offset++] = snapshot.Room.Stage;
        bytes[offset++] = snapshot.Room.Room;
        bytes[offset++] = snapshot.Room.Area;
        WriteInt32(bytes, ref offset, snapshot.Room.Left);
        WriteInt32(bytes, ref offset, snapshot.Room.Top);
        WriteInt32(bytes, ref offset, snapshot.Room.Right);
        WriteInt32(bytes, ref offset, snapshot.Room.Bottom);
        WriteInt32(bytes, ref offset, snapshot.X);
        WriteInt32(bytes, ref offset, snapshot.Y);
        WriteInt32(bytes, ref offset, snapshot.VelocityX);
        WriteInt32(bytes, ref offset, snapshot.VelocityY);
        WriteBool(bytes, ref offset, snapshot.Initialized);
        WriteBool(bytes, ref offset, snapshot.Grounded);
        WriteBool(bytes, ref offset, snapshot.FacingLeft);
        WriteBool(bytes, ref offset, snapshot.Crouched);
        WriteBool(bytes, ref offset, snapshot.StandBlocked);
        WriteInt32(bytes, ref offset, snapshot.CoyoteUpdates);
        WriteInt32(bytes, ref offset, snapshot.JumpBufferUpdates);
        bytes[offset++] = snapshot.Locomotion;
        bytes[offset++] = snapshot.Animation;
        WriteInt32(bytes, ref offset, snapshot.AnimationFrame);
        WriteInt32(bytes, ref offset, snapshot.AnimationTick);
        if (offset != CanonicalLength) throw new InvalidOperationException("Managed state layout length drifted.");
    }

    public static ulong Hash(ManagedInputFrame input, ManagedProxySnapshot snapshot)
    {
        Span<byte> bytes = stackalloc byte[CanonicalLength];
        WriteCanonical(input, snapshot, bytes);
        ulong hash = 14695981039346656037UL;
        foreach (byte value in bytes) hash = (hash ^ value) * 1099511628211UL;
        return hash;
    }

    private static void WriteBool(Span<byte> bytes, ref int offset, bool value) =>
        bytes[offset++] = value ? (byte)1 : (byte)0;

    private static void WriteUInt16(Span<byte> bytes, ref int offset, ushort value)
    {
        bytes[offset++] = (byte)value;
        bytes[offset++] = (byte)(value >> 8);
    }

    private static void WriteInt32(Span<byte> bytes, ref int offset, int value) =>
        WriteUInt32(bytes, ref offset, unchecked((uint)value));

    private static void WriteUInt32(Span<byte> bytes, ref int offset, uint value)
    {
        for (int shift = 0; shift < 32; shift += 8) bytes[offset++] = (byte)(value >> shift);
    }

    private static void WriteInt64(Span<byte> bytes, ref int offset, long value) =>
        WriteUInt64(bytes, ref offset, unchecked((ulong)value));

    private static void WriteUInt64(Span<byte> bytes, ref int offset, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8) bytes[offset++] = (byte)(value >> shift);
    }
}
