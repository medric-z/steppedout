// Writes the settings text the app shows, in English and Arabic, from the approved lines.
//
//   dotnet run tools/text/gen.cs            write src/Text/V1/*.g.cs
//   dotnet run tools/text/gen.cs -- --check  only say whether they are up to date (exit 1 if not)
//
// Inputs, all in tools/text:
//   approved.json   the approved lines, copied as they are; never edited here
//   names.json      one entry per approved id: its C# name and its kind: "text", "template" (with
//                   "template": {"en", "ar"}, the example inside the line that becomes {0}), or
//                   "formatted" (produced by code, not a line); "excluded": true for an example that
//                   must not ship, and "sameInArabic": true for a line that reads the same in both
//   new/*.json      lines added during the build, one file per package, in the approved shape plus
//                   "name" and "status": "new", or "owner-approved" for a line the app's owner already
//                   approved (a change asked for); "kind" is "text" or "template"; "excluded": true, with
//                   a "why", for a line no code uses any more, as in names.json
//
// Each generated file carries a hash of every input (this file included) and a hash of its own
// text, so a test can tell when the files are stale or were edited by hand.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var check = args.Contains("--check");
var root = FindRoot();
var textDir = Path.Combine(root, "tools", "text");
var outDir = Path.Combine(root, "src", "Text", "V1");

var errors = new List<string>();
var lines = new List<Line>();
var names = new HashSet<string>(StringComparer.Ordinal);
var reserved = new HashSet<string>(StringComparer.Ordinal) { "Words", "InputsHash", "Equals", "GetHashCode", "ToString", "GetType" };
string[] banned = { "OBS", "Zoom", "WhatsApp", "Google Chrome" };

using var approvedDoc = JsonDocument.Parse(Read(Path.Combine(textDir, "approved.json")));
using var namesDoc = JsonDocument.Parse(Read(Path.Combine(textDir, "names.json")));

var entries = new Dictionary<int, JsonElement>();
foreach (var entry in namesDoc.RootElement.EnumerateArray())
{
    int id = entry.GetProperty("id").GetInt32();
    if (!entries.TryAdd(id, entry)) errors.Add($"names.json: id {id} is listed twice");
}

var approvedIds = new HashSet<int>();
foreach (var item in approvedDoc.RootElement.EnumerateArray())
{
    int id = item.GetProperty("id").GetInt32();
    approvedIds.Add(id);
    string en = item.GetProperty("en").GetString() ?? "";
    string ar = item.GetProperty("ar").GetString() ?? "";
    string where = item.GetProperty("where").GetString() ?? "";
    if (!entries.TryGetValue(id, out var entry))
    {
        errors.Add($"approved line {id} \"{en}\" has no entry in names.json");
        continue;
    }
    string kind = entry.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
    bool template = entry.TryGetProperty("template", out var example);
    if (kind is not ("text" or "template" or "formatted")) errors.Add($"names.json: id {id} has kind \"{kind}\"; use text, template or formatted");
    if (template != (kind == "template")) errors.Add($"names.json: id {id} needs \"template\" exactly when its kind is template");
    if (Flag(entry, "excluded") || kind == "formatted") continue;
    if (Flag(entry, "sameInArabic"))
    {
        if (ar != "" && ar != en) errors.Add($"approved line {id}: sameInArabic, but its Arabic differs");
        ar = en;
    }
    if (template)
    {
        en = Placeholder(id, en, example.GetProperty("en").GetString() ?? "");
        ar = Placeholder(id, ar, example.GetProperty("ar").GetString() ?? "");
    }
    Add(new Line(id.ToString(), Name(entry), en, ar, where, template, "approved.json"));
}
foreach (int id in entries.Keys.Where(id => !approvedIds.Contains(id)))
    errors.Add($"names.json: id {id} is not an approved line");

