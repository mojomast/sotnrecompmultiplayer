using System;

namespace CoopFeasibilityMod;

public interface IAttackTargetReadAdapter
{
    int ReadScrollX();
    int ReadScrollY();
    uint TargetAddress(int slot);
    byte ReadAttackU8(uint offset);
    byte ReadTargetU8(uint address, uint offset);
    ushort ReadTargetU16(uint address, uint offset);
    uint ReadTargetU32(uint address, uint offset);
}

public readonly struct AttackTargetCaptureInput
{
    public readonly int WorldX;
    public readonly int WorldY;
    public readonly bool Projectile;
    public readonly bool FacingLeft;
    public readonly byte HalfWidth;
    public readonly byte HalfHeight;
    public readonly ushort HitState;
    public readonly int AttackerId;

    public AttackTargetCaptureInput(int worldX, int worldY, bool projectile, bool facingLeft,
        byte halfWidth, byte halfHeight, ushort hitState, int attackerId)
    {
        WorldX = worldX; WorldY = worldY; Projectile = projectile; FacingLeft = facingLeft;
        HalfWidth = halfWidth; HalfHeight = halfHeight; HitState = hitState; AttackerId = attackerId;
    }
}

public sealed class AttackTargetCaptureState
{
    internal readonly uint[] Addresses = new uint[16];
    internal readonly ulong[] Identities = new ulong[16];
    internal readonly short[] HpBefore = new short[16];
    internal readonly byte[] CooldownBefore = new byte[16];
    internal readonly int[] WorldX = new int[16];
    internal readonly int[] WorldY = new int[16];
    public int Count { get; internal set; }
    public bool Overflowed { get; internal set; }

    public void Clear()
    {
        Count = 0;
        Overflowed = false;
        Array.Clear(Addresses); Array.Clear(Identities); Array.Clear(HpBefore); Array.Clear(CooldownBefore);
        Array.Clear(WorldX); Array.Clear(WorldY);
    }
}

public readonly struct AttackTargetObservation
{
    public readonly bool HitFlag;
    public readonly int HpChanges;
    public readonly int CooldownChanges;
    public readonly int NativeHits;
    public readonly int Defeated;
    public readonly int CompatibleZeroHpHits;
    public readonly int CausalResults;
    public readonly bool UniqueDefeat;
    public readonly bool CaptureOverflowed;
    public readonly int DefeatWorldX;
    public readonly int DefeatWorldY;

    public AttackTargetObservation(bool hitFlag, int hpChanges, int cooldownChanges,
        int nativeHits, int defeated, int compatibleZeroHpHits, int causalResults,
        bool uniqueDefeat, int defeatWorldX, int defeatWorldY, bool captureOverflowed)
    {
        HitFlag = hitFlag; HpChanges = hpChanges; CooldownChanges = cooldownChanges;
        NativeHits = nativeHits; Defeated = defeated; CompatibleZeroHpHits = compatibleZeroHpHits;
        CausalResults = causalResults;
        UniqueDefeat = uniqueDefeat; DefeatWorldX = defeatWorldX; DefeatWorldY = defeatWorldY;
        CaptureOverflowed = captureOverflowed;
    }
}

public static class AttackTargetObservationPolicy
{
    private const uint DeadFlag = 0x100;

