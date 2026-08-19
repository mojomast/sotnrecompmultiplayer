using System;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;

namespace CoopFeasibilityMod;

internal static class CpuContextDirectCall
{
    internal static uint Invoke(CpuContext context, IMemory memory, uint function,
        uint a0, uint a1, uint a2, uint a3)
    {
        context.A0 = a0;
        context.A1 = a1;
        context.A2 = a2;
        context.A3 = a3;
        Dispatcher.Call(context, memory, function);
        return context.V0;
    }
}

internal static class CpuContextGuardedDirectCall
{
    internal static uint Invoke(CpuContext context, IMemory memory, uint function,
        uint a0, uint a1, uint a2, uint a3)
    {
        Span<uint> savedContext = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
        var contextGuard = new CpuContextRegisterGuard(context, savedContext);
        try
        {
            return CpuContextDirectCall.Invoke(context, memory, function, a0, a1, a2, a3);
        }
        finally
        {
            contextGuard.Restore();
        }
    }
}

internal static class CpuContextScratchDirectCall
{
    internal static bool TryInvoke(CpuContext context, IMemory memory, uint function,
        uint a0, uint a1, uint temporarySp, uint scratchStart, Span<byte> saved,
        out uint result, out int savedCount, out Exception? restoreFailure)
    {
        result = 0;
        savedCount = 0;
        restoreFailure = null;
        bool callSucceeded = true;
        Span<uint> savedContext = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
        var contextGuard = new CpuContextRegisterGuard(context, savedContext);
        try
        {
            try
            {
                for (int i = 0; i < saved.Length; i++)
                {
                    saved[i] = memory.ReadU8(scratchStart + (uint)i);
                    savedCount++;
                    memory.WriteU8(scratchStart + (uint)i, 0);
                }
                context.SP = temporarySp;
                result = CpuContextDirectCall.Invoke(context, memory, function, a0, a1, 0, 0);
            }
            catch
            {
                callSucceeded = false;
            }
        }
        finally
        {
            try
            {
                GuestScratchRestore.RestoreAll(memory, scratchStart, saved[..savedCount]);
            }
            catch (Exception ex)
            {
                restoreFailure = ex;
            }
            finally
            {
                contextGuard.Restore();
            }
        }
        return callSucceeded;
    }
}

internal static class GuestScratchRestore
{
    /// <summary>
    /// Restores and verifies every saved byte. Faults are delayed until all recoverable
    /// writes and reads have been attempted so one bad address cannot strand later bytes.
    /// </summary>
    internal static void RestoreAll(IMemory memory, uint start, ReadOnlySpan<byte> saved)
    {
        Exception? firstFailure = null;
        for (int i = 0; i < saved.Length; i++)
        {
            try
            {
                memory.WriteU8(start + (uint)i, saved[i]);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        for (int i = 0; i < saved.Length; i++)
        {
            try
            {
                if (memory.ReadU8(start + (uint)i) != saved[i] && firstFailure is null)
                    firstFailure = new InvalidOperationException($"Guest scratch restore failed at byte {i}.");
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        if (firstFailure is not null) throw firstFailure;
    }
}
