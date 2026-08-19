using System;

namespace CoopFeasibilityMod;

public readonly struct ContactObservation
{
    public readonly bool Eligible;
    public readonly bool Overlapping;
    public readonly ushort EntityId;
    public readonly ushort EnemyId;
    public readonly uint Update;
    public readonly ushort HitboxState;
    public readonly short Attack;
    public readonly ushort Element;
    public readonly short CenterX;
    public readonly short CenterY;

    public ContactObservation(bool eligible, bool overlapping, ushort entityId, ushort enemyId,
        uint update, ushort hitboxState, short attack, ushort element, short centerX, short centerY)
    {
        if (overlapping && !eligible)
            throw new ArgumentException("An overlapping contact observation must be eligible.", nameof(overlapping));
        if (overlapping && (entityId == 0 || update == 0))
            throw new ArgumentException("An overlapping contact observation requires native identity.", nameof(entityId));
        Eligible = eligible;
        Overlapping = overlapping;
        EntityId = entityId;
        EnemyId = enemyId;
        Update = update;
        HitboxState = hitboxState;
        Attack = attack;
        Element = element;
        CenterX = centerX;
        CenterY = centerY;
    }
}

public readonly struct ContactDamageOpportunity
{
    public readonly int Index;
    public readonly int Damage;
    public readonly ushort Element;
    public readonly short CenterX;
    public readonly short CenterY;

    internal ContactDamageOpportunity(int index, int damage, ushort element, short centerX, short centerY)
    {
        Index = index;
        Damage = damage;
        Element = element;
        CenterX = centerX;
        CenterY = centerY;
    }
}

// This value snapshot contains no reducer-owned buffers, so retaining a result cannot observe later mutations.
public readonly struct ContactOpportunityState
{
    public readonly bool BaselinePending;
    public readonly bool Suspended;
    public readonly bool ResumeGracePending;
    public readonly int ResumeGraceBudget;
    public readonly int ResumeGraceScans;
    public readonly int ContinuousSafeScans;
    public readonly long ScanFrames;
    public readonly long SlotsScanned;
    public readonly long EligibleSamples;
    public readonly long OverlapSamples;
    public readonly long DamagingSamples;
    public readonly long StaySamples;
    public readonly int Current;
    public readonly int Peak;
    public readonly int Entries;
    public readonly int Exits;
    public readonly int Resets;

    internal ContactOpportunityState(ContactOpportunityMachine machine)
    {
        BaselinePending = machine.BaselinePending;
        Suspended = machine.Suspended;
        ResumeGracePending = machine.ResumeGracePending;
        ResumeGraceBudget = machine.ResumeGraceBudget;
        ResumeGraceScans = machine.ResumeGraceScans;
        ContinuousSafeScans = machine.ContinuousSafeScans;
        ScanFrames = machine.ScanFrames;
        SlotsScanned = machine.SlotsScanned;
        EligibleSamples = machine.EligibleSamples;
        OverlapSamples = machine.OverlapSamples;
        DamagingSamples = machine.DamagingSamples;
        StaySamples = machine.StaySamples;
        Current = machine.Current;
        Peak = machine.Peak;
        Entries = machine.Entries;
        Exits = machine.Exits;
        Resets = machine.Resets;
    }
}

public readonly struct ContactOpportunityTransition
{
    public readonly ContactOpportunityState State;
    public readonly int OpportunityCount;
    public readonly bool HasWinner;
    public readonly ContactDamageOpportunity Winner;

    internal ContactOpportunityTransition(ContactOpportunityState state, int opportunityCount,
        bool hasWinner, ContactDamageOpportunity winner)
    {
        State = state;
        OpportunityCount = opportunityCount;
        HasWinner = hasWinner;
        Winner = winner;
    }
}

