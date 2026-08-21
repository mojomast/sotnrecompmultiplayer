using System;

namespace CoopFeasibilityMod;

internal interface INativeExpObservationSource
{
    bool MemoryAvailable { get; }
    bool InGame { get; }
    bool IsLoading { get; }
    bool IsAlucard { get; }
    bool IsMarbleGallery { get; }
    uint ReadUnsignedExp();
}

internal static class NativeExpObservation
{
    public static long? TryRead(INativeExpObservationSource source)
    {
        try
        {
            if (!source.MemoryAvailable || !source.InGame || source.IsLoading ||
                !source.IsAlucard || !source.IsMarbleGallery)
                return null;
            return (long)source.ReadUnsignedExp();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
