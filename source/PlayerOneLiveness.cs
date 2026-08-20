namespace CoopFeasibilityMod;

public static class PlayerOneLiveness
{
    public const uint TransformStatusMask = 0x7;
    public const uint DeadStatusMask = 0x40000;
    public const ushort DeathStep = 0x10;

    public static bool IsAlive(int hp, uint status, ushort step) =>
        hp > 0 && (status & DeadStatusMask) == 0 && step != DeathStep;

    public static bool IsCompatible(int hp, uint status, ushort step, bool isAlucard, bool hasControl) =>
        IsAlive(hp, status, step) && isAlucard && hasControl &&
        (status & TransformStatusMask) == 0;
}
