namespace CoopFeasibilityMod;

public enum AttackResetPreflightOutcome
{
    Ready,
    Cleaned,
    CarryMutationStopped,
    RefusedMemoryUnavailable,
    RefusedCleanupFault,
    RefusedResidualOwnership
}

public readonly struct AttackPublicationResetCommand
{
    internal readonly ulong MachineOwner;
    internal readonly ulong Revision;
    internal readonly AttackPublicationPhase Phase;

    internal AttackPublicationResetCommand(AttackPublicationState state)
    {
        MachineOwner = state.MachineOwner;
        Revision = state.Revision;
        Phase = state.Phase;
    }
}

/// <summary>Safety gate run before any diagnostic/session reset projection is mutated.</summary>
public static class AttackResetPreflight
{
    public static AttackPublicationResetCommand Prepare(AttackPublicationState state)
    {
        AttackPublicationPolicy.ValidateResetPreparation(state);
        return new AttackPublicationResetCommand(state);
    }

    public static AttackResetPreflightOutcome Run(ref AttackPublicationState state,
        IAttackPublicationAdapter? adapter, bool memoryAvailable)
    {
        AttackPublicationResetCommand command = Prepare(state);
        return RunPrepared(ref state, adapter, memoryAvailable, command);
    }

    public static AttackResetPreflightOutcome RunPrepared(ref AttackPublicationState state,
        IAttackPublicationAdapter? adapter, bool memoryAvailable,
        in AttackPublicationResetCommand command)
    {
        if (!AttackPublicationPolicy.MatchesResetPreparation(state, command.MachineOwner, command.Revision) ||
            state.Phase != command.Phase)
            return AttackResetPreflightOutcome.RefusedResidualOwnership;
        if (command.Phase is AttackPublicationPhase.Empty or AttackPublicationPhase.RolledBack)
        {
            state = AttackPublicationPolicy.CommitResetTransition(state, command.MachineOwner,
                command.Revision, AttackPublicationPhase.Empty);
            return AttackResetPreflightOutcome.Ready;
        }
        if (command.Phase == AttackPublicationPhase.MutationStopped)
        {
            state = AttackPublicationPolicy.CommitResetTransition(state, command.MachineOwner,
                command.Revision, AttackPublicationPhase.MutationStopped);
            return AttackResetPreflightOutcome.CarryMutationStopped;
        }
        if (command.Phase == AttackPublicationPhase.ResidualStopped)
            return AttackResetPreflightOutcome.RefusedResidualOwnership;
        if (!memoryAvailable || adapter == null)
            return AttackResetPreflightOutcome.RefusedMemoryUnavailable;

        AttackPublicationState working = state;
        bool cleaned = command.Phase == AttackPublicationPhase.RetryableQuarantine
            ? AttackPublicationPolicy.RetryQuarantine(ref working, adapter)
            : AttackPublicationPolicy.Cleanup(ref working, adapter);
        AttackPublicationPhase resultPhase = working.Phase;
        if (cleaned || resultPhase is AttackPublicationPhase.Empty or AttackPublicationPhase.RolledBack)
        {
            state = AttackPublicationPolicy.CommitResetTransition(state, command.MachineOwner,
                command.Revision, AttackPublicationPhase.Empty);
            return AttackResetPreflightOutcome.Cleaned;
        }
        working.MachineOwner = command.MachineOwner;
        working.Revision = command.Revision + 1;
        state = working;
        if (resultPhase == AttackPublicationPhase.MutationStopped)
            return AttackResetPreflightOutcome.CarryMutationStopped;
        return resultPhase == AttackPublicationPhase.ResidualStopped
            ? AttackResetPreflightOutcome.RefusedResidualOwnership
            : AttackResetPreflightOutcome.RefusedCleanupFault;
    }

    public static bool AllowsReset(AttackResetPreflightOutcome outcome) => outcome is
        AttackResetPreflightOutcome.Ready or AttackResetPreflightOutcome.Cleaned or
        AttackResetPreflightOutcome.CarryMutationStopped;
}
