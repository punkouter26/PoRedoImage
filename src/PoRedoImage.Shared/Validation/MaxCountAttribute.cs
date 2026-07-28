using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace PoRedoImage.Shared.Validation;

/// <summary>
/// Validates that an <see cref="IEnumerable"/>'s element count does not exceed
/// <see cref="MaxCount"/>.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="MaxLengthAttribute"/>: that attribute's constructor carries
/// <c>[RequiresUnreferencedCode]</c> (its fallback path reflects for a <c>Count</c> property on
/// types that are not <see cref="ICollection"/>), which trips the trim analyzer on
/// <c>PoRedoImage.Shared</c>. This counts via <see cref="ICollection.Count"/> directly with no
/// reflection, so it stays trim-safe.
/// </remarks>
public sealed class MaxCountAttribute(int maxCount) : ValidationAttribute
{
    public int MaxCount { get; } = maxCount;

    public override bool IsValid(object? value) => value switch
    {
        null => true,
        ICollection collection => collection.Count <= MaxCount,
        IEnumerable enumerable => enumerable.Cast<object?>().Count() <= MaxCount,
        _ => true,
    };

    public override string FormatErrorMessage(string name) =>
        $"{name} must contain no more than {MaxCount} entries.";
}
