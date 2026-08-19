using System;
using RecompOne.Runtime.Context;

namespace CoopFeasibilityMod;

/// <summary>
/// Allocation-free save/restore guard for every state word exposed by CpuContext.
/// The caller owns the storage so hot guest-call adapters can use stack memory.
/// </summary>
internal ref struct CpuContextRegisterGuard
{
    internal const int GeneralRegisterCount = 32;
    internal const int StateWordCount = GeneralRegisterCount + 7;

    private readonly CpuContext _context;
    private readonly Span<uint> _saved;

    internal CpuContextRegisterGuard(CpuContext context, Span<uint> storage)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (storage.Length < StateWordCount)
            throw new ArgumentException($"CpuContext guard requires {StateWordCount} words.", nameof(storage));

        _context = context;
        _saved = storage[..StateWordCount];
        for (int i = 0; i < GeneralRegisterCount; i++) _saved[i] = context[i];
        _saved[32] = context.HI;
        _saved[33] = context.LO;
        _saved[34] = context.SR;
        _saved[35] = context.Cause;
        _saved[36] = context.EPC;
        _saved[37] = context.BadVAddr;
        _saved[38] = context.PRId;
    }

    internal void Restore()
    {
        for (int i = 0; i < GeneralRegisterCount; i++) _context[i] = _saved[i];
        _context.HI = _saved[32];
        _context.LO = _saved[33];
        _context.SR = _saved[34];
        _context.Cause = _saved[35];
        _context.EPC = _saved[36];
        _context.BadVAddr = _saved[37];
        _context.PRId = _saved[38];

        // A failed exact restore is native-state corruption, not a recoverable collision miss.
        for (int i = 0; i < GeneralRegisterCount; i++)
            if (_context[i] != _saved[i])
                throw new InvalidOperationException($"CpuContext GPR {i} restore failed.");
        if (_context.HI != _saved[32] || _context.LO != _saved[33] || _context.SR != _saved[34] ||
            _context.Cause != _saved[35] || _context.EPC != _saved[36] ||
            _context.BadVAddr != _saved[37] || _context.PRId != _saved[38])
            throw new InvalidOperationException("CpuContext special-register restore failed.");
    }
}
