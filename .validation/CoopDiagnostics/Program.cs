using System.Text.Json;
using CoopFeasibilityMod;

const string Golden = "P2D4 VER=0.4.0 H=W:0/0/0/0/0 I=W:K:-/0/0/0/A- K=0/0/H0000/R0000/U0000/N0/S0 M=W:0/0/0 R=W:0/0/0/D0/H00 N=W:0/0/0/0/0/S0/F0/LWAIT B=W:F0/S0/E0/O0/C0/P0/D0/I0/T0/X0/R0/U0,1,0/G0,0/V0/H0,0,0,0/Qnone/LWAIT C=W:0/0/0/0/0/0/0/B00 T=W:0/0/0/R0,0,0,0,H0/LWAIT S=W:-/-/0 G=WAIT:E1S0P0 Q=0/0/0/0 A=I:0/0 E=0 D=W:3/3/0/0/S0/F0/Q0/P0/T0/A0/H0,0,0,0,0/C00 VIS=W:F00/P0000000000000000/H0000000000000000/E0/S0/R0,0/X0/LWAIT J=W:N0/C0/B0/R0,0 X=W:IDLE/T0/O-1,0/Q-1,0,0,0/A0/W0/C0,0/F0,0/I0,0/G0,-1,0/R0,0,0,0/P-1,0,0,0,0/E0,0,0,0/J0,0 EN=W:S0/N0/C0/T-1,0,0,0,0,0,0/H0,0,0/LEMPTY AW=W:C0/O0/S-1/LWAIT HU=W:E0/S0 HP=W:100/100/I0/K0/D0,0,0,0,0,-1,0/N0,0/R0,0,0,0,0/F0";

