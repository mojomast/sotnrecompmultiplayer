using System;

namespace CoopFeasibilityMod;

public readonly record struct NativeDropSlotObservation(ushort EntityId, uint Update, ushort Parameters,
    byte Step, byte HitFlags, short X, short Y)
{
    public bool IsPrize => EntityId == NativeDropObservationTracker.PrizeEntityId &&
        Update == NativeDropObservationTracker.PrizeUpdate;
    public bool IsEquipment => EntityId == NativeDropObservationTracker.EquipmentEntityId &&
        Update == NativeDropObservationTracker.EquipmentUpdate;
    public bool IsDrop => IsPrize || IsEquipment;
}

public readonly record struct NativeDropDiagnostics(ulong Scans, ulong Active, ulong MaximumActive,
    ulong PrizeSpawns, ulong EquipmentSpawns, ulong P2AssociatedSpawns, ulong AmbientSpawns,
    ulong AmbiguousSpawns, ulong CausalDefeatsWithoutDrop, ulong ObservedNativeExpEvents,
    ulong ObservedNativeExpDelta, ulong Collections, ulong Expirations, ulong LifecycleDisappears,
    ulong Reuses, ulong UnresolvedPickups, ulong Morphs, ulong OverflowEvents, bool Faulted);

// Fixed 32-slot NO0 observer. It never writes guest state and performs no per-window allocation.
public sealed class NativeDropObservationTracker
{
    public const int FirstSlot = 160;
    public const int SlotCount = 32;
    public const ushort PrizeEntityId = 3;
    public const ushort EquipmentEntityId = 10;
    public const uint PrizeUpdate = 0x801C9220;
    public const uint EquipmentUpdate = 0x801C9C34;
    public const int PositionTolerance = 24;
    private const int PendingCapacity = 4;
    private const int CausalWindowUpdates = 4;

    private readonly NativeDropSlotObservation[] _before = new NativeDropSlotObservation[SlotCount];
    private readonly NativeDropSlotObservation[] _after = new NativeDropSlotObservation[SlotCount];
    private readonly PendingDefeat[] _pending = new PendingDefeat[PendingCapacity];
    private bool _open;
    private bool _causalDefeatThisWindow;
    private bool _expRecordedThisWindow;
    private ulong _roomEpoch;
    private long? _expBefore;
    private ulong _scans, _active, _maximumActive, _prize, _equipment, _associated, _ambient;
    private ulong _ambiguous, _noDrop, _expEvents, _expDelta, _collections, _expirations;
    private ulong _lifecycle, _reuses, _unresolved, _morphs, _overflow;
    private bool _faulted;

    public NativeDropDiagnostics Diagnostics => new(_scans, _active, _maximumActive, _prize,
        _equipment, _associated, _ambient, _ambiguous, _noDrop, _expEvents, _expDelta,
        _collections, _expirations, _lifecycle, _reuses, _unresolved, _morphs, _overflow, _faulted);

    public void BeginWindow(ulong roomEpoch, long? nativeExp)
    {
        if (_open) { Fault(); return; }
        _open = true;
        _roomEpoch = roomEpoch;
        _expBefore = nativeExp;
        _causalDefeatThisWindow = false;
        _expRecordedThisWindow = false;
        Array.Clear(_before);
        Array.Clear(_after);
    }

    public void SetBefore(int slot, in NativeDropSlotObservation value) => Set(_before, slot, value);
    public void SetAfter(int slot, in NativeDropSlotObservation value) => Set(_after, slot, value);

