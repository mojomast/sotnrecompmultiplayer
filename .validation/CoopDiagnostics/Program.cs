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
        string json = P2D4DiagnosticsEnvelope.Serialize(P2D4Report.Parse(Golden), new string('a', 32), 0, 30, 40);
        using JsonDocument document = JsonDocument.Parse(json);
        Equal("p2d4/1", document.RootElement.GetProperty("schema").GetString());
        Equal(0, document.RootElement.GetProperty("generation").GetInt32());
        Equal(Golden, document.RootElement.GetProperty("legacy").GetString());
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
        _ = P2D4DiagnosticsEnvelope.Serialize(P2D4Report.Parse(Golden), new string('a', 32), generation, 0, 0);
    }
    catch (ArgumentException)
    {
        return;
    }
    throw new InvalidOperationException("Inconsistent envelope was accepted.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