var tests = new List<(string Name, Action Run)>
{
    ("golden canonical round trip", () =>
    {
        P2D4Report report = P2D4Report.Parse(Golden);
        Equal(Golden, report.CanonicalLine);
        Equal(23, report.Fields.Count);
        Equal("0.4.0", report.Fields["VER"]);
        Equal(P2D4Result.Wait, report.Result("HP"));
    }),
    ("structured envelope identity", () =>
    {
        string json = P2D4DiagnosticsEnvelope.Serialize(P2D4Report.Parse(Golden), new string('a', 32), 0, 30, 40,
            EmptyMetrics());
        using JsonDocument document = JsonDocument.Parse(json);
        Equal("p2d4/2", document.RootElement.GetProperty("schema").GetString());
        Equal(0, document.RootElement.GetProperty("generation").GetInt32());
        Equal(Golden, document.RootElement.GetProperty("legacy").GetString());
        Equal(23, document.RootElement.GetProperty("fields").EnumerateObject().Count());
        Equal(JsonValueKind.Null, document.RootElement.GetProperty("transitionTrace").ValueKind);
        JsonElement metrics = document.RootElement.GetProperty("metrics");
        Equal(99, metrics.EnumerateObject().Count());
        Equal(JsonValueKind.Number, metrics.GetProperty("sessionRoomEpoch").ValueKind);
        Equal(0L, metrics.GetProperty("attackExactOwnedLifetimeCurrent").GetInt64());
        Equal(0L, metrics.GetProperty("attackExactOwnedLifetimeMaximum").GetInt64());
        Equal(JsonValueKind.False, metrics.GetProperty("fatal").ValueKind);
        Equal(JsonValueKind.String, metrics.GetProperty("errorCode").ValueKind);
        foreach (JsonProperty metric in metrics.EnumerateObject())
            if (metric.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.True or
                JsonValueKind.False or JsonValueKind.String))
                throw new InvalidOperationException($"Metric {metric.Name} is not scalar.");
    }),
    ("transition trace room identity and bounds serialize", () =>
    {
        var origin = new ManagedRoomKey(17, 34, 51, -64, 128, 448, 608);
        var current = new ManagedRoomKey(68, 85, 102, 16, -32, 336, 208);
        var trace = new[]
        {
            new MovementTransitionTraceEntry(123, MovementTransitionTraceSource.RoomLayer, origin, current,
                true, false, "selected", "none")
        };
        string json = P2D4DiagnosticsEnvelope.Serialize(P2D4Report.Parse(Golden), new string('a', 32), 0, 30, 40,
            EmptyMetrics(), trace);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement entry = document.RootElement.GetProperty("transitionTrace")[0];
        AssertRoom(entry.GetProperty("origin"), 17, 51, 34, -64, 128, 448, 608);
        AssertRoom(entry.GetProperty("current"), 68, 102, 85, 16, -32, 336, 208);
    }),
    ("envelope generation mismatch rejected", () => RejectEnvelope(1)),
    ("duplicate key rejected", () => Reject(Golden.Replace(" H=W:0/0/0/0/0", " H=W:0/0/0/0/0 H=W:0/0/0/0/0"))),
    ("missing key rejected", () => Reject(Golden.Replace(" HU=W:E0/S0", ""))),
    ("unknown key rejected", () => Reject(Golden.Replace(" HU=W:E0/S0", " ZZ=0"))),
    ("illegal predicate rejected", () => Reject(Golden.Replace(" M=W:", " M=F:"))),
    ("reset invariant rejected", () => Reject(Golden.Replace(" Q=0/0/0/0", " Q=0/0/1/0"))),
    ("contact slot invariant rejected", () => Reject(Golden.Replace(" B=W:F0/S0/", " B=W:F1/S0/"))),
    ("render invariant rejected", () => Reject(Golden.Replace(" R=W:0/0/", " R=W:1/0/"))),
    ("attack cleanup invariant rejected", () => Reject(Golden.Replace("/A0/W0/C0,0/", "/A0/W0/C1,0/"))),
    ("awareness invariant rejected", () => Reject(Golden.Replace(" AW=W:C0/O0/", " AW=W:C0/O1/"))),
    ("HUD invariant rejected", () => Reject(Golden.Replace(" HU=W:E0/S0", " HU=W:E0/S1"))),
    ("health range rejected", () => Reject(Golden.Replace(" HP=W:100/100/", " HP=W:101/100/"))),
    ("valid guard failure accepted", () =>
        _ = P2D4Report.Parse(Golden.Replace(" B=W:F0/S0/", " B=F:F0/S0/").Replace("/G0,0/V0/", "/G1,1/V0/"))),
    ("valid pre-allocation cleanup failure accepted", () =>
        _ = P2D4Report.Parse(Golden.Replace(" X=W:IDLE/", " X=F:FAIL/").Replace("/A0/W0/C0,0/", "/A0/W0/C1,1/"))),
    ("ASCII decimal overflow rejected", () =>
        Reject(Golden.Replace(" Q=0/0/0/0", " Q=9223372036854775808/0/0/0"))),
    ("Unicode digits rejected", () => Reject(Golden.Replace(" Q=0/0/0/0", " Q=٠/0/0/0"))),
    ("embedded newline rejected", () => Reject(Golden.Replace(" HU=W:E0/S0", " HU=W:E0/S0\n"))),
    ("oversized report rejected", () => Reject(Golden + new string('x', P2D4Report.MaximumUtf8Bytes)))
    ,("virtual keyboard preference defaults and persists", () =>
    {
        var store = new PreferenceStore();
        Equal(true, VirtualKeyboardPreference.Load(store));
        VirtualKeyboardPreference.Persist(store, false);
        Equal(false, VirtualKeyboardPreference.Load(store));
        Equal(VirtualKeyboardPreference.Key, store.LastKey);
        Equal(1, store.Saves);
    })
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

