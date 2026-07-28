namespace PoRedoImage.Domain.Entities;

/// <summary>
/// Strongly-typed identifier for a <see cref="UserImage"/> (§1 "Eradicate primitive obsession").
/// </summary>
/// <remarks>
/// The underlying value is a 32-character hex GUID ("N" format) — it doubles as the Table Storage
/// RowKey and the blob name, so it must stay free of the characters those stores reject. Modelling
/// it as a <c>readonly record struct</c> costs no allocation while making it impossible to pass a
/// <c>UserId</c> where an image id is expected: the two were previously both bare
/// <see cref="string"/> and sat next to each other in every repository signature.
/// </remarks>
public readonly record struct UserImageId
{
    private readonly string? _value;

    private UserImageId(string value) => _value = value;

    /// <summary>The raw identifier. Empty for a defaulted instance.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Creates a fresh, random identifier.</summary>
    public static UserImageId New() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Rehydrates an identifier read back from storage or a route parameter.
    /// </summary>
    /// <exception cref="ArgumentException">The value is null, blank, or not a 32-char hex GUID.</exception>
    public static UserImageId Parse(string value)
    {
        if (!TryParse(value, out var id))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid UserImageId — expected a 32-character hex GUID.", nameof(value));
        }

        return id;
    }

    /// <summary>Non-throwing counterpart to <see cref="Parse"/>, for untrusted input such as route values.</summary>
    public static bool TryParse(string? value, out UserImageId id)
    {
        // Guid.TryParseExact with "N" rejects braces, hyphens, and any non-hex character, which is
        // precisely the set that would break a blob name or Table Storage RowKey.
        if (!string.IsNullOrWhiteSpace(value) && Guid.TryParseExact(value, "N", out _))
        {
            id = new UserImageId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value;
}