    public static void Capture(AttackTargetCaptureState state, IAttackTargetReadAdapter adapter,
        in AttackTargetCaptureInput input)
    {
        state.Count = 0;
        state.Overflowed = false;
        int scrollX = adapter.ReadScrollX();
        int scrollY = adapter.ReadScrollY();
        int attackX = input.Projectile
            ? (input.WorldX >> 16) - scrollX
            : (input.WorldX >> 16) - scrollX + (input.FacingLeft ? -14 : 14);
        int attackY = input.Projectile
            ? (input.WorldY >> 16) - scrollY
            : (input.WorldY >> 16) - scrollY - 8;

        for (int slot = 64; slot < 192; slot++)
        {
            uint address = adapter.TargetAddress(slot);
            uint update = adapter.ReadTargetU32(address, 0x28);
            ushort id = adapter.ReadTargetU16(address, 0x26);
            ushort targetState = adapter.ReadTargetU16(address, 0x3C);
            if ((targetState & 0x3E) == 0 || (targetState & input.HitState) == 0) continue;
            if ((adapter.ReadTargetU32(address, 0x34) & DeadFlag) != 0) continue;
            int width = adapter.ReadTargetU8(address, 0x46);
            int height = adapter.ReadTargetU8(address, 0x47);
            if (width == 0 || height == 0) continue;
            int offsetX = unchecked((short)adapter.ReadTargetU16(address, 0x10));
            int positionX = unchecked((short)adapter.ReadTargetU16(address, 0x02));
            ushort facing = adapter.ReadTargetU16(address, 0x14);
            int centerX = positionX + (facing != 0 ? -offsetX : offsetX);
            int positionY = unchecked((short)adapter.ReadTargetU16(address, 0x06));
            int offsetY = unchecked((short)adapter.ReadTargetU16(address, 0x12));
            int centerY = positionY + offsetY;
            if (centerX is <= -32 or >= 288 || centerY is <= -32 or >= 256) continue;
            if (Math.Abs(centerX - attackX) >= width + input.HalfWidth ||
                Math.Abs(centerY - attackY) >= height + input.HalfHeight) continue;
            ushort enemyId = adapter.ReadTargetU16(address, 0x3A);
            short hp = unchecked((short)adapter.ReadTargetU16(address, 0x3E));
            byte cooldown = adapter.ReadTargetU8(address, 0x6D + (uint)input.AttackerId);
            if (state.Count == state.Addresses.Length)
            {
                state.Overflowed = true;
                continue;
            }
            int index = state.Count++;
            state.Addresses[index] = address;
            state.Identities[index] = ((ulong)update << 32) | ((ulong)enemyId << 16) | id;
            state.HpBefore[index] = hp;
            state.CooldownBefore[index] = cooldown;
            state.WorldX[index] = centerX + scrollX;
            state.WorldY[index] = centerY + scrollY;
        }
    }

    public static AttackTargetObservation Observe(AttackTargetCaptureState state,
        IAttackTargetReadAdapter adapter, int attackerId)
    {
        bool hitFlag = adapter.ReadAttackU8(0x48) != 0;
        bool anyCooldown = false;
        int hpChanges = 0, cooldownChanges = 0, nativeHits = 0, defeated = 0, zeroHp = 0;
        int defeatX = 0, defeatY = 0;
        for (int index = 0; index < state.Count; index++)
        {
            uint address = state.Addresses[index];
            uint update = adapter.ReadTargetU32(address, 0x28);
            ushort id = adapter.ReadTargetU16(address, 0x26);
            ushort enemyId = adapter.ReadTargetU16(address, 0x3A);
            ulong identity = ((ulong)update << 32) | ((ulong)enemyId << 16) | id;
            if (identity != state.Identities[index]) continue;
            short hpAfter = unchecked((short)adapter.ReadTargetU16(address, 0x3E));
            if (hpAfter < state.HpBefore[index]) hpChanges++;
            byte cooldown = adapter.ReadTargetU8(address, 0x6D + (uint)attackerId);
            if (cooldown <= state.CooldownBefore[index]) continue;
            cooldownChanges++; anyCooldown = true;
            if (!hitFlag) continue;
            nativeHits++;
            if (state.HpBefore[index] > 0)
            {
                if (hpAfter <= 0 || (adapter.ReadTargetU32(address, 0x34) & DeadFlag) != 0)
                {
                    defeated++; defeatX = state.WorldX[index]; defeatY = state.WorldY[index];
                }
            }
            if (state.HpBefore[index] <= 0 || hpAfter <= 0) zeroHp++;
        }
        bool uniqueDefeat = !state.Overflowed && hitFlag && anyCooldown && defeated == 1 && nativeHits == 1;
        return new AttackTargetObservation(hitFlag, hpChanges, cooldownChanges, nativeHits,
            defeated, zeroHp, hitFlag && anyCooldown ? 1 : 0, uniqueDefeat, defeatX, defeatY,
            state.Overflowed);
    }
}