Console.WriteLine($"CoopDiagnostics: {tests.Count - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static void Reject(string line)
{
    try
    {
        _ = P2D4Report.Parse(line);
    }
    catch (FormatException)
    {
        return;
    }
    throw new InvalidOperationException("Malformed report was accepted.");
}

static void RejectEnvelope(int generation)
{
    try
    {
        _ = P2D4DiagnosticsEnvelope.Serialize(P2D4Report.Parse(Golden), new string('a', 32), generation, 0, 0,
            EmptyMetrics());
    }
    catch (ArgumentException)
    {
        return;
    }
    throw new InvalidOperationException("Inconsistent envelope was accepted.");
}

static void AssertRoom(JsonElement room, int stage, int area, int number, int left, int top, int right, int bottom)
{
    Equal(stage, room.GetProperty("stage").GetInt32());
    Equal(area, room.GetProperty("area").GetInt32());
    Equal(number, room.GetProperty("room").GetInt32());
    Equal(left, room.GetProperty("left").GetInt32());
    Equal(top, room.GetProperty("top").GetInt32());
    Equal(right, room.GetProperty("right").GetInt32());
    Equal(bottom, room.GetProperty("bottom").GetInt32());
}

static P2D4Metrics EmptyMetrics() => new(
    SessionRoomEpoch: 0, TransitionPassed: 0, TransitionCompleted: 0,
    ReconstructionAttempts: 0, ReconstructionSuccesses: 0, ReconstructionFailures: 0,
    ReconstructionRetryCooldown: 0, ReconstructionRetries: 0,
    ReconstructionSuppressedAttempts: 0, ReconstructionSuspensionReasonCode: 0,
    TransitionPending: false, AwaitingPostTransitionMovement: false, TetherRecoveries: 0,
    PostTransitionCommandedPixels: 0, PostTransitionMoved: false,
    TransitionPendingUpdates: 0, TransitionPendingMaxUpdates: 0,
    PostTransitionAbandonments: 0, TransitionReconstructionFailures: 0,
    TetherPhase: 5, TetherReasonCode: 6, TetherWarningEntries: 0,
    TetherResistanceEntries: 0, TetherReconstructionEntries: 0, TetherSuspensionEntries: 0,
    TetherWarningFrames: 0, TetherWarningMaxConsecutive: 0, TetherResistanceFrames: 0,
    TetherResistanceMaxConsecutive: 0, TetherReconstructionFrames: 0,
    TetherReconstructionMaxConsecutive: 0, TetherSuspensionFrames: 0,
    TetherSuspensionMaxConsecutive: 0, TetherOutwardResistance: false,
    TetherStatusEligible: 0, TetherStatusSubmitted: 0, TetherHardRecoveries: 0,
    HealthHp: 100, HealthDowned: false, HealthDamageEvents: 0, HealthDamageConsumed: 0,
    HealthSuppressions: 0, HealthHitSuppressions: 0, HealthDowns: 0, HealthReviveStarts: 0,
    HealthReviveCancels: 0, HealthRevives: 0, HealthRecoveries: 0, HealthInvariantFailures: 0,
    AttackAllocations: 0, AttackContactAllocations: 0, AttackProjectileAllocations: 0,
    AttackCleanups: 0, AttackLifecycleCancellations: 0, AttackFailures: 0, AttackTimingFailures: 0,
    AttackContactWindows: 0, AttackProjectileWindows: 0, AttackContactNativeHits: 0,
    AttackProjectileNativeHits: 0, AttackProjectileLifetime: 0,
    AttackExactOwnedLifetimeCurrent: 0, AttackExactOwnedLifetimeMaximum: 0, AttackQuarantineSlot: -1,
    AttackCleanupPending: false, AttackEquipmentRestoreFailures: 0, AttackMarkerCount: 0,
    AttackOrphanMarkerCount: 0, AttackTargetOverflowEvents: 0, CompatibleTargetCurrent: 0,
    EnemyNativeHits: 0, EnemyDefeats: 0, EnemyZeroHpHits: 0,
    DropScans: 0, DropActive: 0, DropMaximumActive: 0, DropPrizeSpawns: 0,
    DropEquipmentSpawns: 0, DropP2AssociatedSpawns: 0, DropAmbientSpawns: 0,
    DropAmbiguousSpawns: 0, DropCausalDefeatsWithoutDrop: 0, DropTrackerOverflowEvents: 0,
    DropTrackerFaulted: false, DropCollections: 0, DropExpirations: 0,
    DropLifecycleDisappears: 0, DropReuses: 0, DropUnresolvedPickups: 0,
    ObservedNativeExpEvents: 0, ObservedNativeExpDelta: 0,
    ContactGuardChecks: 0, ContactGuardFailures: 0, ContactSuspended: false,
    CollisionRestoreFailures: 0, VisualRestoreFailures: 0, Fatal: false, ErrorCode: "0",
    ConfiguredProcessedPad2Available: false);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

sealed class PreferenceStore : IBooleanPreferenceStore
{
    private bool? _value;
    public string? LastKey { get; private set; }
    public int Saves { get; private set; }
    public bool GetBool(string key, bool defaultValue) { LastKey = key; return _value ?? defaultValue; }
    public void SetBool(string key, bool value) { LastKey = key; _value = value; }
    public void Save() => Saves++;
}
