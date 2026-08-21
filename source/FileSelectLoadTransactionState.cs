using System;

namespace CoopFeasibilityMod;

[Flags]
public enum FileSelectLoadTransactionAction : byte
{
    None = 0,
    FileSelectObserved = 1,
    LoadingObserved = 2,
    ArmBootstrap = 4,
    Cancel = 8,
    CancelBootstrap = 16,
}

public enum FileSelectLoadTransactionPhase : byte
{
    Idle,
    FileSelectLatched,
    ArmedAwaitingPlay,
    PlayObserved,
}

public enum FileSelectLoadTransactionReason : byte
{
    None,
    NativeFileSelect,
    NowLoading,
    SelectedSaveProgression,
    ReturnedToTitle,
    ReturnedToMainMenuIdle,
    PlayBeforeLoading,
    UnsupportedFileSelectPath,
    IncompatibleState,
    Timeout,
    Fatal,
    Unload,
    DiagnosticReset,
    DirectPlayInitialization,
}

public readonly record struct FileSelectLoadObservation(
    int GameStateRaw,
    uint GameStepRaw,
    uint EngineStepRaw,
    bool Loading);

public readonly record struct FileSelectLoadTransactionTransition(
    FileSelectLoadTransactionAction Action,
    FileSelectLoadTransactionReason Reason)
{
    public bool Has(FileSelectLoadTransactionAction action) => (Action & action) != 0;
}

// Adapter-local authorization for native file-select loads. Inputs are existing public game
// state and full-width step observations; no selector or save-data memory enters this machine.
public sealed class FileSelectLoadTransactionState
{
    public const int DefaultTimeoutObservations = 1800;
    public const int MainMenuState = 8;
    public const int NowLoadingState = 4;
    public const int PlayState = 2;
    public const int TitleState = 1;
    public const uint FileSelectGameStep = 6;
    public const uint FileSelectEngineStep = 0x33;

    private readonly int _timeoutObservations;

    public FileSelectLoadTransactionPhase Phase { get; private set; }
    public int ElapsedObservations { get; private set; }
    public bool BootstrapArmed => Phase is FileSelectLoadTransactionPhase.ArmedAwaitingPlay or
        FileSelectLoadTransactionPhase.PlayObserved;

    public FileSelectLoadTransactionState(int timeoutObservations = DefaultTimeoutObservations)
    {
        if (timeoutObservations <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutObservations));
        _timeoutObservations = timeoutObservations;
    }

    public FileSelectLoadTransactionTransition Observe(in FileSelectLoadObservation observation)
    {
        if (Phase == FileSelectLoadTransactionPhase.Idle)
        {
            if (!IsConfirmedFileSelect(observation)) return default;
            Phase = FileSelectLoadTransactionPhase.FileSelectLatched;
            ElapsedObservations = 0;
            return new(FileSelectLoadTransactionAction.FileSelectObserved,
                FileSelectLoadTransactionReason.NativeFileSelect);
        }

        if (++ElapsedObservations >= _timeoutObservations)
            return Cancel(FileSelectLoadTransactionReason.Timeout);

        if (Phase == FileSelectLoadTransactionPhase.FileSelectLatched)
        {
            if (IsConfirmedFileSelect(observation)) return default;
            if (observation.GameStateRaw == NowLoadingState)
                return Arm(FileSelectLoadTransactionReason.NowLoading);
            if (IsSelectedSaveProgression(observation))
                return Arm(FileSelectLoadTransactionReason.SelectedSaveProgression);
            if (observation.GameStateRaw == TitleState)
                return Cancel(FileSelectLoadTransactionReason.ReturnedToTitle);
            if (IsMainMenuIdle(observation))
                return Cancel(FileSelectLoadTransactionReason.ReturnedToMainMenuIdle);
            if (observation.GameStateRaw == MainMenuState)
                return Cancel(FileSelectLoadTransactionReason.UnsupportedFileSelectPath);
            if (observation.GameStateRaw == PlayState)
            {
                if (IsFullyNormalPlay(observation))
                    return ArmDirectPlay();
                return Cancel(FileSelectLoadTransactionReason.PlayBeforeLoading);
            }
            if (observation.Loading)
                return Arm(FileSelectLoadTransactionReason.NowLoading);
            return Cancel(FileSelectLoadTransactionReason.IncompatibleState);
        }

        if (observation.GameStateRaw == PlayState)
        {
            Phase = FileSelectLoadTransactionPhase.PlayObserved;
            return default;
        }
        if (observation.GameStateRaw == NowLoadingState) return default;
        if (IsSelectedSaveProgression(observation)) return default;
        if (observation.GameStateRaw == TitleState)
            return Cancel(FileSelectLoadTransactionReason.ReturnedToTitle);
        if (IsMainMenuIdle(observation))
            return Cancel(FileSelectLoadTransactionReason.ReturnedToMainMenuIdle);
        if (observation.GameStateRaw == MainMenuState)
            return Cancel(FileSelectLoadTransactionReason.IncompatibleState);
        if (observation.Loading) return default;
        return Cancel(FileSelectLoadTransactionReason.IncompatibleState);
    }

    public FileSelectLoadTransactionTransition Cancel(FileSelectLoadTransactionReason reason)
    {
        if (Phase == FileSelectLoadTransactionPhase.Idle) return default;
        FileSelectLoadTransactionAction action = FileSelectLoadTransactionAction.Cancel;
        if (BootstrapArmed) action |= FileSelectLoadTransactionAction.CancelBootstrap;
        Reset();
        return new(action, reason);
    }

    public void Complete() => Reset();

    private FileSelectLoadTransactionTransition Arm(FileSelectLoadTransactionReason reason)
    {
        Phase = FileSelectLoadTransactionPhase.ArmedAwaitingPlay;
        return new(FileSelectLoadTransactionAction.LoadingObserved |
            FileSelectLoadTransactionAction.ArmBootstrap, reason);
    }

    private FileSelectLoadTransactionTransition ArmDirectPlay()
    {
        Phase = FileSelectLoadTransactionPhase.PlayObserved;
        return new(FileSelectLoadTransactionAction.ArmBootstrap,
            FileSelectLoadTransactionReason.DirectPlayInitialization);
    }

    private void Reset()
    {
        Phase = FileSelectLoadTransactionPhase.Idle;
        ElapsedObservations = 0;
    }

    private static bool IsConfirmedFileSelect(in FileSelectLoadObservation observation) =>
        observation.GameStateRaw == MainMenuState &&
        observation.GameStepRaw == FileSelectGameStep &&
        observation.EngineStepRaw == FileSelectEngineStep;

    private static bool IsSelectedSaveProgression(in FileSelectLoadObservation observation) =>
        observation.GameStateRaw == MainMenuState &&
        observation.GameStepRaw == FileSelectGameStep &&
        observation.EngineStepRaw is 0x100 or 0x101 or 0x104;

    private static bool IsFullyNormalPlay(in FileSelectLoadObservation observation) =>
        observation.GameStateRaw == PlayState && observation.GameStepRaw == 3 &&
        observation.EngineStepRaw == 1 && !observation.Loading;

    private static bool IsMainMenuIdle(in FileSelectLoadObservation observation) =>
        observation.GameStateRaw == MainMenuState &&
        observation.GameStepRaw == FileSelectGameStep && observation.EngineStepRaw == 2;
}