// The machine owns fixed buffers allocated once. Advance and Suspend mutate only that private state and
// return immutable value results, keeping the hot path allocation-free without exposing writable storage.
public sealed class ContactOpportunityMachine
{
    public const int SlotCount = 128;
    public const int RepeatScans = 60;
    public const int MaximumDamage = 40;
    private const ulong GenerationMix = 0x9E3779B97F4A7C15UL;

    private readonly ulong[] _identities = new ulong[SlotCount];
    private readonly short[] _attacks = new short[SlotCount];
    private readonly ulong[] _phaseKeys = new ulong[SlotCount];
    private readonly uint[] _generations = new uint[SlotCount];
    private readonly bool[] _wasEligible = new bool[SlotCount];
    private readonly int[] _repeatTicks = new int[SlotCount];

    public bool BaselinePending { get; private set; }
    public bool Suspended { get; private set; }
    public bool ResumeGracePending { get; private set; }
    public int ResumeGraceBudget { get; private set; }
    public int ResumeGraceScans { get; private set; }
    public int ContinuousSafeScans { get; private set; }
    public long ScanFrames { get; private set; }
    public long SlotsScanned { get; private set; }
    public long EligibleSamples { get; private set; }
    public long OverlapSamples { get; private set; }
    public long DamagingSamples { get; private set; }
    public long StaySamples { get; private set; }
    public int Current { get; private set; }
    public int Peak { get; private set; }
    public int Entries { get; private set; }
    public int Exits { get; private set; }
    public int Resets { get; private set; }
    public ContactOpportunityState State => new(this);

    public ContactOpportunityMachine() => Reset();

    public void Reset()
    {
        Array.Clear(_identities);
        Array.Clear(_attacks);
        Array.Clear(_phaseKeys);
        Array.Clear(_generations);
        Array.Clear(_wasEligible);
        Array.Clear(_repeatTicks);
        BaselinePending = true;
        Suspended = false;
        ResumeGracePending = false;
        ResumeGraceBudget = 1;
        ResumeGraceScans = 0;
        ContinuousSafeScans = 0;
        ScanFrames = 0;
        SlotsScanned = 0;
        EligibleSamples = 0;
        OverlapSamples = 0;
        DamagingSamples = 0;
        StaySamples = 0;
        Current = 0;
        Peak = 0;
        Entries = 0;
        Exits = 0;
        Resets = 0;
    }

