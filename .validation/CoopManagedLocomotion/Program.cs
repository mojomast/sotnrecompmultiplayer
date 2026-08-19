using System.Reflection;
using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("report ordinals are explicit and stable", () =>
    {
        for (int value = 0; value <= 7; value++) Equal(value, (int)(ManagedLocomotion)value);
        for (int value = 0; value <= 13; value++) Equal(value, (int)(ManagedAnimation)value);
        Equal(ManagedLocomotion.Downed, (ManagedLocomotion)7);
        Equal(ManagedAnimation.Downed, (ManagedAnimation)13);
    }),
    ("logical catalog covers exact 43 pose timings", () =>
    {
        int[][] durations =
        [
            [12, 12, 12, 12], [4, 4, 4, 4, 4, 4, 4, 4], [6, 6], [6, 6],
            [5, 5, 5, 5, 5], [2, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4], [255],
            [3, 3], [18], [18], [8], [4], [10], [255]
        ];
        int poses = 0;
        for (int state = 0; state < durations.Length; state++)
        {
            ManagedAnimation animation = (ManagedAnimation)state;
            Equal(durations[state].Length, ManagedLocomotionCatalog.FrameCount(animation));
            for (int frame = 0; frame < durations[state].Length; frame++)
            {
                True(ManagedLocomotionCatalog.TryGetDuration(animation, frame, out int actual));
                Equal(durations[state][frame], actual);
                poses++;
            }
            True(!ManagedLocomotionCatalog.TryGetDuration(animation, durations[state].Length, out _));
        }
        Equal(43, poses);
        Equal(22, ManagedLocomotionCatalog.AttackTotalUpdates);
    }),
    ("selection priority is downed hurt attack crouch landing ground air", () =>
    {
        Equal(ManagedAnimation.Downed, Select(Obs(downed: true, hurt: true, attack: 22,
            crouched: true, grounded: true, landed: true, vy: -1)).Animation);
        Equal(ManagedAnimation.CompactHurt, Select(Obs(hurt: true, compact: true, attack: 22,
            crouched: true, grounded: true, landed: true)).Animation);
        Equal(ManagedAnimation.AttackStartup, Select(Obs(attack: 22, crouched: true,
            grounded: true, landed: true)).Animation);
        Equal(ManagedAnimation.CrouchEnter, Select(Obs(crouched: true, grounded: true,
            landed: true)).Animation);
        Equal(ManagedAnimation.Landing, Select(Obs(grounded: true, landed: true)).Animation);
        Equal(ManagedAnimation.Idle, Select(Obs(grounded: true)).Animation);
        Equal(ManagedAnimation.Walk, Select(Obs(grounded: true, vx: 1)).Animation);
        Equal(ManagedAnimation.JumpRise, Select(Obs(vy: -1)).Animation);
        Equal(ManagedAnimation.Fall, Select(Obs(vy: 0)).Animation);
    }),
    ("fresh buffered landing survives grounded clearing", () =>
    {
        ManagedLocomotionState state = Select(Obs(grounded: false, landed: true, vy: -0x48000));
        Equal(ManagedLocomotion.Idle, state.Locomotion);
        Equal(ManagedAnimation.Landing, state.Animation);
    }),
    ("horizontal intent has legacy selection semantics", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        reducer.Update(Obs(crouched: true, grounded: true));
        reducer.Update(Obs(grounded: true, horizontal: true, vx: 0));
        Equal(ManagedAnimation.Walk, reducer.State.Animation); // Intent skips crouch exit.

        reducer.DiagnosticReset();
        reducer.Update(Obs(grounded: true, horizontal: true, vx: 0, landed: true));
        Equal(ManagedAnimation.Idle, reducer.State.Animation); // Ground movement still follows velocity.
        reducer.Update(Obs(grounded: true, horizontal: false, vx: 2));
        Equal(ManagedAnimation.Walk, reducer.State.Animation);
    }),
    ("loop timing advances only on exact boundary", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        reducer.Update(Obs(grounded: true));
        for (int tick = 1; tick < 12; tick++) reducer.Update(Obs(grounded: true));
        Equal(0, reducer.State.Frame);
        Equal(11, reducer.State.Tick);
        Equal(0L, reducer.State.Advances);
        reducer.Update(Obs(grounded: true));
        Equal(1, reducer.State.Frame);
        Equal(0, reducer.State.Tick);
        Equal(1L, reducer.State.Advances);
        for (int tick = 0; tick < 36; tick++) reducer.Update(Obs(grounded: true));
        Equal(0, reducer.State.Frame);
        Equal(4L, reducer.State.Advances);
    }),
    ("terminal one-shot is strict and held priority repeats accounting", () =>
    {
        var landing = new ManagedLocomotionReducer();
        landing.Update(Obs(grounded: true, landed: true));
        for (int tick = 0; tick < 25; tick++) landing.Update(Obs(grounded: true));
        Equal(4, landing.State.Frame);
        Equal(5, landing.State.Tick);
        Equal(5L, landing.State.Advances);
        landing.Update(Obs(grounded: true));
        Equal(ManagedAnimation.Idle, landing.State.Animation);

        var hurt = new ManagedLocomotionReducer();
        hurt.Update(Obs(hurt: true));
        for (int tick = 0; tick < 18; tick++) hurt.Update(Obs(hurt: true));
        Equal(18, hurt.State.Tick);
        Equal(1L, hurt.State.Advances);
        hurt.Update(Obs(hurt: true));
        Equal(18, hurt.State.Tick);
        Equal(2L, hurt.State.Advances);
    }),
    ("crouch enter hold exit one-shots preserve progression", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        reducer.Update(Obs(crouched: true, grounded: true));
        for (int tick = 0; tick < 50; tick++) reducer.Update(Obs(crouched: true, grounded: true));
        Equal(12, reducer.State.Frame);
        Equal(4, reducer.State.Tick);
        reducer.Update(Obs(crouched: true, grounded: true));
        Equal(ManagedAnimation.CrouchHold, reducer.State.Animation);
        reducer.Update(Obs(grounded: true));
        Equal(ManagedAnimation.CrouchExit, reducer.State.Animation);
        for (int tick = 0; tick < 6; tick++) reducer.Update(Obs(grounded: true));
        Equal(1, reducer.State.Frame);
        Equal(3, reducer.State.Tick);
        reducer.Update(Obs(grounded: true));
        Equal(ManagedAnimation.Idle, reducer.State.Animation);
    }),
    ("attack observes pre-decrement phase then forces boundaries", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        int timer = ManagedLocomotionCatalog.AttackTotalUpdates;
        reducer.Update(Obs(attack: timer));
        Equal(ManagedAnimation.AttackStartup, reducer.State.Animation);
        timer = reducer.AdvanceAttackCountdown(timer).Timer;
        while (timer > 14)
        {
            reducer.Update(Obs(attack: timer));
            ManagedAttackAdvance advance = reducer.AdvanceAttackCountdown(timer);
            timer = advance.Timer;
            if (timer == 14) True(advance.EnteredActive);
        }
        Equal(ManagedAnimation.AttackActive, reducer.State.Animation);
        Equal(1, reducer.State.AttackPhaseCompletionMask);
        Equal(1, reducer.State.Transitions);
        while (timer > 10)
        {
            reducer.Update(Obs(attack: timer));
            ManagedAttackAdvance advance = reducer.AdvanceAttackCountdown(timer);
            timer = advance.Timer;
            if (timer == 10) True(advance.EnteredRecovery);
        }
        Equal(ManagedAnimation.AttackRecovery, reducer.State.Animation);
        Equal(3, reducer.State.AttackPhaseCompletionMask);
        while (timer > 0)
        {
            reducer.Update(Obs(attack: timer));
            ManagedAttackAdvance advance = reducer.AdvanceAttackCountdown(timer);
            timer = advance.Timer;
            if (timer == 0) True(advance.Completed);
        }
        Equal(7, reducer.State.AttackPhaseCompletionMask);
        Equal(2, reducer.State.Transitions);
        Equal(2L, reducer.State.Advances); // Active and recovery reach their exact terminal boundary.
    }),
    ("attack phase mask is cumulative across attacks", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        RunAttack(reducer);
        Equal(7, reducer.State.AttackPhaseCompletionMask);
        int transitions = reducer.State.Transitions;
        RunAttack(reducer);
        Equal(7, reducer.State.AttackPhaseCompletionMask);
        True(reducer.State.Transitions > transitions);
    }),
    ("state and advance masks accumulate exact selected ordinals", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        reducer.Update(Obs(grounded: true));
        for (int tick = 0; tick < 12; tick++) reducer.Update(Obs(grounded: true));
        reducer.Update(Obs(vy: -1));
        Equal((1 << (int)ManagedAnimation.Idle) | (1 << (int)ManagedAnimation.JumpRise),
            reducer.State.StatesSeen);
        Equal(1 << (int)ManagedAnimation.Idle, reducer.State.AdvanceStatesSeen);
        Equal(1, reducer.State.Transitions);
    }),
    ("invalid pose preserves partial legacy update", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        reducer.Update(Obs(grounded: true));
        int seen = reducer.State.StatesSeen;
        SetField(reducer, "_frame", 99);
        reducer.Update(Obs(grounded: true));
        True(!reducer.State.Valid);
        Equal(1, reducer.State.Tick);
        Equal(seen, reducer.State.StatesSeen);
        Equal(0L, reducer.State.Advances);
        reducer.Update(Obs(grounded: true));
        True(reducer.State.Valid);
        Equal(0, reducer.State.Frame);
        Equal(0, reducer.State.Tick);
    }),
    ("invalidate initialize and diagnostic reset differ exactly", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        True(!reducer.State.Valid);
        Equal(ManagedLocomotion.Idle, reducer.State.Locomotion);
        Equal(ManagedAnimation.Idle, reducer.State.Animation);
        reducer.Update(Obs(grounded: true));
        for (int tick = 0; tick < 12; tick++) reducer.Update(Obs(grounded: true));
        ManagedLocomotionState before = reducer.State;
        reducer.Invalidate();
        True(!reducer.State.Valid);
        Equal(before.Frame, reducer.State.Frame);
        Equal(before.Advances, reducer.State.Advances);
        reducer.Update(Obs(vy: -1));
        reducer.Initialize();
        True(!reducer.State.Valid);
        Equal(0, reducer.State.Frame);
        Equal(0, reducer.State.Tick);
        True(reducer.State.StatesSeen != 0 && reducer.State.Advances != 0);
        reducer.DiagnosticReset();
        ManagedLocomotionState reset = reducer.State;
        True(!reset.Valid);
        Equal(ManagedLocomotion.Falling, reset.Locomotion);
        Equal(ManagedAnimation.Fall, reset.Animation);
        Equal(0, reset.Frame);
        Equal(0, reset.Tick);
        Equal(0, reset.Transitions);
        Equal(0L, reset.Advances);
        Equal(0, reset.StatesSeen);
        Equal(0, reset.AdvanceStatesSeen);
        Equal(0, reset.AttackPhaseCompletionMask);
    }),
    ("generated event sequences are deterministic and valid", () =>
    {
        for (uint seed = 1; seed <= 128; seed++) Equal(Replay(seed, 4_000), Replay(seed, 4_000));
    }),
    ("steady-state hot path allocates nothing", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        ulong digest = 0;
        for (int index = 0; index < 1_000; index++) digest ^= Reduce(reducer, index);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++) digest ^= Reduce(reducer, index);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        GC.KeepAlive(digest);
    }),
    ("replay projection preserves canonical ordinal layout", () =>
    {
        var reducer = new ManagedLocomotionReducer();
        reducer.Update(Obs(hurt: true, compact: true));
        ManagedLocomotionState state = reducer.State;
        var input = new ManagedInputFrame(1, 1, 0, 0, true);
        var snapshot = new ManagedProxySnapshot(1, 1, ManagedMovementSessionPhase.Active,
            new ManagedRoomKey(1, 2, 3, 0, 0, 256, 240),
            0, 0, 0, 0, true, false, false, false, false, 0, 0,
            (byte)state.Locomotion, (byte)state.Animation, state.Frame, state.Tick);
        byte[] bytes = ManagedStateCodec.WriteCanonical(input, snapshot);
        Equal(ManagedStateCodec.CanonicalLength, bytes.Length);
        Equal((byte)ManagedLocomotion.Hurt, bytes[106]);
        Equal((byte)ManagedAnimation.CompactHurt, bytes[107]);
        Equal(0, BitConverter.ToInt32(bytes, 108));
        Equal(0, BitConverter.ToInt32(bytes, 112));
        Equal(0x7F4A4F2C11375097UL, ManagedStateCodec.Hash(input, snapshot));
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopManagedLocomotion: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static ManagedLocomotionObservation Obs(bool downed = false, bool hurt = false, bool compact = false,
    int attack = 0, bool crouched = false, bool grounded = false, bool horizontal = false,
    int vx = 0, int vy = 0, bool landed = false) =>
    new(downed, hurt, compact, attack, crouched, grounded, horizontal, vx, vy, landed);

static ManagedLocomotionState Select(ManagedLocomotionObservation observation)
{
    var reducer = new ManagedLocomotionReducer();
    return reducer.Update(observation);
}

static void RunAttack(ManagedLocomotionReducer reducer)
{
    int timer = ManagedLocomotionCatalog.AttackTotalUpdates;
    while (timer > 0)
    {
        reducer.Update(Obs(attack: timer));
        timer = reducer.AdvanceAttackCountdown(timer).Timer;
    }
}

static ulong Replay(uint seed, int steps)
{
    var reducer = new ManagedLocomotionReducer();
    ulong digest = 14695981039346656037UL;
    int attackTimer = 0;
    for (int index = 0; index < steps; index++)
    {
        seed = Next(seed);
        if ((seed & 255) == 0) reducer.DiagnosticReset();
        else if ((seed & 127) == 1) reducer.Initialize();
        else if ((seed & 63) == 2) reducer.Invalidate();
        if (attackTimer == 0 && (seed & 31) == 3) attackTimer = ManagedLocomotionCatalog.AttackTotalUpdates;
        bool downed = (seed & 1023) == 5;
        bool hurt = !downed && (seed & 31) == 7;
        bool grounded = (seed & 1) != 0;
        reducer.Update(Obs(downed, hurt, (seed & 64) != 0, attackTimer,
            (seed & 8) != 0, grounded, (seed & 16) != 0,
            (seed & 2) == 0 ? 0 : (seed & 4) == 0 ? -1 : 1,
            (seed & 4) == 0 ? -1 : 1, (seed & 128) != 0));
        if (attackTimer > 0) attackTimer = reducer.AdvanceAttackCountdown(attackTimer).Timer;
        ManagedLocomotionState state = reducer.State;
        True(state.Frame >= 0 && state.Tick >= 0);
        digest = Mix(digest, (ulong)(byte)state.Locomotion);
        digest = Mix(digest, (ulong)(byte)state.Animation);
        digest = Mix(digest, unchecked((ulong)state.Frame));
        digest = Mix(digest, unchecked((ulong)state.Tick));
        digest = Mix(digest, unchecked((ulong)state.Transitions));
        digest = Mix(digest, unchecked((ulong)state.Advances));
        digest = Mix(digest, unchecked((ulong)state.StatesSeen));
        digest = Mix(digest, unchecked((ulong)state.AdvanceStatesSeen));
        digest = Mix(digest, unchecked((ulong)state.AttackPhaseCompletionMask));
    }
    return digest;
}

static ulong Reduce(ManagedLocomotionReducer reducer, int index)
{
    bool grounded = (index & 7) < 5;
    ManagedLocomotionState state = reducer.Update(Obs(grounded: grounded,
        horizontal: (index & 3) == 0, vx: (index & 3) == 0 ? 1 : 0,
        vy: grounded ? 0 : (index & 1) == 0 ? -1 : 1, landed: (index & 63) == 9));
    return unchecked((ulong)state.Frame ^ ((ulong)state.Tick << 8) ^ ((ulong)state.StatesSeen << 16));
}

static uint Next(uint value)
{
    value ^= value << 13;
    value ^= value >> 17;
    value ^= value << 5;
    return value;
}

static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);

static void SetField(ManagedLocomotionReducer reducer, string name, object value)
{
    FieldInfo? field = typeof(ManagedLocomotionReducer).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    if (field == null) throw new InvalidOperationException($"Missing reducer field {name}.");
    field.SetValue(reducer, value);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}
