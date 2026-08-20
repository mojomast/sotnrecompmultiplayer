using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string path = Path.Combine(root, "routes", "no0-marble-gallery-candidate.json");
byte[] bytes = File.ReadAllBytes(path);
Require(bytes.Length is > 0 and <= 64 * 1024, "manifest size");
Require(Encoding.UTF8.GetString(bytes).All(value => value is '\r' or '\n' or >= ' ' and <= '~'), "ASCII only");
using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
{ AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
JsonElement rootElement = document.RootElement;
Properties(rootElement, "schema", "id", "version", "stage", "area", "displayName", "verdict",
    "qualification", "orderedRooms", "rooms", "segments", "excludedBranches", "globalLimitations");
Equal("coop-route/1", Text(rootElement, "schema"));
Equal("no0-marble-gallery-candidate", Text(rootElement, "id"));
Equal(1, Integer(rootElement, "version"));
Equal("NO0", Text(rootElement, "stage"));
Equal(0, Integer(rootElement, "area"));
Equal("candidate-untested", Text(rootElement, "verdict"));
Printable(Text(rootElement, "qualification"));

int[] expected = [9, 10, 5, 6, 5, 10, 9, 19, 11, 19, 9];
int[] ordered = rootElement.GetProperty("orderedRooms").EnumerateArray().Select(ValueInteger).ToArray();
Require(ordered.SequenceEqual(expected), "exact ordered route");
string fingerprintSource = $"{Text(rootElement, "schema")}|{Integer(rootElement, "version")}|{string.Join(',', ordered)}";
string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(fingerprintSource))).ToLowerInvariant();
Equal("34d38244074a0ea351c1374479cafa003bd1a21332e53554fbfba84e30591bac", fingerprint);

int[] expectedUnique = [5, 6, 9, 10, 11, 19];
var expectedBounds = new Dictionary<int, (int XMin, int XMax, int YMin, int YMax)>
{
    [5] = (26, 28, 26, 26), [6] = (22, 25, 26, 26), [9] = (32, 32, 26, 26),
    [10] = (29, 31, 26, 26), [11] = (36, 36, 26, 26), [19] = (33, 35, 26, 26),
};
var roomIds = new HashSet<int>();
foreach (JsonElement room in rootElement.GetProperty("rooms").EnumerateArray())
{
    Properties(room, "id", "bounds", "purpose", "limitations");
    int id = Integer(room, "id");
    Require(id is >= 0 and <= 255 && roomIds.Add(id), "safe unique room id");
    JsonElement bounds = room.GetProperty("bounds");
    Properties(bounds, "xMin", "xMax", "yMin", "yMax", "basis");
    (int xMin, int xMax, int yMin, int yMax) = expectedBounds[id];
    Equal(xMin, Integer(bounds, "xMin")); Equal(xMax, Integer(bounds, "xMax"));
    Equal(yMin, Integer(bounds, "yMin")); Equal(yMax, Integer(bounds, "yMax"));
    Equal("confirmed-map-cells-not-walkable-world-geometry", Text(bounds, "basis"));
    Printable(Text(room, "purpose"));
    Printable(Text(room, "limitations"));
}
Require(roomIds.Order().SequenceEqual(expectedUnique), "exact room inventory");

JsonElement.ArrayEnumerator segments = rootElement.GetProperty("segments").EnumerateArray();
int segmentIndex = 0;
foreach (JsonElement segment in segments)
{
    Properties(segment, "from", "to", "exit", "entry", "purpose", "limitations");
    Equal(expected[segmentIndex], Integer(segment, "from"));
    Equal(expected[segmentIndex + 1], Integer(segment, "to"));
    Require(Text(segment, "exit") is "west" or "east", "cardinal exit");
    Require(Text(segment, "entry") is "west" or "east", "cardinal entry");
    Printable(Text(segment, "purpose"));
    Printable(Text(segment, "limitations"));
    segmentIndex++;
}
Equal(expected.Length - 1, segmentIndex);

string[] exclusions = rootElement.GetProperty("excludedBranches").EnumerateArray().Select(ValueText).ToArray();
string[] requiredExclusions = ["west exit beyond room 6", "south room 21", "CEN elevator",
    "Maria branch", "bosses", "water", "shaped or moving terrain", "projectile-through-wall claims"];
Require(exclusions.SequenceEqual(requiredExclusions), "exact exclusions");
JsonElement limitationsElement = rootElement.GetProperty("globalLimitations");
Require(limitationsElement.ValueKind == JsonValueKind.Array &&
    limitationsElement.GetArrayLength() is >= 1 and <= 16, "bounded global limitations");
foreach (JsonElement limitation in limitationsElement.EnumerateArray()) Printable(ValueText(limitation));
string serialized = Encoding.UTF8.GetString(bytes);
Require(!serialized.Contains("/home/", StringComparison.OrdinalIgnoreCase) &&
    !serialized.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase) &&
    !serialized.Contains(".bin", StringComparison.OrdinalIgnoreCase) &&
    !serialized.Contains(".cue", StringComparison.OrdinalIgnoreCase) &&
    !serialized.Contains(".sav", StringComparison.OrdinalIgnoreCase), "no private paths or assets");

Console.WriteLine("CoopRouteManifest: 1 passed, 0 failed.");
return 0;

static void Properties(JsonElement value, params string[] names)
{
    Require(value.ValueKind == JsonValueKind.Object, "object expected");
    string[] actual = value.EnumerateObject().Select(property => property.Name).ToArray();
    Require(actual.Length == actual.Distinct(StringComparer.Ordinal).Count(), "duplicate property");
    Require(actual.Order().SequenceEqual(names.Order()), "closed property set");
}
static string Text(JsonElement value, string name) => ValueText(value.GetProperty(name));
static string ValueText(JsonElement value) => value.ValueKind == JsonValueKind.String
    ? value.GetString()! : throw new InvalidDataException("string expected");
static int Integer(JsonElement value, string name) => ValueInteger(value.GetProperty(name));
static int ValueInteger(JsonElement value) => value.TryGetInt32(out int result)
    ? result : throw new InvalidDataException("integer expected");
static void Printable(string value) => Require(value.Length is > 0 and <= 512 &&
    value.All(character => character is >= ' ' and <= '~'), "bounded printable text");
static void Equal<T>(T expected, T actual) => Require(EqualityComparer<T>.Default.Equals(expected, actual),
    $"expected {expected}, got {actual}");
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}
