using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteppedOut;

// Readers for the settings added in 1.0. A value of the wrong kind in one of them, such as "yes"
// for a switch, a word where a number belongs or a rule that names no app, is skipped: the setting
// keeps its default, or a list loses only that item, the log says what was skipped, and the rest of
// the file loads. The settings from before 1.0 keep their strict readers, so a mistake in one of
// them still sets the whole file aside, as it always has.
static class Lenient
{
    // What the reads on this thread skipped, while a Collector is open.
    [ThreadStatic] static Collector? open;

    public static Collector Collect() => new();

    public sealed class Collector : IDisposable
    {
        readonly Collector? outer;
        readonly List<string> heard = new();

        public Collector()
        {
            outer = open;
            open = this;
        }

        // One entry per value skipped: the setting's name, then what was wrong with it.
        public IReadOnlyList<string> Skipped => heard;

        public void Dispose() => open = outer;

        internal void Note(string key, string why) => heard.Add($"{key} ({why})");
    }

    // The reader for a setting added in 1.0, by its type. fallback is the setting's default and
    // kinds the effects an effect setting may hold. A type without a reader throws, so a new setting
    // cannot slip in with a strict one.
    public static JsonConverter For(Type type, string key, object? fallback, IReadOnlyList<EffectKind>? kinds)
    {
        if (type == typeof(bool)) return new Switch(key, (bool)fallback!);
        if (type == typeof(int)) return new Number(key, (int)fallback!);
        if (type == typeof(string)) return new Text(key, (string)fallback!);
        if (type == typeof(List<string>)) return new TextList(key);
        if (type == typeof(List<AppRule>)) return new RuleList(key);
        if (type == typeof(Effect)) return new EffectReader(key, (Effect)fallback!, kinds ?? throw new InvalidOperationException($"{key} does not say which effects it holds"));
        if (type.IsEnum) return (JsonConverter)Activator.CreateInstance(typeof(Choice<>).MakeGenericType(type), key, fallback)!;
        if (Nullable.GetUnderlyingType(type) is { IsEnum: true } inner) return (JsonConverter)Activator.CreateInstance(typeof(OptionalChoice<>).MakeGenericType(inner), key)!;
        throw new InvalidOperationException($"no lenient reader for {key} ({type.Name})");
    }

    // An enum value by its name in any case: "onScreen", "OnScreen" and "onscreen" are one value.
    // Numbers are not names, so "theme": "2" is skipped like any other unknown word.
    public static bool TryName<T>(string? text, out T value) where T : struct, Enum
    {
        string name = (text ?? "").Trim();
        foreach (var each in Enum.GetValues<T>())
        {
            if (string.Equals(each.ToString(), name, StringComparison.OrdinalIgnoreCase)
                || FormerlyAttribute.Of(each).Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                value = each;
                return true;
            }
        }
        value = default;
        return false;
    }

    // The file's spelling of an enum value: camelCase, as the strict reader writes the older ones.
    public static string Name<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    static void Note(string key, string why) => open?.Note(key, why);

    // Skips what the reader is on, says so, and gives the default back.
    static T Skip<T>(ref Utf8JsonReader reader, string key, string expected, T fallback)
    {
        Note(key, $"{Found(ref reader)} is not {expected}");
        reader.Skip();
        return fallback;
    }

    // What a value was, for the log.
    static string Found(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => Quote(reader.GetString()),
        JsonTokenType.Number => reader.TryGetDouble(out double number) ? number.ToString(CultureInfo.InvariantCulture) : "a number",
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        JsonTokenType.Null => "null",
        JsonTokenType.StartArray => "a list",
        JsonTokenType.StartObject => "an object",
        _ => "the value",
    };

    static string Quote(string? text)
    {
        string plain = new((text ?? "").Where(c => !char.IsControl(c)).ToArray());
        return "\"" + (plain.Length > 40 ? plain[..40] + "..." : plain) + "\"";
    }

    static string Spell(IEnumerable<EffectKind> kinds) => Spell(kinds.Select(k => k switch
    {
        EffectKind.Extra => "later N",
        EffectKind.Sooner => "sooner P",
        EffectKind.Cap => "always N",
        _ => Name(k),
    }));

    static string Spell<T>() where T : struct, Enum => Spell(Enum.GetValues<T>().Select(v => Name(v)));

    static string Spell(IEnumerable<string> names)
    {
        var words = names.ToList();
        return words.Count == 1 ? words[0] : string.Join(", ", words.Take(words.Count - 1)) + " or " + words[^1];
    }

    // Every reader takes a null as well, so a null in the file gives the default instead of leaving
    // a setting null or setting the whole file aside.
    abstract class Reader<T> : JsonConverter<T>
    {
        public override bool HandleNull => true;
    }

