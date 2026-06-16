using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azdo.Core.AzureDevOps;

/// <summary>
/// Deserializes timestamps as UTC <see cref="DateTime"/> values, preserving the
/// instant rather than shifting to the machine's local time. This mirrors Go's
/// time.Time JSON behavior, so formatting/duration helpers are timezone-stable.
/// A missing value (Go's zero time) becomes <see cref="DateTime"/>.<see cref="DateTime.MinValue"/>.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;
        var s = reader.GetString();
        if (string.IsNullOrEmpty(s))
            return default;
        var dto = DateTimeOffset.Parse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return dto.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class UtcNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        var s = reader.GetString();
        if (string.IsNullOrEmpty(s))
            return null;
        var dto = DateTimeOffset.Parse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return dto.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
    }
}
