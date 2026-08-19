using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("initial state is ready nonzero and empty", () =>
    {
        var reducer = new JumpForgivenessReducer();
        True(reducer.State.Revision != 0);
        Equal(JumpForgivenessPhase.PrePhysics, reducer.State.Phase);
        Equal(0, reducer.CoyoteUpdates);
        Equal(0, reducer.BufferUpdates);
    }),
    ("grounded tap has priority and clears both windows", () =>
    {
        var reducer = new JumpForgivenessReducer();
        WalkOff(reducer);
        JumpForgivenessTransition jump = reducer.BeginUpdate(true, true, false);
        Equal(JumpForgivenessRequest.Normal, jump.Request);
        Equal(0, reducer.CoyoteUpdates);
        Equal(0, reducer.BufferUpdates);
        Equal(JumpForgivenessRequest.None,
            reducer.CompleteUpdate(jump.Continuation, false).Request);
    }),
    ("walking off initializes four without same-update decay", () =>
    {
        var reducer = new JumpForgivenessReducer();
        WalkOff(reducer);
        Equal(4, reducer.CoyoteUpdates);
        Step(reducer, false, false, false, false);
        Equal(3, reducer.CoyoteUpdates);
    }),
    ("coyote boundaries allow exactly four following updates", () =>
    {
        var reducer = new JumpForgivenessReducer();
        WalkOff(reducer);
        for (int remaining = 4; remaining > 0; remaining--)
        {
            Equal(remaining, reducer.CoyoteUpdates);
            JumpForgivenessTransition begin = reducer.BeginUpdate(remaining == 1, false, false);
            Equal(remaining == 1 ? JumpForgivenessRequest.Coyote : JumpForgivenessRequest.None,
                begin.Request);
            reducer.CompleteUpdate(begin.Continuation, false);
        }
        Equal(0, reducer.CoyoteUpdates);
    }),
    ("airborne tap stores four then decays to three", () =>
    {
        var reducer = new JumpForgivenessReducer();
        JumpForgivenessTransition begin = reducer.BeginUpdate(true, false, false);
        Equal(4, reducer.BufferUpdates);
        Equal(JumpForgivenessRequest.None, begin.Request);
        reducer.CompleteUpdate(begin.Continuation, false);
        Equal(3, reducer.BufferUpdates);
    }),
    ("landing consumes buffer before decay", () =>
    {
        var reducer = AirTap();
        JumpForgivenessTransition complete = Step(reducer, false, false, true, false);
        Equal(JumpForgivenessRequest.Buffered, complete.Request);
        Equal(0, reducer.BufferUpdates);
        Equal(0, reducer.CoyoteUpdates);
    }),
    ("crouch suppresses pre-physics grounded jump but post edge consumes it", () =>
    {
        var reducer = new JumpForgivenessReducer();
        JumpForgivenessTransition begin = reducer.BeginUpdate(true, true, true);
        Equal(JumpForgivenessRequest.None, begin.Request);
        Equal(4, reducer.BufferUpdates);
        JumpForgivenessTransition complete = reducer.CompleteUpdate(begin.Continuation, true);
        Equal(JumpForgivenessRequest.Buffered, complete.Request);
        Equal(0, reducer.BufferUpdates);
    }),
    ("one update cannot request a second jump", () =>
    {
        var reducer = new JumpForgivenessReducer();
        JumpForgivenessTransition begin = reducer.BeginUpdate(true, true, false);
        Equal(JumpForgivenessRequest.Normal, begin.Request);
        Equal(JumpForgivenessRequest.None,
            reducer.CompleteUpdate(begin.Continuation, true).Request);
    }),
    ("clear invalidates retained continuation", () =>
    {
        var reducer = new JumpForgivenessReducer();
        JumpForgivenessTransition begin = reducer.BeginUpdate(true, false, false);
        JumpForgivenessContinuation retained = begin.Continuation;
        ulong issuedRevision = retained.Revision;
        reducer.Clear();
        True(reducer.State.Revision != 0 && reducer.State.Revision != issuedRevision);
        Equal(JumpForgivenessPhase.PrePhysics, reducer.State.Phase);
        Equal(0, reducer.BufferUpdates);
        RejectInvalidOperation(() => reducer.CompleteUpdate(retained, false));
    }),
    ("advance invalidates retained continuation and duplicate finish", () =>
    {
        var reducer = new JumpForgivenessReducer();
        JumpForgivenessContinuation first = reducer.BeginUpdate(false, true, false).Continuation;
        reducer.CompleteUpdate(first, true);
        RejectInvalidOperation(() => reducer.CompleteUpdate(first, true));

        JumpForgivenessContinuation second = reducer.BeginUpdate(false, true, false).Continuation;
        True(second.Revision != 0 && second.Revision != first.Revision);
        RejectInvalidOperation(() => reducer.CompleteUpdate(first, true));
        reducer.CompleteUpdate(second, true);
    }),
    ("continuation is rejected by wrong owner at equal revision", () =>
    {
        var first = new JumpForgivenessReducer();
        var second = new JumpForgivenessReducer();
        JumpForgivenessContinuation firstToken = first.BeginUpdate(false, true, false).Continuation;
        JumpForgivenessContinuation secondToken = second.BeginUpdate(false, true, false).Continuation;
        Equal(firstToken.Revision, secondToken.Revision);
        RejectInvalidOperation(() => second.CompleteUpdate(firstToken, true));
        second.CompleteUpdate(secondToken, true);
        first.CompleteUpdate(firstToken, true);
    }),
    ("retained token cannot revive at revision exhaustion", () =>
    {
        var reducer = new JumpForgivenessReducer();
        SetRevision(reducer, ulong.MaxValue - 1);
        JumpForgivenessContinuation retained = reducer.BeginUpdate(false, true, false).Continuation;
        Equal(ulong.MaxValue, retained.Revision);
        reducer.CompleteUpdate(retained, true);
        RejectInvalidOperation(() => reducer.BeginUpdate(false, true, false));
        Equal(ulong.MaxValue, reducer.State.Revision);
        Equal(JumpForgivenessPhase.PrePhysics, reducer.State.Phase);
        RejectInvalidOperation(() => reducer.CompleteUpdate(retained, true));
        RejectInvalidOperation(() => reducer.Clear());
        Equal(ulong.MaxValue, reducer.State.Revision);
    }),
    ("out-of-order and default continuation fail closed", () =>
    {
        var reducer = new JumpForgivenessReducer();
        RejectInvalidOperation(() => reducer.CompleteUpdate(default, false));
        JumpForgivenessTransition pending = reducer.BeginUpdate(false, true, false);
        RejectInvalidOperation(() => reducer.BeginUpdate(false, true, false));
        RejectInvalidOperation(() => reducer.CompleteUpdate(default, true));
        reducer.CompleteUpdate(pending.Continuation, true);
    }),
    ("generated sequences are deterministic and bounded", () =>
    {
        for (uint seed = 1; seed <= 128; seed++) Equal(Replay(seed, 2_000), Replay(seed, 2_000));
    }),
    ("steady-state reduction allocates nothing", () =>
    {
        var reducer = new JumpForgivenessReducer();
        for (int index = 0; index < 256; index++) Step(reducer, false, false, false, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++)
            Step(reducer, (index & 31) == 0, false, false, false);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"CoopJumpForgiveness: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static void WalkOff(JumpForgivenessReducer reducer) => Step(reducer, false, true, false, false);

static JumpForgivenessReducer AirTap()
{
    var reducer = new JumpForgivenessReducer();
    Step(reducer, true, false, false, false);
    return reducer;
}

static JumpForgivenessTransition Step(JumpForgivenessReducer reducer, bool tap, bool wasGrounded,
    bool groundedAfterPhysics, bool crouched)
{
    JumpForgivenessTransition begin = reducer.BeginUpdate(tap, wasGrounded, crouched);
    return reducer.CompleteUpdate(begin.Continuation, groundedAfterPhysics);
}

static ulong Replay(uint seed, int steps)
{
    var reducer = new JumpForgivenessReducer();
    bool grounded = true;
    ulong digest = 14695981039346656037UL;
    for (int index = 0; index < steps; index++)
    {
        seed = Next(seed);
        if ((seed & 63) == 0)
        {
            reducer.Clear();
            grounded = (seed & 64) != 0;
        }
        else
        {
            bool tap = (seed & 7) == 0;
            bool crouched = (seed & 16) != 0;
            bool groundedAfter = (seed & 3) == 0;
            JumpForgivenessTransition begin = reducer.BeginUpdate(tap, grounded, crouched);
            JumpForgivenessTransition complete = reducer.CompleteUpdate(begin.Continuation, groundedAfter);
            grounded = begin.Request == JumpForgivenessRequest.None &&
                complete.Request == JumpForgivenessRequest.None && groundedAfter;
            digest = Mix(digest, (ulong)begin.Request);
            digest = Mix(digest, (ulong)complete.Request);
        }
        JumpForgivenessState state = reducer.State;
        True(state.Revision != 0);
        True(state.Phase == JumpForgivenessPhase.PrePhysics);
        True(state.CoyoteUpdates is >= 0 and <= 4);
        True(state.BufferUpdates is >= 0 and <= 4);
        digest = Mix(digest, state.Revision);
        digest = Mix(digest, (ulong)state.CoyoteUpdates);
        digest = Mix(digest, (ulong)state.BufferUpdates);
    }
    return digest;
}

static uint Next(uint value)
{
    value ^= value << 13;
    value ^= value >> 17;
    value ^= value << 5;
    return value;
}

static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

static void RejectInvalidOperation(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Invalid jump continuation or phase was accepted.");
}

static void SetRevision(JumpForgivenessReducer reducer, ulong revision)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic;
    typeof(JumpForgivenessReducer).GetField("_revision", flags)?.SetValue(reducer, revision);
    Equal(revision, reducer.State.Revision);
}
