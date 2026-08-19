using System;
using System.Threading;

namespace CoopFeasibilityMod;

public enum AttackPublicationPhase { Empty, Live, Observed, RolledBack, RetryableQuarantine, ResidualStopped, MutationStopped }
public enum AttackSlotObservation { Free, Exact, Reused }
public enum AttackUnloadResult { NoLease, Cleaned, ResidualMemoryUnavailable, ResidualFault, OwnershipMismatch }

public readonly struct AttackPublicationTuple
{
    public readonly int Slot;
    public readonly uint Generation;
    public readonly uint RoomHash;

    public AttackPublicationTuple(int slot, uint generation, uint roomHash)
    {
        if (slot is < 17 or >= 48) throw new ArgumentOutOfRangeException(nameof(slot));
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        Slot = slot;
        Generation = generation;
        RoomHash = roomHash;
    }
}

public struct AttackPublicationState
{
    internal ulong MachineOwner;
    internal ulong Revision;
    public AttackPublicationPhase Phase;
    public AttackPublicationTuple Tuple;
    public long ArmedMainGeneration;
    public long ArmedUpdateGeneration;
    public bool OwnershipPublished;
    public bool ReservationAuthorized;
    public bool SlotTouched;
    public bool ExternalExecution;
    public int TupleFieldsWritten;
    public int RollbackAttempts;
    public bool RollbackAuthorized;
    public bool ResidualEvidence;
    public bool OwnershipMismatchEvidence;
}

/// <summary>All native memory access and guest dispatch used by the publication transaction.</summary>
public interface IAttackPublicationAdapter
{
    AttackSlotObservation Probe(in AttackPublicationTuple tuple);
    void ClearReservedSlot(in AttackPublicationTuple tuple);
    void WriteOwnershipField(in AttackPublicationTuple tuple, int field);
    void WritePayload(in AttackPublicationTuple tuple);
    void WriteLiveField(in AttackPublicationTuple tuple, int field);
    void CallGuest(in AttackPublicationTuple tuple);
    void ReadPostCall(in AttackPublicationTuple tuple);
    void ObserveNative(in AttackPublicationTuple tuple);
    void DeactivateLiveField(in AttackPublicationTuple tuple, int field);
    void MutateProjectile(in AttackPublicationTuple tuple);
    void ClearOwnedSlot(in AttackPublicationTuple tuple);
}

/// <summary>
/// Executes the safety-critical publication order. Adapter methods retain native operation granularity;
/// the journal authorizes same-call rollback even when only part of the ownership tuple was written.
/// </summary>
public static class AttackPublicationPolicy
{
    private static long s_nextMachineOwner;

    public static AttackPublicationState Initial() => new()
    {
        Phase = AttackPublicationPhase.Empty,
        MachineOwner = NextMachineOwner(),
        Revision = 1
    };

    internal static void ValidateResetPreparation(AttackPublicationState state)
    {
        if (state.MachineOwner == 0 || state.Revision == 0)
            throw new InvalidOperationException("Attack publication owner or revision is invalid.");
        _ = NextRevision(state.Revision);
    }

    internal static bool MatchesResetPreparation(AttackPublicationState state,
        ulong machineOwner, ulong revision) => state.MachineOwner == machineOwner && state.Revision == revision;

    internal static AttackPublicationState CommitResetTransition(AttackPublicationState state,
        ulong machineOwner, ulong revision, AttackPublicationPhase phase)
    {
        if (!MatchesResetPreparation(state, machineOwner, revision)) return state;
        state.Phase = phase;
        state.Revision = NextRevision(revision);
        if (phase == AttackPublicationPhase.Empty)
        {
            state.OwnershipPublished = false;
            state.ReservationAuthorized = false;
            state.SlotTouched = false;
            state.ExternalExecution = false;
            state.TupleFieldsWritten = 0;
            state.RollbackAttempts = 0;
            state.RollbackAuthorized = false;
            state.ResidualEvidence = false;
            state.OwnershipMismatchEvidence = false;
        }
        return state;
    }

