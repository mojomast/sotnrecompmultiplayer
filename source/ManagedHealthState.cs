using System;

namespace CoopFeasibilityMod;

public struct ManagedHealthState
{
    public int Hp;
    public int Invulnerability;
    public int HurtLock;
    public bool Downed;
    public int DamageEvents;
    public int DamageConsumed;
    public int SuppressedInvulnerability;
    public int SuppressedHitInvulnerability;
    public bool HitInvulnerabilityActive;
    public int DownedCount;
    public int ReviveStarts;
    public int ReviveCancels;
    public int ReviveRecoveries;
    public int InvariantFailures;
    public bool CompactHurt;
    public int LastDamage;
    public int LastDamageSlot;
    public ushort LastDamageElement;
    public int ReviveProgress;
    public int Revives;
}

public readonly struct ManagedDamageTransition
{
    public readonly ManagedHealthState State;
    public readonly bool Applied;
    public readonly bool Lethal;

    public ManagedDamageTransition(ManagedHealthState state, bool applied, bool lethal)
    {
        State = state;
        Applied = applied;
        Lethal = lethal;
    }
}

public readonly struct ManagedReviveObservation
{
    public readonly bool CanControl;
    public readonly bool PlayerOneDown;
    public readonly bool PlayerTwoCircle;
    public readonly int DeltaX;
    public readonly int DeltaY;
    public readonly bool PlayerAlive;
    public readonly bool PlayerCompatible;
    public readonly bool RoomStable;

    public ManagedReviveObservation(bool canControl, bool playerOneDown, bool playerTwoCircle,
        int deltaX, int deltaY, bool playerAlive, bool playerCompatible, bool roomStable)
    {
        CanControl = canControl;
        PlayerOneDown = playerOneDown;
        PlayerTwoCircle = playerTwoCircle;
        DeltaX = deltaX;
        DeltaY = deltaY;
        PlayerAlive = playerAlive;
        PlayerCompatible = playerCompatible;
        RoomStable = roomStable;
    }

    public bool Eligible => CanControl && PlayerOneDown && PlayerTwoCircle &&
        Math.Abs((long)DeltaX) <= 24 && Math.Abs((long)DeltaY) <= 32 &&
        PlayerAlive && PlayerCompatible && RoomStable;
}

public static class ManagedHealthMachine
{
    public const int MaximumHp = 100;
    public const int ReviveHp = 50;
    public const int ReviveUpdates = 120;
    public const int ReviveProtectionUpdates = 120;
    public const int DamageInvulnerabilityUpdates = 60;
    public const int HurtLockUpdates = 18;
    public const int MaximumDamage = 40;

    public static ManagedHealthState Reset()
    {
        return new ManagedHealthState { Hp = MaximumHp, LastDamageSlot = -1 };
    }

    public static ManagedHealthState ConsumeOpportunity(ManagedHealthState state)
    {
        state.DamageConsumed = checked(state.DamageConsumed + 1);
        return state;
    }

    public static ManagedDamageTransition ApplyIncomingHit(ManagedHealthState state, int rawDamage,
        int slot, ushort element, bool crouched)
    {
        if (rawDamage <= 0) throw new ArgumentOutOfRangeException(nameof(rawDamage));
        if (state.Invulnerability > 0)
        {
            state.SuppressedInvulnerability = checked(state.SuppressedInvulnerability + 1);
            if (state.HitInvulnerabilityActive && !state.Downed)
                state.SuppressedHitInvulnerability = checked(state.SuppressedHitInvulnerability + 1);
            return new ManagedDamageTransition(state, false, false);
        }
        if (state.Downed) return new ManagedDamageTransition(state, false, false);

        int damage = Math.Min(rawDamage, MaximumDamage);
        state.Hp = Math.Max(0, state.Hp - damage);
        state.Invulnerability = DamageInvulnerabilityUpdates;
        state.HurtLock = HurtLockUpdates;
        state.DamageEvents = checked(state.DamageEvents + 1);
        state.LastDamage = damage;
        state.LastDamageSlot = slot;
        state.LastDamageElement = element;
        bool lethal = state.Hp == 0;
        if (lethal)
        {
            state.HitInvulnerabilityActive = false;
            state.Downed = true;
            state.DownedCount = checked(state.DownedCount + 1);
            state.HurtLock = 0;
            state.CompactHurt = false;
            state.ReviveProgress = 0;
        }
        else
        {
            state.HitInvulnerabilityActive = true;
            state.CompactHurt = crouched;
        }
        return new ManagedDamageTransition(state, true, lethal);
    }

    public static ManagedHealthState ApplyRevive(ManagedHealthState state, bool eligible)
    {
        if (!state.Downed || !eligible)
        {
            if (state.ReviveProgress > 0) state.ReviveCancels = checked(state.ReviveCancels + 1);
            state.ReviveProgress = 0;
            return state;
        }
        if (state.ReviveProgress == 0) state.ReviveStarts = checked(state.ReviveStarts + 1);
        state.ReviveProgress++;
        if (state.ReviveProgress < ReviveUpdates) return state;
        state.Hp = ReviveHp;
        state.Downed = false;
        state.Invulnerability = ReviveProtectionUpdates;
        state.HitInvulnerabilityActive = false;
        state.HurtLock = 0;
        state.ReviveProgress = 0;
        state.Revives = checked(state.Revives + 1);
        if (state.Hp == ReviveHp && state.Invulnerability == ReviveProtectionUpdates)
            state.ReviveRecoveries = checked(state.ReviveRecoveries + 1);
        else state.InvariantFailures = checked(state.InvariantFailures + 1);
        return state;
    }

    public static ManagedHealthState ApplyRevive(ManagedHealthState state, ManagedReviveObservation observation) =>
        ApplyRevive(state, observation.Eligible);

    public static ManagedHealthState AdvanceTimers(ManagedHealthState state, bool decrementInvulnerability,
        bool decrementHurtLock)
    {
        if (decrementInvulnerability && state.Invulnerability > 0)
        {
            state.Invulnerability--;
            if (state.Invulnerability == 0) state.HitInvulnerabilityActive = false;
        }
        if (decrementHurtLock && state.HurtLock > 0) state.HurtLock--;
        return state;
    }

    public static ManagedHealthState Reconstructed(ManagedHealthState state)
    {
        if (!state.Downed) state.Invulnerability = Math.Max(state.Invulnerability, DamageInvulnerabilityUpdates);
        return state;
    }

    public static ManagedHealthState Validate(ManagedHealthState state)
    {
        if (state.Hp < 0 || state.Hp > MaximumHp || state.Downed != (state.Hp == 0) ||
            state.Invulnerability < 0 || state.HurtLock < 0 || state.ReviveProgress < 0)
            state.InvariantFailures = checked(state.InvariantFailures + 1);
        return state;
    }
}
