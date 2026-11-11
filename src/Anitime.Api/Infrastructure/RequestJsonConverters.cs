using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anitime.Api.Infrastructure;

/// <summary>PostgreSQL text and jsonb cannot store U+0000, so a stray pasted character must not fail the request.</summary>
public sealed class NulStrippingStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString()?.Replace("\0", string.Empty);

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>Npgsql only accepts UTC for timestamptz. Offsets are converted; a value without an offset is read as UTC.</summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
