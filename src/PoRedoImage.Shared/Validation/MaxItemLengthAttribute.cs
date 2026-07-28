using System.ComponentModel.DataAnnotations;

namespace PoRedoImage.Shared.Validation;

/// <summary>
/// Validates that every string in an <see cref="IEnumerable{T}"/> of strings is no longer than
/// <see cref="MaxItemLength"/> characters. Complements <see cref="MaxLengthAttribute"/>, which caps
/// only the collection's count, not the length of each entry — a client-supplied list whose entries
/// flow verbatim into a metered model's prompt needs both.
/// </summary>
/// <remarks>
/// Plain <see cref="ValidationAttribute"/> override, no reflection — trim-safe for use on DTOs in
/// <c>PoRedoImage.Shared</c>.
/// </remarks>
public sealed class MaxItemLengthAttribute(int maxItemLength) : ValidationAttribute
{
    public int MaxItemLength { get; } = maxItemLength;

    public override bool IsValid(object? value)
    {
        if (value is not IEnumerable<string?> items) return true;
        return items.All(item => item is null || item.Length <= MaxItemLength);
    }

    public override string FormatErrorMessage(string name) =>
        $"Each entry in {name} must be no more than {MaxItemLength} characters.";
}
