using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteppedOut;

// The whole-number settings take what a hand edit gives them: 15.0 or 14.6 reads as 15, and a
// number too big for an int stops at its edge. Clamp then brings either into range, so a typo like
// that does not throw away the whole file.
sealed class WholeNumber : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out double value))
        {
            throw new JsonException("expected a number");
        }
        return (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
