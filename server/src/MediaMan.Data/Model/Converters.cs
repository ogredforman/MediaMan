using System.Text.Json;
using System.Text.Json.Serialization;
using MediaMan.Core;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediaMan.Data.Model;

/// <summary>Timestamps are stored as UTC unix milliseconds so SQLite can sort and compare them (ADR 0003).</summary>
internal sealed class UtcUnixMillisecondsConverter()
    : ValueConverter<DateTimeOffset, long>(
        v => v.ToUnixTimeMilliseconds(),
        v => DateTimeOffset.FromUnixTimeMilliseconds(v));

/// <summary>Encrypts a column with the per-install key (SPEC §9, ADR 0003).</summary>
internal sealed class SecretConverter(ISecretProtector protector)
    : ValueConverter<string, string>(v => protector.Protect(v), v => protector.Unprotect(v));

internal static class DataJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        Converters = { new JsonStringEnumConverter(), new ExternalIdJsonConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"Stored {typeof(T).Name} JSON was null.");

    /// <summary>Stores the property as a JSON text column, compared and snapshotted by its JSON form.</summary>
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> builder)
    {
        var converter = new ValueConverter<T, string>(v => Serialize(v), s => Deserialize<T>(s));
        var comparer = new ValueComparer<T>((a, b) => JsonEquals(a, b), v => JsonHash(v), v => JsonClone(v));
        builder.HasConversion(converter, comparer);
        return builder;
    }

    private static bool JsonEquals<T>(T? a, T? b) =>
        a is null || b is null ? a is null && b is null : Serialize(a) == Serialize(b);

    private static int JsonHash<T>(T? value) =>
        value is null ? 0 : Serialize(value).GetHashCode(StringComparison.Ordinal);

    private static T JsonClone<T>(T value) => value is null ? value : Deserialize<T>(Serialize(value));

    private sealed class ExternalIdJsonConverter : JsonConverter<ExternalId>
    {
        public override ExternalId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ExternalId.Parse(reader.GetString() ?? throw new JsonException("ExternalId was null."));

        public override void Write(Utf8JsonWriter writer, ExternalId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
