using System;

namespace CoopFeasibilityMod;

// Production and focused tests share this exact ordering. Every method before CommitPrepared is
// fallible and must be projection-only. CommitPrepared is entered only after CanCommit proves that
// every owner/revision-bound reducer token is still current.
public interface IManagedReconstructionCommitAdapter
{
    void PrepareScalars();
    void PrepareStance();
    void PrepareJump();
    void PrepareLocomotion();
    void ValidatePoseProjection();
    void PrepareHealthProjection();
    void PrepareSessionCompletion();
    void PrepareDiagnostics();
    bool CanCommit();
    bool CommitPrepared();
}

public static class ManagedReconstructionCommitOrchestration
{
    public static ReconstructionRunResult Run<TAdapter>(ref TAdapter adapter)
        where TAdapter : IManagedReconstructionCommitAdapter
    {
        try
        {
            adapter.PrepareScalars();
            adapter.PrepareStance();
            adapter.PrepareJump();
            adapter.PrepareLocomotion();
            adapter.ValidatePoseProjection();
            adapter.PrepareHealthProjection();
            adapter.PrepareSessionCompletion();
            adapter.PrepareDiagnostics();
            if (!adapter.CanCommit()) return ReconstructionRunResult.AdapterFault;
            return adapter.CommitPrepared()
                ? ReconstructionRunResult.Selected
                : ReconstructionRunResult.AdapterFault;
        }
        catch
        {
            return ReconstructionRunResult.AdapterFault;
        }
    }
}