    // The caller supplies this only for one target defeated by an exact-owned attack, proven by
    // either retained hit/cooldown state or unique cleared-slot plus native reward evidence.
    public void RecordUniqueCausalDefeat(ulong roomEpoch, int worldX, int worldY)
    {
        if (!_open || roomEpoch != _roomEpoch) { Fault(); return; }
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i].Active) continue;
            _pending[i] = new PendingDefeat(true, roomEpoch, worldX, worldY, CausalWindowUpdates);
            _causalDefeatThisWindow = true;
            return;
        }
        Bump(ref _overflow);
        _faulted = true;
    }

    public void RecordAmbiguousOverflowWindow(ulong roomEpoch)
    {
        if (!_open || roomEpoch != _roomEpoch) { Fault(); return; }
        Bump(ref _ambiguous);
        Bump(ref _overflow);
    }

    public void CompleteWindow(long? nativeExp)
    {
        if (!_open) { Fault(); return; }
        _open = false;
        Bump(ref _scans);
        int active = 0, newDrops = 0, compatiblePairs = 0, compatibleDrop = -1, compatiblePending = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            NativeDropSlotObservation before = _before[i], after = _after[i];
            if (after.IsDrop) active++;
            bool morph = before.IsEquipment && after.IsPrize;
            bool exactSame = before.IsDrop && after.IsDrop && before.EntityId == after.EntityId &&
                before.Update == after.Update && before.Parameters == after.Parameters;
            if (morph) Bump(ref _morphs);
            if (after.IsDrop && !exactSame && !morph)
            {
                newDrops++;
                if (after.IsPrize) Bump(ref _prize); else Bump(ref _equipment);
                for (int p = 0; p < _pending.Length; p++)
                {
                    PendingDefeat pending = _pending[p];
                    if (!pending.Active || pending.RoomEpoch != _roomEpoch ||
                        Math.Abs(after.X - pending.X) > PositionTolerance ||
                        Math.Abs(after.Y - pending.Y) > PositionTolerance) continue;
                    compatiblePairs++;
                    compatibleDrop = i;
                    compatiblePending = p;
                }
            }
            ClassifyDisappearance(before, after, nativeExp);
        }
        _active = (ulong)active;
        if (_active > _maximumActive) _maximumActive = _active;
        if (_causalDefeatThisWindow) ObserveExp(nativeExp);

        if (newDrops != 0)
        {
            if (newDrops == 1 && compatiblePairs == 1)
            {
                Bump(ref _associated);
                _pending[compatiblePending] = default;
                _ = compatibleDrop;
            }
            else if (compatiblePairs != 0)
            {
                Bump(ref _ambiguous);
            }
            else
            {
                Add(ref _ambient, (ulong)newDrops);
            }
        }

        for (int i = 0; i < _pending.Length; i++)
        {
            PendingDefeat pending = _pending[i];
            if (!pending.Active) continue;
            if (pending.RoomEpoch != _roomEpoch || pending.Remaining <= 1)
            {
                Bump(ref _noDrop);
                _pending[i] = default;
            }
            else _pending[i] = pending with { Remaining = pending.Remaining - 1 };
        }
    }

    public void Cancel()
    {
        _open = false;
        _causalDefeatThisWindow = false;
        _expRecordedThisWindow = false;
        _expBefore = null;
        Array.Clear(_before); Array.Clear(_after); Array.Clear(_pending);
    }

    public void ResetDiagnostics()
    {
        Cancel();
        _scans = _active = _maximumActive = _prize = _equipment = _associated = _ambient = 0;
        _ambiguous = _noDrop = _expEvents = _expDelta = _collections = _expirations = 0;
        _lifecycle = _reuses = _unresolved = _morphs = _overflow = 0;
        _faulted = false;
    }

    private void Set(NativeDropSlotObservation[] values, int slot, in NativeDropSlotObservation value)
    {
        if (!_open || slot is < FirstSlot or >= FirstSlot + SlotCount) { Fault(); return; }
        values[slot - FirstSlot] = value;
    }

    private void ClassifyDisappearance(in NativeDropSlotObservation before,
        in NativeDropSlotObservation after, long? nativeExp)
    {
        if (!before.IsDrop || (after.IsDrop && before.EntityId == after.EntityId &&
            before.Update == after.Update && before.Parameters == after.Parameters) ||
            (before.IsEquipment && after.IsPrize)) return;
        if (after.EntityId != 0 || after.Update != 0) { Bump(ref _reuses); return; }
        bool pickupEvidence = before.Step == 5 || before.HitFlags != 0;
        bool reward = Delta(nativeExp, out ulong delta);
        if (pickupEvidence && reward)
        {
            Bump(ref _collections); RecordExp(delta);
        }
        else if (pickupEvidence) Bump(ref _unresolved);
        else if (before.Step >= 6) Bump(ref _expirations);
        else Bump(ref _lifecycle);
    }

    private void ObserveExp(long? nativeExp)
    {
        if (!Delta(nativeExp, out ulong delta)) return;
        RecordExp(delta);
    }

    private void RecordExp(ulong delta)
    {
        if (_expRecordedThisWindow) return;
        _expRecordedThisWindow = true;
        Bump(ref _expEvents); Add(ref _expDelta, delta);
    }

    private bool Delta(long? nativeExp, out ulong delta)
    {
        delta = 0;
        if (_expBefore is not long before || nativeExp is not long after || after <= before) return false;
        try { delta = checked((ulong)(after - before)); return true; }
        catch (OverflowException) { Fault(); return false; }
    }

    private void Fault() { Bump(ref _overflow); _faulted = true; }
    private void Bump(ref ulong value) => Add(ref value, 1);
    private void Add(ref ulong value, ulong amount)
    {
        if (ulong.MaxValue - value < amount) { _faulted = true; _overflow = ulong.MaxValue; return; }
        value += amount;
    }

    private readonly record struct PendingDefeat(bool Active, ulong RoomEpoch, int X, int Y,
        int Remaining);
}
