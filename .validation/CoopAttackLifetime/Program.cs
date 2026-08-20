using CoopFeasibilityMod;

var tests = new List<(string Name, Action Run)>
{
    ("contact lifetime is one exact window", () => Equal(new(1, 1), Run(1))),
    ("projectile lifetime is forty exact windows", () => Equal(new(40, 40), Run(40))),
    ("overlong lifetime is retained between diagnostic polls", () => Equal(new(49, 49), Run(49))),
    ("cleanup clears current and preserves maximum", () =>
    {
        ExactOwnedAttackLifetime state = ExactOwnedAttackLifetimeReducer.Observe(Run(40), false);
        Equal(new(0, 40), state);
    }),
    ("diagnostic reset clears maximum", () => Equal(default, ExactOwnedAttackLifetimeReducer.Reset())),
    ("counter exhaustion fails closed without wrapping", () =>
    {
        var state = new ExactOwnedAttackLifetime(long.MaxValue, long.MaxValue);
        Throws<InvalidOperationException>(() => ExactOwnedAttackLifetimeReducer.Observe(state, true));
        Equal(long.MaxValue, state.Current);
    }),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); }
}
Console.WriteLine($"CoopAttackLifetime: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static ExactOwnedAttackLifetime Run(int windows)
{
    ExactOwnedAttackLifetime state = default;
    for (int index = 0; index < windows; index++)
        state = ExactOwnedAttackLifetimeReducer.Observe(state, true);
    return state;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
