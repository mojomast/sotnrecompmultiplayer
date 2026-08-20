using System;

namespace CoopFeasibilityMod;

public readonly record struct AttackMarkerObservation(int Slot, bool Marked, uint Generation,
    uint RoomHash);
public readonly record struct AttackMarkerProjection(int Markers, int Orphans);

public static class AttackMarkerCensus
{
    public static AttackMarkerProjection Project(ReadOnlySpan<AttackMarkerObservation> observations,
        int ownedSlot, uint ownedGeneration, uint ownedRoomHash,
        int quarantineSlot, uint quarantineGeneration, uint quarantineRoomHash)
    {
        int markers = 0, orphans = 0;
        for (int i = 0; i < observations.Length; i++)
        {
            AttackMarkerObservation value = observations[i];
            if (!value.Marked) continue;
            markers++;
            bool owned = value.Slot == ownedSlot && value.Generation == ownedGeneration &&
                value.RoomHash == ownedRoomHash;
            bool quarantine = value.Slot == quarantineSlot && value.Generation == quarantineGeneration &&
                value.RoomHash == quarantineRoomHash;
            if (!owned && !quarantine) orphans++;
        }
        return new(markers, orphans);
    }
}