    public static bool Publish(ref AttackPublicationState state, IAttackPublicationAdapter adapter,
        in AttackPublicationTuple tuple, long mainGeneration, long updateGeneration)
    {
        if (state.Phase != AttackPublicationPhase.Empty)
            return ResidualStop(ref state);
        ulong nextRevision = NextRevision(state.Revision);
        state.Tuple = tuple;
        state.ArmedMainGeneration = mainGeneration;
        state.ArmedUpdateGeneration = updateGeneration;
        state.OwnershipPublished = false;
        state.ReservationAuthorized = false;
        state.SlotTouched = false;
        state.ExternalExecution = false;
        state.TupleFieldsWritten = 0;
        state.RollbackAttempts = 0;
        state.RollbackAuthorized = false;
        state.ResidualEvidence = false;
        state.OwnershipMismatchEvidence = false;
        try
        {
            if (adapter.Probe(tuple) != AttackSlotObservation.Free) return Stop(ref state);
            state.ReservationAuthorized = true;
            state.SlotTouched = true;
            adapter.ClearReservedSlot(tuple);
            for (int field = 0; field < 3; field++)
            {
                adapter.WriteOwnershipField(tuple, field);
                state.TupleFieldsWritten++;
            }
            state.OwnershipPublished = true;
            adapter.WritePayload(tuple);
            for (int field = 0; field < 3; field++) adapter.WriteLiveField(tuple, field);
            // A throwing guest dispatch may already have executed native code.
            state.ExternalExecution = true;
            adapter.CallGuest(tuple);
            adapter.ReadPostCall(tuple);
            state.Phase = AttackPublicationPhase.Live;
            state.Revision = nextRevision;
            return true;
        }
        catch
        {
            RollbackPublication(ref state, adapter);
            return false;
        }
    }

    public static bool Observe(ref AttackPublicationState state, IAttackPublicationAdapter adapter,
        long mainGeneration)
    {
        if (state.Phase != AttackPublicationPhase.Live || mainGeneration != state.ArmedMainGeneration + 1)
            return RejectOwnedEvent(ref state, adapter);
        ulong nextRevision = NextRevision(state.Revision);
        try
        {
            AttackSlotObservation observation = adapter.Probe(state.Tuple);
            if (observation != AttackSlotObservation.Exact)
            {
                return NonExactOwned(ref state, observation);
            }
            adapter.ObserveNative(state.Tuple);
            state.Phase = AttackPublicationPhase.Observed;
            state.Revision = nextRevision;
            return true;
        }
        catch
        {
            QuarantineAfterFault(ref state, adapter);
            return false;
        }
    }

    public static bool RepublishProjectile(ref AttackPublicationState state, IAttackPublicationAdapter adapter,
        long mainGeneration, long updateGeneration)
    {
        if (state.Phase != AttackPublicationPhase.Observed ||
            mainGeneration != state.ArmedMainGeneration + 1 ||
            updateGeneration != state.ArmedUpdateGeneration + 1)
            return RejectOwnedEvent(ref state, adapter);
        ulong nextRevision = NextRevision(state.Revision);
        try
        {
            RequireExact(ref state, adapter);
            for (int field = 0; field < 3; field++) adapter.DeactivateLiveField(state.Tuple, field);
            RequireExact(ref state, adapter);
            adapter.MutateProjectile(state.Tuple);
            RequireExact(ref state, adapter);
            for (int field = 0; field < 3; field++) adapter.WriteLiveField(state.Tuple, field);
            state.ArmedMainGeneration = mainGeneration;
            state.ArmedUpdateGeneration = updateGeneration;
            state.Phase = AttackPublicationPhase.Live;
            state.Revision = nextRevision;
            return true;
        }
        catch (OwnershipMismatchException)
        {
            return false; // RequireExact already stopped the machine; never roll back a reused slot.
        }
        catch
        {
            QuarantineAfterFault(ref state, adapter);
            return false;
        }
    }

    public static bool Cleanup(ref AttackPublicationState state, IAttackPublicationAdapter adapter)
    {
        if (state.Phase is not (AttackPublicationPhase.Live or AttackPublicationPhase.Observed))
            return state.Phase == AttackPublicationPhase.Empty || ResidualStop(ref state);
        ulong nextRevision = NextRevision(state.Revision);
        try
        {
            RequireExact(ref state, adapter);
            state.RollbackAuthorized = true;
            for (int field = 0; field < 3; field++) adapter.DeactivateLiveField(state.Tuple, field);
            RequireExact(ref state, adapter);
            adapter.ClearOwnedSlot(state.Tuple);
            ResetToEmpty(ref state, nextRevision);
            return true;
        }
        catch (OwnershipMismatchException)
        {
            return false;
        }
        catch
        {
            if (state.RollbackAuthorized)
                return CompleteAuthorizedClear(ref state, adapter, nextRevision);
            QuarantineAfterFault(ref state, adapter);
            return false;
        }
    }