var newFiles = Directory.Exists(Path.Combine(textDir, "new"))
    ? Directory.GetFiles(Path.Combine(textDir, "new"), "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList()
    : new List<string>();
var newIds = new HashSet<string>(StringComparer.Ordinal);
foreach (var file in newFiles)
{
    string fileName = "new/" + Path.GetFileName(file);
    using var doc = JsonDocument.Parse(Read(file));
    foreach (var item in doc.RootElement.EnumerateArray())
    {
        string id = item.GetProperty("id").GetString() ?? "";
        if (!newIds.Add(id)) errors.Add($"{fileName}: id {id} is used twice");
        if (Flag(item, "excluded")) continue;
        string kind = item.GetProperty("kind").GetString() ?? "";
        if (kind is not ("text" or "template")) errors.Add($"{fileName}: {id} has kind \"{kind}\"; use text or template");
        if (item.TryGetProperty("status", out var status) && status.GetString() is not ("new" or "owner-approved"))
            errors.Add($"{fileName}: {id} has the status \"{status.GetString()}\"; use new or owner-approved");
        Add(new Line(id, Name(item), item.GetProperty("en").GetString() ?? "", item.GetProperty("ar").GetString() ?? "",
            item.GetProperty("where").GetString() ?? "", kind == "template", fileName));
    }
}

if (errors.Count > 0)
{
    foreach (var error in errors) Console.Error.WriteLine(error);
    return 1;
}

string hash = InputsHash();
var outputs = new Dictionary<string, string>
{
    ["V1Text.g.cs"] = Finish(TextClass(hash)),
    ["EnglishV1.g.cs"] = Finish(Values(hash, "EnglishV1", "English", l => l.En)),
    ["ArabicV1.g.cs"] = Finish(Values(hash, "ArabicV1", "Arabic", l => l.Ar)),
};

bool stale = false;
foreach (var (file, text) in outputs)
{
    string path = Path.Combine(outDir, file);
    string old = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
    if (old == text) continue;
    stale = true;
    if (check) Console.Error.WriteLine($"src/Text/V1/{file} is out of date; run: dotnet run tools/text/gen.cs");
    else
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        Console.WriteLine($"wrote src/Text/V1/{file}");
    }
}
Console.WriteLine($"{lines.Count} lines ({lines.Count(l => l.Template)} templates), inputs {hash[..12]}{(stale ? "" : ", already up to date")}");
return check && stale ? 1 : 0;

void Add(Line line)
{
    string at = $"{line.Source} {line.Id}";
    if (!Regex.IsMatch(line.Name, "^[A-Z][A-Za-z0-9]*$")) errors.Add($"{at}: \"{line.Name}\" is not a C# name starting with a capital");
    else if (reserved.Contains(line.Name)) errors.Add($"{at}: \"{line.Name}\" is reserved");
    else if (!names.Add(line.Name)) errors.Add($"{at}: the name {line.Name} is used twice");
    if (line.En.Trim() == "" || line.Ar.Trim() == "") errors.Add($"{at}: a blank line");
    var enHoles = Holes(line.En);
    var arHoles = Holes(line.Ar);
    if (enHoles is null || arHoles is null) errors.Add($"{at}: a brace that is not a placeholder like {{0}}");
    else
    {
        if (!enHoles.SetEquals(arHoles)) errors.Add($"{at}: English has {string.Join(",", enHoles.Order())} and Arabic has {string.Join(",", arHoles.Order())}");
        if (line.Template && enHoles.Count == 0) errors.Add($"{at}: a template with no {{0}}");
        if (!line.Template && enHoles.Count > 0) errors.Add($"{at}: placeholders in a line that is not a template");
        if (enHoles.Count > 0 && enHoles.Max() != enHoles.Count - 1) errors.Add($"{at}: placeholders must run {{0}}, {{1}}, … without gaps");
    }
    var same = lines.FirstOrDefault(l => l.En == line.En && l.Ar == line.Ar);
    if (same is not null) errors.Add($"{at}: the same text as {same.Name} ({same.Source} {same.Id}); use that name");
    foreach (string name in banned)
    {
        if (Regex.IsMatch(line.En + "\n" + line.Ar, $@"\b{Regex.Escape(name)}\b")) errors.Add($"{at}: names the example app {name}");
    }
    lines.Add(line);
}

