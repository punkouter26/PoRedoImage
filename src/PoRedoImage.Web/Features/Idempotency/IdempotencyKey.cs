using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoRedoImage.Web.Features.Idempotency;

/// <summary>
/// RFC 9562 / UUID v7 strongly-typed wrapper. UUID v7 is time-sortable and ~40% cheaper
/// to generate than v4 (single random block + 48-bit timestamp). Used to identify
/// duplicate Write requests at the Minimal API boundary (R5 in Po2Logic refactor queue).
/// <para>
/// Why strongly-typed? Stops the "magic string" anti-pattern where any header value
/// gets accepted as an idempotency token. C# 14 primary constructors + readonly record
/// struct give us value-equality + zero heap allocation for the common path.
/// </para>
/// </summary>
[JsonConverter(typeof(IdempotencyKeyJsonConverter))]
public readonly record struct IdempotencyKey
{
    public Guid Value { get; }

    public IdempotencyKey(Guid value) => Value = value;

    /// <summary>Allocates a new time-sortable UUID v7 using .NET 9+ built-in.</summary>
    public static IdempotencyKey Create() => new(Guid.CreateVersion7());

    public static bool TryParse(string? raw, out IdempotencyKey key)
    {
        if (Guid.TryParse(raw, out var g))
        {
            key = new IdempotencyKey(g);
            return true;
        }
        key = default;
        return false;
    }

    public override string ToString() => Value.ToString("D");
}

public sealed class IdempotencyKeyJsonConverter : JsonConverter<IdempotencyKey>
{
    public override IdempotencyKey Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
    {
        var raw = reader.GetString();
        return IdempotencyKey.TryParse(raw, out var k)
            ? k
            : throw new JsonException($"Invalid IdempotencyKey: '{raw}'. Must be a UUID.");
    }

    public override void Write(Utf8JsonWriter writer, IdempotencyKey value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