    public static bool RetryQuarantine(ref AttackPublicationState state, IAttackPublicationAdapter adapter)
    {
        if (state.Phase != AttackPublicationPhase.RetryableQuarantine) return false;
        ulong nextRevision = NextRevision(state.Revision);
        try
        {
            AttackSlotObservation observation = adapter.Probe(state.Tuple);
            if (observation == AttackSlotObservation.Reused)
            {
                state.ResidualEvidence = true;
                state.OwnershipMismatchEvidence = true;
                return Stop(ref state);
            }
            if (observation == AttackSlotObservation.Exact)
            {
                state.RollbackAuthorized = true;
                return CompleteAuthorizedClear(ref state, adapter, nextRevision);
            }
            ResetToEmpty(ref state, nextRevision);
            return true;
        }
        catch (OwnershipMismatchException)
        {
            return false;
        }
        catch
        {
            state.Phase = AttackPublicationPhase.RetryableQuarantine;
            return false;
        }
    }

    public static AttackUnloadResult Unload(ref AttackPublicationState state,
        IAttackPublicationAdapter? adapter, bool memoryAvailable)
    {
        if (state.Phase is AttackPublicationPhase.Empty or AttackPublicationPhase.RolledBack)
            return AttackUnloadResult.NoLease;
        if (!memoryAvailable || adapter == null)
        {
            state.ResidualEvidence = true;
            state.Phase = AttackPublicationPhase.ResidualStopped;
            return AttackUnloadResult.ResidualMemoryUnavailable;
        }

        if (state.Phase is AttackPublicationPhase.Live or AttackPublicationPhase.Observed)
        {
            if (Cleanup(ref state, adapter)) return AttackUnloadResult.Cleaned;
            if (state.Phase is AttackPublicationPhase.MutationStopped or AttackPublicationPhase.ResidualStopped)
                return state.OwnershipMismatchEvidence
                    ? AttackUnloadResult.OwnershipMismatch : AttackUnloadResult.ResidualFault;
        }
        if (state.Phase == AttackPublicationPhase.RetryableQuarantine)
        {
            if (RetryQuarantine(ref state, adapter)) return AttackUnloadResult.Cleaned;
            if (state.Phase is AttackPublicationPhase.MutationStopped or AttackPublicationPhase.ResidualStopped)
                return state.OwnershipMismatchEvidence
                    ? AttackUnloadResult.OwnershipMismatch : AttackUnloadResult.ResidualFault;
            // One bounded same-call retry. Never leave authority for a later unload callback.
            if (RetryQuarantine(ref state, adapter)) return AttackUnloadResult.Cleaned;
        }
        state.ResidualEvidence = true;
        state.Phase = AttackPublicationPhase.ResidualStopped;
        return AttackUnloadResult.ResidualFault;
    }

    private static void RequireExact(ref AttackPublicationState state, IAttackPublicationAdapter adapter)
    {
        AttackSlotObservation observation = adapter.Probe(state.Tuple);
        if (observation == AttackSlotObservation.Exact) return;
        NonExactOwned(ref state, observation);
        throw new OwnershipMismatchException();
    }

    private static void QuarantineAfterFault(ref AttackPublicationState state, IAttackPublicationAdapter adapter)
    {
        if (!state.OwnershipPublished)
        {
            state.Phase = AttackPublicationPhase.RetryableQuarantine;
            return;
        }
        try
        {
            AttackSlotObservation observation = adapter.Probe(state.Tuple);
            state.Phase = observation switch
            {
                AttackSlotObservation.Exact => AttackPublicationPhase.RetryableQuarantine,
                AttackSlotObservation.Free => AttackPublicationPhase.RolledBack,
                _ => AttackPublicationPhase.MutationStopped
            };
            if (observation == AttackSlotObservation.Reused)
            {
                state.ResidualEvidence = true;
                state.OwnershipMismatchEvidence = true;
            }
        }
        catch
        {
            state.Phase = AttackPublicationPhase.RetryableQuarantine;
        }
    }