    public ContactOpportunityTransition Advance(ReadOnlySpan<ContactObservation> observations)
    {
        if (observations.Length != SlotCount)
            throw new ArgumentException($"A contact scan must contain exactly {SlotCount} slots.", nameof(observations));

        bool baseline = BaselinePending;
        bool resumeGrace = Suspended && ResumeGracePending;
        Suspended = false;
        ResumeGracePending = false;
        int eligible = 0;
        int overlaps = 0;
        int damaging = 0;
        int currentCount = 0;
        int opportunityCount = 0;
        int winnerDamage = 0;
        bool hasWinner = false;
        ContactDamageOpportunity winner = default;

        // Slot order is policy: every opportunity is counted, and a strict-greater winner comparison
        // makes the first (lowest) slot win equal clamped-damage ties.
        for (int index = 0; index < SlotCount; index++)
        {
            ContactObservation observation = observations[index];
            if (_wasEligible[index] && !observation.Eligible)
                _generations[index] = unchecked(_generations[index] + 1);
            _wasEligible[index] = observation.Eligible;
            if (observation.Eligible) eligible++;

            ulong currentIdentity = 0;
            short currentAttack = 0;
            ulong currentPhase = 0;
            if (observation.Overlapping)
            {
                ulong identity = PackIdentity(observation.Update, observation.EnemyId, observation.EntityId);
                currentIdentity = unchecked(identity ^ ((ulong)_generations[index] * GenerationMix));
                currentAttack = observation.Attack;
                currentPhase = PackPhase(observation.Attack, observation.Element, observation.HitboxState);
                overlaps++;
                currentCount++;
                if (observation.Attack > 0) damaging++;
            }

            if (baseline)
            {
                _repeatTicks[index] = 0;
            }
            else if (resumeGrace)
            {
                _repeatTicks[index] = currentIdentity != 0 && currentAttack > 0 ? RepeatScans - 1 : 0;
            }
            else
            {
                ulong previous = _identities[index];
                short previousAttack = _attacks[index];
                ulong previousPhase = _phaseKeys[index];
                bool opportunity = false;
                if (previous == currentIdentity)
                {
                    if (currentIdentity != 0)
                    {
                        StaySamples = unchecked(StaySamples + 1);
                        if (currentAttack > 0 && (previousAttack <= 0 || currentPhase != previousPhase))
                        {
                            opportunity = true;
                            _repeatTicks[index] = 0;
                        }
                        else if (currentAttack > 0 && previousAttack > 0 &&
                            ++_repeatTicks[index] >= RepeatScans)
                        {
                            opportunity = true;
                            _repeatTicks[index] = 0;
                        }
                        else if (currentAttack <= 0) _repeatTicks[index] = 0;
                    }
                    else _repeatTicks[index] = 0;
                }
                else
                {
                    if (previous != 0) Exits = unchecked(Exits + 1);
                    if (currentIdentity != 0)
                    {
                        Entries = unchecked(Entries + 1);
                        opportunity = currentAttack > 0;
                    }
                    _repeatTicks[index] = 0;
                }

                if (opportunity && currentAttack > 0)
                {
                    opportunityCount++;
                    int damage = Math.Clamp((int)currentAttack, 1, MaximumDamage);
                    if (damage > winnerDamage)
                    {
                        winnerDamage = damage;
                        winner = new ContactDamageOpportunity(index, damage, observation.Element,
                            observation.CenterX, observation.CenterY);
                        hasWinner = true;
                    }
                }
            }

            _identities[index] = currentIdentity;
            _attacks[index] = currentAttack;
            _phaseKeys[index] = currentPhase;
        }

        ScanFrames = unchecked(ScanFrames + 1);
        SlotsScanned = unchecked(SlotsScanned + SlotCount);
        EligibleSamples = unchecked(EligibleSamples + eligible);
        OverlapSamples = unchecked(OverlapSamples + overlaps);
        DamagingSamples = unchecked(DamagingSamples + damaging);
        if (baseline) BaselinePending = false;
        if (resumeGrace)
        {
            ResumeGraceBudget = 0;
            ResumeGraceScans = unchecked(ResumeGraceScans + 1);
        }
        Current = currentCount;
        Peak = Math.Max(Peak, currentCount);
        ContinuousSafeScans = unchecked(ContinuousSafeScans + 1);
        if (ContinuousSafeScans >= RepeatScans) ResumeGraceBudget = 1;
        return new ContactOpportunityTransition(State, opportunityCount, hasWinner, winner);
    }

    public ContactOpportunityState Suspend()
    {
        bool reset = !BaselinePending || Current != 0;
        bool newSuspension = !Suspended;
        if (newSuspension)
        {
            Suspended = true;
            ResumeGracePending = ResumeGraceBudget > 0 && !BaselinePending;
            ContinuousSafeScans = 0;
        }
        Current = 0;
        if (reset && newSuspension) Resets = unchecked(Resets + 1);
        return State;
    }

    public uint GenerationAt(int index) => _generations[index];
    public int RepeatTickAt(int index) => _repeatTicks[index];
    public ulong IdentityAt(int index) => _identities[index];
    public ulong PhaseKeyAt(int index) => _phaseKeys[index];

    public static ulong PackIdentity(uint update, ushort enemyId, ushort entityId) =>
        ((ulong)update << 32) | ((ulong)enemyId << 16) | entityId;

    public static ulong PackPhase(short attack, ushort element, ushort hitboxState) =>
        ((ulong)(ushort)attack << 32) | ((ulong)element << 16) | hitboxState;
}
