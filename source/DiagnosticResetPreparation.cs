using System;

namespace CoopFeasibilityMod;

public readonly struct DiagnosticResetPreparation
{
    public readonly int NextDiagnosticGeneration;
    public readonly ManagedMovementDiagnosticResetCommand Session;
    public readonly AttackPublicationResetCommand Publication;
    public readonly AttackLeasePreparedTransition ReadyLease;
    public readonly AttackLeasePreparedTransition CleanedLease;
    public readonly AttackLeasePreparedTransition ReusedLease;
    public readonly JumpForgivenessClearPreparation Jump;
    public readonly ManagedStanceInitialization Stance;
    public readonly ManagedLocomotionDiagnosticReset Locomotion;
    public readonly ReconstructionPolicyReset Reconstruction;

    internal DiagnosticResetPreparation(int nextDiagnosticGeneration,
        ManagedMovementDiagnosticResetCommand session, AttackPublicationResetCommand publication,
        AttackLeasePreparedTransition readyLease, AttackLeasePreparedTransition cleanedLease,
        AttackLeasePreparedTransition reusedLease, JumpForgivenessClearPreparation jump,
        ManagedStanceInitialization stance, ManagedLocomotionDiagnosticReset locomotion,
        ReconstructionPolicyReset reconstruction)
    {
        NextDiagnosticGeneration = nextDiagnosticGeneration;
        Session = session;
        Publication = publication;
        ReadyLease = readyLease;
        CleanedLease = cleanedLease;
        ReusedLease = reusedLease;
        Jump = jump;
        Stance = stance;
        Locomotion = locomotion;
        Reconstruction = reconstruction;
    }

    public AttackLeasePreparedTransition LeaseFor(AttackResetPreflightOutcome outcome) => outcome switch
    {
        AttackResetPreflightOutcome.Cleaned => CleanedLease,
        AttackResetPreflightOutcome.CarryMutationStopped => ReusedLease,
        _ => ReadyLease
    };
}

public static class DiagnosticResetPreparationPolicy
{
    public static bool TryPrepare(int diagnosticGeneration, ManagedMovementSessionReducer session,
        AttackLeaseState lease, AttackPublicationState publication, JumpForgivenessReducer jump,
        ManagedStanceReducer stance, ManagedLocomotionReducer locomotion,
        ReconstructionPolicyReducer reconstruction,
        out DiagnosticResetPreparation preparation)
    {
        preparation = default;
        try
        {
            if (diagnosticGeneration < 0 || diagnosticGeneration == int.MaxValue) return false;
            int nextGeneration = checked(diagnosticGeneration + 1);
            ManagedMovementDiagnosticResetCommand sessionCommand = session.PrepareDiagnosticReset();
            AttackPublicationResetCommand publicationCommand = AttackResetPreflight.Prepare(publication);
            JumpForgivenessClearPreparation jumpCommand = jump.PrepareClear();
            ManagedStanceInitialization stanceCommand = stance.PrepareInitialization(false);
            ManagedLocomotionDiagnosticReset locomotionCommand = locomotion.PrepareDiagnosticReset();
            ReconstructionPolicyReset reconstructionCommand = reconstruction.PrepareReset();
            AttackLeasePreparedTransition ready = default, cleaned = default, reused = default;
            if (lease.Phase == AttackLeasePhase.Owned)
            {
                AttackLeasePreparedTransition clear = AttackLeaseMachine.PrepareOwnedClear(lease);
                AttackLeasePreparedTransition reuse = AttackLeaseMachine.PrepareObservedReuse(lease);
                cleaned = AttackLeaseMachine.PrepareDiagnosticResetAfter(lease, clear);
                reused = AttackLeaseMachine.PrepareDiagnosticResetAfter(lease, reuse);
            }
            else if (lease.Phase == AttackLeasePhase.CleanupPending)
            {
                AttackLeasePreparedTransition clear = AttackLeaseMachine.PrepareQuarantineClear(lease);
                AttackLeasePreparedTransition reuse = AttackLeaseMachine.PrepareObservedReuse(lease);
                cleaned = AttackLeaseMachine.PrepareDiagnosticResetAfter(lease, clear);
                reused = AttackLeaseMachine.PrepareDiagnosticResetAfter(lease, reuse);
            }
            else
            {
                ready = AttackLeaseMachine.PrepareDiagnosticReset(lease);
                reused = ready;
            }
            preparation = new DiagnosticResetPreparation(nextGeneration, sessionCommand,
                publicationCommand, ready, cleaned, reused, jumpCommand, stanceCommand,
                locomotionCommand, reconstructionCommand);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    public static bool CommitPreparedReducers(in DiagnosticResetPreparation preparation,
        AttackResetPreflightOutcome outcome, ref AttackLeaseState lease,
        ManagedMovementSessionReducer session, JumpForgivenessReducer jump,
        ManagedStanceReducer stance, ManagedLocomotionReducer locomotion,
        ReconstructionPolicyReducer reconstruction)
    {
        AttackLeasePreparedTransition leaseTransition = preparation.LeaseFor(outcome);
        if (!AttackLeaseMachine.CanCommitPrepared(lease, leaseTransition) ||
            !session.CanCommitDiagnosticReset(preparation.Session) ||
            !jump.CanCommitClear(preparation.Jump) ||
            !stance.CanCommitInitialization(preparation.Stance) ||
            !locomotion.CanCommitDiagnosticReset(preparation.Locomotion) ||
            !reconstruction.CanCommitReset(preparation.Reconstruction)) return false;
        _ = AttackLeaseMachine.CommitPrepared(ref lease, leaseTransition);
        _ = session.CommitDiagnosticReset(preparation.Session);
        _ = jump.CommitClear(preparation.Jump);
        _ = stance.CommitInitialization(preparation.Stance);
        _ = locomotion.CommitDiagnosticReset(preparation.Locomotion);
        _ = reconstruction.CommitReset(preparation.Reconstruction);
        return true;
    }
}
