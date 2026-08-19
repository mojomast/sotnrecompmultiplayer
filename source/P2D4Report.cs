using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CoopFeasibilityMod;

public enum P2D4Result
{
    Pass,
    Wait,
    Fail
}

public sealed class P2D4Report
{
    public const string Prefix = "P2D4";
    public const int MaximumUtf8Bytes = 16 * 1024;

    private static readonly string[] FieldOrder =
    [
        "VER", "H", "I", "K", "M", "R", "N", "B", "C", "T", "S", "G", "Q", "A", "E",
        "D", "VIS", "J", "X", "EN", "AW", "HU", "HP"
    ];

    private static readonly HashSet<string> PassWaitFields = new(StringComparer.Ordinal)
    {
        "I", "M", "R", "N", "J"
    };

    private static readonly HashSet<string> PassWaitFailFields = new(StringComparer.Ordinal)
    {
        "H", "B", "C", "T", "S", "D", "VIS", "X", "EN", "AW", "HU", "HP"
    };

    private readonly Dictionary<string, string> _fields;

    private P2D4Report(Dictionary<string, string> fields)
    {
        _fields = fields;
        CanonicalLine = $"{Prefix} {string.Join(' ', FieldOrder.Select(key => $"{key}={fields[key]}"))}";
        DiagnosticGeneration = checked((int)Tuple(fields["Q"], null, null, 4, "Q")[0]);
    }

    public IReadOnlyDictionary<string, string> Fields => _fields;
    public string CanonicalLine { get; }
    public int DiagnosticGeneration { get; }

    public P2D4Result Result(string key)
    {
        if (!PassWaitFields.Contains(key) && !PassWaitFailFields.Contains(key))
            throw new ArgumentException($"P2D4 {key} is not a predicate field.", nameof(key));
        return _fields[key][0] switch
        {
            'P' => P2D4Result.Pass,
            'W' => P2D4Result.Wait,
            'F' => P2D4Result.Fail,
            _ => throw new InvalidOperationException("Validated result state became invalid.")
        };
    }

