using System.Text;
using System.Text.Json;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
byte[] bytes = File.ReadAllBytes(Path.Combine(root, "release", "m5-release-matrix.json"));
Require(bytes.Length is > 0 and <= 32 * 1024, "matrix size");
Require(Encoding.UTF8.GetString(bytes).All(value => value is '\r' or '\n' or >= ' ' and <= '~'), "ASCII only");
using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
{ AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
JsonElement matrix = document.RootElement;
Properties(matrix, "schema", "id", "version", "status", "claims", "objectives", "workflow", "privateEvidenceFields", "prohibitions");
Equal("m5-release-matrix/1", Text(matrix, "schema"));
Equal("coop-m5-live-release", Text(matrix, "id"));
Equal(1, Integer(matrix, "version"));
Equal("live-blocked", Text(matrix, "status"));
Require(Text(matrix, "claims").Contains("no completion evidence", StringComparison.OrdinalIgnoreCase), "honest claims");

JsonElement objectives = matrix.GetProperty("objectives");
Properties(objectives, "coldLaunches", "routeRuns", "consecutiveTransitions", "contactHits", "projectileHits",
    "associatedDrops", "revives", "playableSoakMinutes", "soakSamples");
Equal(3, Integer(objectives, "coldLaunches")); Equal(3, Integer(objectives, "routeRuns"));
Equal(25, Integer(objectives, "consecutiveTransitions")); Equal(1, Integer(objectives, "contactHits"));
Equal(1, Integer(objectives, "projectileHits")); Equal(1, Integer(objectives, "associatedDrops"));
Equal(1, Integer(objectives, "revives")); Equal(60, Integer(objectives, "playableSoakMinutes"));
Equal(13, Integer(objectives, "soakSamples"));

string[] ids = ["cold-launch", "route", "combat", "save-enabled", "restart-disabled", "playable-soak"];
JsonElement[] workflow = matrix.GetProperty("workflow").EnumerateArray().ToArray();
Equal(ids.Length, workflow.Length);
for (int index = 0; index < workflow.Length; index++)
{
    Properties(workflow[index], "id", "repeat", "approval", "observe");
    Equal(ids[index], Text(workflow[index], "id"));
    Require(Integer(workflow[index], "repeat") is >= 1 and <= 3, "bounded repeat");
    Printable(Text(workflow[index], "approval")); Printable(Text(workflow[index], "observe"));
}
string[] evidence = matrix.GetProperty("privateEvidenceFields").EnumerateArray().Select(ValueText).ToArray();
Require(evidence.Length is >= 8 and <= 32 && evidence.Distinct(StringComparer.Ordinal).Count() == evidence.Length, "bounded evidence fields");
string[] prohibitions = matrix.GetProperty("prohibitions").EnumerateArray().Select(ValueText).ToArray();
Require(prohibitions.Length == 4 && prohibitions.All(value => value.StartsWith("No ", StringComparison.Ordinal) || value.StartsWith("Do not ", StringComparison.Ordinal)), "explicit prohibitions");
string text = Encoding.UTF8.GetString(bytes);
Require(!text.Contains("/home/", StringComparison.OrdinalIgnoreCase) && !text.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase) &&
    !text.Contains(".cue", StringComparison.OrdinalIgnoreCase) && !text.Contains(".sav", StringComparison.OrdinalIgnoreCase) &&
    !text.Contains("token=", StringComparison.OrdinalIgnoreCase), "no private data");
Console.WriteLine("CoopReleaseMatrix: 1 passed, 0 failed.");
return 0;

static void Properties(JsonElement value, params string[] names)
{
    Require(value.ValueKind == JsonValueKind.Object, "object expected");
    string[] actual = value.EnumerateObject().Select(property => property.Name).ToArray();
    Require(actual.Length == actual.Distinct(StringComparer.Ordinal).Count() && actual.Order().SequenceEqual(names.Order()), "closed property set");
}
static string Text(JsonElement value, string name) => ValueText(value.GetProperty(name));
static string ValueText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException("string expected");
static int Integer(JsonElement value, string name) => value.GetProperty(name).TryGetInt32(out int result) ? result : throw new InvalidDataException("integer expected");
static void Printable(string value) => Require(value.Length is > 0 and <= 512 && value.All(character => character is >= ' ' and <= '~'), "printable text");
static void Equal<T>(T expected, T actual) => Require(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