    sealed class Switch(string key, bool fallback) : Reader<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType is JsonTokenType.True or JsonTokenType.False ? reader.GetBoolean() : Skip(ref reader, key, "true or false", fallback);

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
    }

    // Whole numbers take what a hand edit gives them, as the older ones do: 45.0 or 44.6 reads as
    // 45, and a number too big for an int stops at its edge for Clamp to bring into range.
    sealed class Number(string key, int fallback) : Reader<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out double value)
                ? (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue)
                : Skip(ref reader, key, "a number", fallback);

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    sealed class Text(string key, string fallback) : Reader<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String ? reader.GetString()! : Skip(ref reader, key, "text", fallback);

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) => writer.WriteStringValue(value ?? "");
    }

    // A list of text. Something else in it is left out on its own; a null is a blank entry, which
    // Clamp drops anyway.
    sealed class TextList(string key) : Reader<List<string>>
    {
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) return Skip(ref reader, key, "a list", new List<string>());
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String) list.Add(reader.GetString()!);
                else if (reader.TokenType != JsonTokenType.Null)
                {
                    Note(key, $"left out {Found(ref reader)}, which is not text");
                    reader.Skip();
                }
            }
            return list;
        }

        public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (string item in value ?? new()) writer.WriteStringValue(item);
            writer.WriteEndArray();
        }
    }

    sealed class Choice<T>(string key, T fallback) : Reader<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && TryName(reader.GetString(), out T value) ? value : Skip(ref reader, key, Spell<T>(), fallback);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(Name(value));
    }

    // A choice that may be left unmade, which a null in the file also says.
    sealed class OptionalChoice<T>(string key) : Reader<T?> where T : struct, Enum
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return reader.TokenType == JsonTokenType.String && TryName(reader.GetString(), out T value) ? value : Skip<T?>(ref reader, key, Spell<T>(), null);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value is T chosen) writer.WriteStringValue(Name(chosen));
            else writer.WriteNullValue();
        }
    }

    sealed class EffectReader(string key, Effect fallback, IReadOnlyList<EffectKind> kinds) : Reader<Effect>
    {
        public override Effect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Effect.TryParse(reader.GetString(), out var effect) && kinds.Contains(effect.Kind)
                ? effect
                : Skip(ref reader, key, Spell(kinds), fallback);

        public override void Write(Utf8JsonWriter writer, Effect value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    // The app rules. A rule with something wrong in it is left out on its own, and the log says
    // which and why; names the rules do not use are passed over.
    sealed class RuleList(string key) : Reader<List<AppRule>>
    {
        public override List<AppRule> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) return Skip(ref reader, key, "a list", new List<AppRule>());
            var rules = new List<AppRule>();
            int number = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                number++;
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    Note(key, $"left out rule {number}: {Found(ref reader)} is not a rule");
                    reader.Skip();
                }
                else if (ReadRule(ref reader, out string? why) is AppRule rule)
                {
                    rules.Add(rule);
                }
                else
                {
                    Note(key, $"left out rule {number}: {why}");
                }
            }
            return rules;
        }

        // One rule, or null with why it was left out: the first mistake in it.
        static AppRule? ReadRule(ref Utf8JsonReader reader, out string? why)
        {
            string? app = null, path = null, aumid = null;
            Effect? effect = null;
            var when = AppWhen.Running;
            why = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string name = reader.GetString() ?? "";
                reader.Read();
                if (Is(name, "app")) app = Text(ref reader, "app", app, ref why);
                else if (Is(name, "path")) path = Text(ref reader, "path", path, ref why);
                else if (Is(name, "aumid")) aumid = Text(ref reader, "aumid", aumid, ref why);
                else if (Is(name, "effect"))
                {
                    if (reader.TokenType == JsonTokenType.String && Effect.TryParse(reader.GetString(), out var read) && AppRule.Kinds.Contains(read.Kind)) effect = read;
                    else why ??= $"\"effect\" is {Found(ref reader)}, not {Spell(AppRule.Kinds)}";
                }
                else if (Is(name, "when"))
                {
                    if (reader.TokenType == JsonTokenType.String && TryName(reader.GetString(), out AppWhen read)) when = read;
                    else why ??= $"\"when\" is {Found(ref reader)}, not {Spell<AppWhen>()}";
                }
                reader.Skip();
            }
            if (why is null && string.IsNullOrWhiteSpace(app)) why = "it names no app";
            if (why is null && effect is null) why = "it has no effect";
            return why is null ? new AppRule(app!, effect!.Value, when, Blank(path), Blank(aumid)) : null;
        }

        // Text, or null for a null; anything else is the rule's mistake.
        static string? Text(ref Utf8JsonReader reader, string name, string? before, ref string? why)
        {
            if (reader.TokenType == JsonTokenType.String) return reader.GetString();
            if (reader.TokenType == JsonTokenType.Null) return null;
            why ??= $"\"{name}\" is {Found(ref reader)}, not text";
            return before;
        }

        static bool Is(string name, string wanted) => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase);

        static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

        public override void Write(Utf8JsonWriter writer, List<AppRule>? value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var rule in value ?? new())
            {
                writer.WriteStartObject();
                writer.WriteString("app", rule.App);
                if (Blank(rule.Path) is string path) writer.WriteString("path", path);
                if (Blank(rule.Aumid) is string aumid) writer.WriteString("aumid", aumid);
                writer.WriteString("effect", rule.Effect.ToString());
                writer.WriteString("when", Name(rule.When));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
    }
}

// The names a choice had in an earlier 1.0 build, which still read as it: "click" and "chime" for the
// SteppedOut sound. Only reading takes them; a save writes the choice's own name.
[AttributeUsage(AttributeTargets.Field)]
sealed class FormerlyAttribute(params string[] names) : Attribute
{
    public IReadOnlyList<string> Names { get; } = names;

    public static IReadOnlyList<string> Of<T>(T value) where T : struct, Enum =>
        typeof(T).GetField(value.ToString())?.GetCustomAttributes(typeof(FormerlyAttribute), inherit: false)
            .OfType<FormerlyAttribute>().SelectMany(a => a.Names).ToList() ?? [];
}