    public static P2D4Report Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Encoding.UTF8.GetByteCount(line) > MaximumUtf8Bytes)
            throw new FormatException($"P2D4 exceeds {MaximumUtf8Bytes} UTF-8 bytes.");
        if (line.Any(character => character is < ' ' or > '~'))
            throw new FormatException("P2D4 must contain printable ASCII only.");

        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != FieldOrder.Length + 1 || tokens[0] != Prefix)
            throw new FormatException("P2D4 must contain the exact required field count.");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < tokens.Length; index++)
        {
            int equals = tokens[index].IndexOf('=');
            if (equals <= 0 || equals == tokens[index].Length - 1)
                throw new FormatException("Every P2D4 field must use KEY=value syntax.");
            string key = tokens[index][..equals];
            string value = tokens[index][(equals + 1)..];
            if (!fields.TryAdd(key, value)) throw new FormatException($"Duplicate P2D4 key: {key}.");
        }

        if (fields.Count != FieldOrder.Length || FieldOrder.Any(key => !fields.ContainsKey(key)) ||
            fields.Keys.Any(key => !FieldOrder.Contains(key, StringComparer.Ordinal)))
            throw new FormatException("P2D4 keys do not match the schema.");

        ValidateVersion(fields["VER"]);
        foreach (string key in PassWaitFields) ValidateState(key, fields[key], allowFailure: false);
        foreach (string key in PassWaitFailFields) ValidateState(key, fields[key], allowFailure: true);
        ValidateInvariants(fields);
        return new P2D4Report(fields);
    }

    private static void ValidateVersion(string value)
    {
        int suffix = value.IndexOfAny(['-', '+']);
        string core = suffix < 0 ? value : value[..suffix];
        string[] parts = core.Split('.');
        if (parts.Length != 3 || parts.Any(part => !CanonicalUnsigned(part)))
            throw new FormatException("P2D4 VER must use semantic version syntax.");
        if (suffix >= 0 && (suffix == value.Length - 1 || value[(suffix + 1)..].Any(character =>
                !IsAsciiAlphaNumeric(character) && character is not ('.' or '-' or '+'))))
            throw new FormatException("P2D4 VER suffix is invalid.");
    }

    private static void ValidateState(string key, string value, bool allowFailure)
    {
        if (value.Length < 2 || value[1] != ':' || value[0] is not ('P' or 'W' or 'F'))
            throw new FormatException($"P2D4 {key} has an invalid result state.");
        if (!allowFailure && value[0] == 'F')
            throw new FormatException($"P2D4 {key} does not define a failure state.");
    }

    private static void ValidateInvariants(IReadOnlyDictionary<string, string> fields)
    {
        long[] q = Tuple(fields["Q"], null, null, 4, "Q");
        Require(q[3] <= 1, "Q pending must be boolean.");
        Require(q[2] <= q[1], "Q reset completions exceed requests.");

        long[] render = Tuple(fields["R"], ":", "/D", 3, "R", delimiter: '/');
        Require(render[2] <= 1, "R confirmation must be boolean.");
        Require(render[0] <= render[1], "R submitted exceeds eligible.");

        long visualEligible = UnsignedSection(fields["VIS"], "/E", "/S", "VIS eligible");
        long visualSubmitted = UnsignedSection(fields["VIS"], "/S", "/R", "VIS submitted");
        long[] visualRestore = Tuple(fields["VIS"], "/R", "/X", 2, "VIS restore");
        Require(visualSubmitted <= visualEligible, "VIS submitted exceeds eligible.");
        Require(visualRestore[1] <= visualRestore[0], "VIS restore failures exceed checks.");

        long scans = UnsignedSection(fields["B"], ":F", "/S", "B scans");
        long slots = UnsignedSection(fields["B"], "/S", "/E", "B slots");
        long current = UnsignedSection(fields["B"], "/C", "/P", "B current");
        long peak = UnsignedSection(fields["B"], "/P", "/D", "B peak");
        long[] guard = Tuple(fields["B"], "/G", "/V", 2, "B guard");
        Require(scans <= long.MaxValue / 128 && slots == scans * 128, "B slots must equal scans times 128.");
        Require(current <= peak && peak <= 128, "B contact occupancy is invalid.");
        Require(guard[1] <= guard[0], "B guard failures exceed checks.");
        Require(guard[1] == 0 ? guard[0] == scans : guard[0] >= scans && guard[0] - scans <= guard[1],
            "B guard checks are inconsistent with scans and failures.");

        long[] transitionHead = Tuple(fields["T"], ":", "/R", 3, "T transitions", delimiter: '/');
        long[] reconstruction = Tuple(fields["T"], "/R", "/L", 5, "T reconstruction", 'H');
        Require(transitionHead[0] <= transitionHead[1], "T passed transitions exceed completed transitions.");
        Require(reconstruction[2] <= reconstruction[1] && reconstruction[3] <= reconstruction[1],
            "T reconstruction outcomes exceed attempts.");

        long allocations = UnsignedSection(fields["X"], "/A", "/W", "X allocations");
        long[] cleanup = Tuple(fields["X"], "/C", "/F", 2, "X cleanup");
        if (fields["X"][0] != 'F')
            Require(cleanup[0] <= allocations && cleanup[1] <= cleanup[0], "X cleanup counts are inconsistent.");

        long awarenessCalls = UnsignedSection(fields["AW"], ":C", "/O", "AW calls");
        long awarenessOverrides = UnsignedSection(fields["AW"], "/O", "/S", "AW overrides");
        Require(awarenessOverrides <= awarenessCalls, "AW overrides exceed calls.");

        long hudEligible = UnsignedSection(fields["HU"], ":E", "/S", "HU eligible");
        long hudSubmitted = UnsignedSection(fields["HU"], "/S", null, "HU submitted");
        Require(hudSubmitted <= hudEligible, "HU submitted exceeds eligible.");
        if (fields["HU"][0] == 'P') Require(hudEligible >= 60 && hudSubmitted == hudEligible,
            "HU pass requires at least 60 matching eligible and submitted draws.");

        long[] health = Tuple(fields["HP"], ":", "/I", 2, "HP range", delimiter: '/');
        long[] downed = Tuple(fields["HP"], "/N", "/R", 2, "HP downed");
        long[] revive = Tuple(fields["HP"], "/R", "/F", 5, "HP revive");
        Require(health[1] == 100 && health[0] <= health[1], "HP range is invalid.");
        Require(downed[0] <= 1, "HP downed must be boolean.");
        if (downed[0] == 1) Require(health[0] == 0, "HP downed state requires zero HP.");
        Require(revive[4] <= revive[3], "HP recoveries exceed revives.");
    }

    private static long UnsignedSection(string value, string startMarker, string? endMarker, string name)
    {
        string section = Section(value, startMarker, endMarker, name);
        return Unsigned(section, name);
    }

    private static long[] Tuple(string value, string? startMarker, string? endMarker, int count, string name,
        char finalPrefix = '\0', char delimiter = ',')
    {
        string section = startMarker is null ? value : Section(value, startMarker, endMarker, name);
        string[] parts = section.Split(startMarker is null ? '/' : delimiter);
        if (parts.Length != count) throw new FormatException($"P2D4 {name} tuple length is invalid.");
        var result = new long[count];
        for (int index = 0; index < count; index++)
        {
            string part = parts[index];
            if (finalPrefix != '\0' && index == count - 1)
            {
                if (part.Length < 2 || part[0] != finalPrefix)
                    throw new FormatException($"P2D4 {name} final marker is invalid.");
                part = part[1..];
            }
            result[index] = Unsigned(part, name);
        }
        return result;
    }

    private static string Section(string value, string startMarker, string? endMarker, string name)
    {
        int start = value.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) throw new FormatException($"P2D4 {name} start marker is missing.");
        start += startMarker.Length;
        int end = endMarker is null ? value.Length : value.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < start) throw new FormatException($"P2D4 {name} end marker is missing.");
        return value[start..end];
    }

    private static long Unsigned(string value, string name)
    {
        if (!CanonicalUnsigned(value)) throw new FormatException($"P2D4 {name} must be canonical ASCII decimal.");
        long result = 0;
        foreach (char character in value)
        {
            int digit = character - '0';
            if (result > (long.MaxValue - digit) / 10)
                throw new FormatException($"P2D4 {name} is outside Int64 range.");
            result = result * 10 + digit;
        }
        return result;
    }

    private static bool CanonicalUnsigned(string value) =>
        value.Length != 0 && (value.Length == 1 || value[0] != '0') && value.All(character => character is >= '0' and <= '9');

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new FormatException(message);
    }
}

public static class P2D4DiagnosticsEnvelope
{
    public const string Schema = "p2d4/1";
    public const int MaximumUtf8Bytes = 64 * 1024;

    public static string Serialize(P2D4Report report, string sessionId, int generation, long modFrame,
        long automationFrame)
    {
        if (sessionId.Length != 32 || sessionId.Any(character => !IsHexDigit(character)))
            throw new ArgumentException("Session ID must be 32 hexadecimal characters.", nameof(sessionId));
        if (generation < 0 || generation != report.DiagnosticGeneration)
            throw new ArgumentException("Envelope generation must match the report Q generation.", nameof(generation));
        if (modFrame < 0 || automationFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(modFrame), "Diagnostic frame values must be nonnegative.");

        string json = JsonSerializer.Serialize(new
        {
            schema = Schema,
            modVersion = report.Fields["VER"],
            sessionId,
            generation,
            modFrame,
            automationFrame,
            legacy = report.CanonicalLine,
            fields = report.Fields
        });
        if (Encoding.UTF8.GetByteCount(json) > MaximumUtf8Bytes)
            throw new InvalidOperationException("Structured diagnostics exceed the response bound.");
        return json;
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