    private static void RollbackPublication(ref AttackPublicationState state, IAttackPublicationAdapter adapter)
    {
        if (!state.SlotTouched)
        {
            state.Phase = AttackPublicationPhase.RolledBack;
            return;
        }
        for (int attempt = 0; attempt < 2; attempt++)
        {
            state.RollbackAttempts++;
            try
            {
                // Before guest execution, the successful free-slot probe plus operation journal is
                // the authorization. Afterwards only the complete exact tuple can authorize writes.
                if (state.ExternalExecution && !state.RollbackAuthorized)
                {
                    AttackSlotObservation observation = adapter.Probe(state.Tuple);
                    if (observation == AttackSlotObservation.Free)
                    {
                        state.Phase = AttackPublicationPhase.RolledBack;
                        state.OwnershipPublished = false;
                        return;
                    }
                    if (observation == AttackSlotObservation.Reused)
                    {
                        state.ResidualEvidence = true;
                        state.OwnershipMismatchEvidence = true;
                        Stop(ref state);
                        return;
                    }
                }
                state.RollbackAuthorized = true;
                for (int field = 0; field < 3; field++) adapter.DeactivateLiveField(state.Tuple, field);
                adapter.ClearOwnedSlot(state.Tuple);
                state.Phase = AttackPublicationPhase.RolledBack;
                state.OwnershipPublished = false;
                return;
            }
            catch
            {
                // Retry synchronously while the journal's authority is still local to this call.
            }
        }
        state.ResidualEvidence = true;
        state.Phase = AttackPublicationPhase.ResidualStopped;
    }

    private static bool CompleteAuthorizedClear(ref AttackPublicationState state,
        IAttackPublicationAdapter adapter, ulong nextRevision)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            state.RollbackAttempts++;
            try
            {
                for (int field = 0; field < 3; field++) adapter.DeactivateLiveField(state.Tuple, field);
                adapter.ClearOwnedSlot(state.Tuple);
                ResetToEmpty(ref state, nextRevision);
                return true;
            }
            catch
            {
                // The exact probe authorized completion for this bounded synchronous operation.
            }
        }
        state.ResidualEvidence = true;
        state.RollbackAuthorized = false;
        state.Phase = AttackPublicationPhase.RetryableQuarantine;
        return false;
    }

    private static bool RejectOwnedEvent(ref AttackPublicationState state,
        IAttackPublicationAdapter adapter)
    {
        if (state.Phase is not (AttackPublicationPhase.Live or AttackPublicationPhase.Observed))
            return ResidualStop(ref state);
        ulong nextRevision = NextRevision(state.Revision);
        try
        {
            AttackSlotObservation observation = adapter.Probe(state.Tuple);
            if (observation != AttackSlotObservation.Exact)
                return NonExactOwned(ref state, observation);
            state.RollbackAuthorized = true;
            bool cleaned = CompleteAuthorizedClear(ref state, adapter, nextRevision);
            if (cleaned) state.Phase = AttackPublicationPhase.RolledBack;
            return false;
        }
        catch
        {
            state.RollbackAuthorized = false;
            state.Phase = AttackPublicationPhase.RetryableQuarantine;
            return false;
        }
    }

    private static bool NonExactOwned(ref AttackPublicationState state, AttackSlotObservation observation)
    {
        if (observation == AttackSlotObservation.Free)
        {
            state.Phase = AttackPublicationPhase.RolledBack;
            return false;
        }
        state.ResidualEvidence = true;
        state.OwnershipMismatchEvidence = true;
        return Stop(ref state);
    }

    private static bool Stop(ref AttackPublicationState state)
    {
        state.Phase = AttackPublicationPhase.MutationStopped;
        return false;
    }

    private static bool ResidualStop(ref AttackPublicationState state)
    {
        state.ResidualEvidence = true;
        state.Phase = AttackPublicationPhase.ResidualStopped;
        return false;
    }

    private static void ResetToEmpty(ref AttackPublicationState state, ulong nextRevision)
    {
        state.Phase = AttackPublicationPhase.Empty;
        state.Revision = nextRevision;
        state.OwnershipPublished = false;
        state.ReservationAuthorized = false;
        state.SlotTouched = false;
        state.ExternalExecution = false;
        state.TupleFieldsWritten = 0;
        state.RollbackAttempts = 0;
        state.RollbackAuthorized = false;
        state.ResidualEvidence = false;
        state.OwnershipMismatchEvidence = false;
    }

    private static ulong NextRevision(ulong revision) => revision == ulong.MaxValue
        ? throw new InvalidOperationException("Attack publication revision space is exhausted.")
        : revision + 1;

    private static ulong NextMachineOwner()
    {
        long owner = Interlocked.Increment(ref s_nextMachineOwner);
        if (owner <= 0) throw new InvalidOperationException("Attack publication owner space is exhausted.");
        return unchecked((ulong)owner);
    }

    private sealed class OwnershipMismatchException : Exception { }
}