string Placeholder(int id, string text, string example)
{
    int at = example == "" ? -1 : text.IndexOf(example, StringComparison.Ordinal);
    if (at < 0 || text.IndexOf(example, at + 1, StringComparison.Ordinal) >= 0)
    {
        errors.Add($"approved line {id}: the example \"{example}\" must appear exactly once in \"{text}\"");
        return text;
    }
    return text[..at] + "{0}" + text[(at + example.Length)..];
}

string Name(JsonElement entry) => entry.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";

static bool Flag(JsonElement entry, string flag) => entry.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True;

// The numbers of the {n} placeholders, or null when a brace is not part of one.
static HashSet<int>? Holes(string text)
{
    var found = new HashSet<int>();
    string rest = Regex.Replace(text, @"\{(\d+)\}", m =>
    {
        found.Add(int.Parse(m.Groups[1].Value));
        return "";
    });
    return rest.Contains('{') || rest.Contains('}') ? null : found;
}

string TextClass(string hash)
{
    var b = new StringBuilder();
    Header(b, hash);
    b.Append("// The settings text of version 1, one property per line. English.cs and Arabic.cs each set\n");
    b.Append("// Strings.V1 to one of these; the comment over each is its English and where it shows.\n");
    b.Append("sealed class V1Text\n{\n");
    b.Append($"    public const string InputsHash = \"{hash}\";\n\n");
    b.Append("    // The counting and joining rules of the language.\n");
    b.Append("    public required V1Words Words { get; init; }\n");
    foreach (var line in lines)
    {
        b.Append('\n');
        b.Append($"    // {line.Id} · {OneLine(line.Where)} · {OneLine(line.En)}\n");
        b.Append($"    public required string {line.Name} {{ get; init; }}\n");
    }
    b.Append("}\n");
    return b.ToString();
}

string Values(string hash, string className, string words, Func<Line, string> pick)
{
    var b = new StringBuilder();
    Header(b, hash);
    b.Append($"static class {className}\n{{\n");
    b.Append("    public static readonly V1Text Text = new()\n    {\n");
    b.Append($"        Words = V1Words.{words},\n");
    foreach (var line in lines) b.Append($"        {line.Name} = {Literal(pick(line))},\n");
    b.Append("    };\n}\n");
    return b.ToString();
}

static void Header(StringBuilder b, string hash)
{
    b.Append("// <auto-generated>\n");
    b.Append("// Written by tools/text/gen.cs from tools/text/approved.json, names.json and new/*.json.\n");
    b.Append("// Don't edit it; change the inputs and run: dotnet run tools/text/gen.cs\n");
    b.Append("// </auto-generated>\n");
    b.Append($"// inputs: {hash}\n");
    b.Append("#nullable enable\n\n");
    b.Append("namespace SteppedOut;\n\n");
}

// The last line holds the hash of everything above it.
static string Finish(string text) => text + "// output: " + Sha(text) + "\n";

static string OneLine(string text) => text.Replace("\r", " ").Replace("\n", " ");

static string Literal(string text)
{
    var b = new StringBuilder("\"");
    foreach (char c in text)
    {
        b.Append(c switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ when char.IsControl(c) || c is '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩') => $"\\u{(int)c:X4}",
            _ => c.ToString(),
        });
    }
    return b.Append('"').ToString();
}

// Every input in a fixed order: its path, then its text with Windows line ends made plain, so a
// checkout with either line end gives the same hash. The test in TextPipelineTests does the same.
string InputsHash()
{
    var files = new List<string> { "tools/text/gen.cs", "tools/text/approved.json", "tools/text/names.json" };
    files.AddRange(newFiles.Select(f => "tools/text/new/" + Path.GetFileName(f)));
    var b = new StringBuilder();
    foreach (string file in files)
    {
        b.Append(file).Append('\n');
        b.Append(Read(Path.Combine(root, file))).Append('\n');
    }
    return Sha(b.ToString());
}

static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "tools", "text", "approved.json"))) return dir.FullName;
    }
    throw new InvalidOperationException("Run this from inside the SteppedOut repository.");
}

record Line(string Id, string Name, string En, string Ar, string Where, bool Template, string Source);
