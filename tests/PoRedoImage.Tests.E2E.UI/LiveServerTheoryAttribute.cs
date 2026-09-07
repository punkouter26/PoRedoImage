using Xunit;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// Theory counterpart of <see cref="LiveServerFactAttribute"/>: runs every <c>[InlineData]</c>
/// row only when a live PoRedoImage instance is reachable at <c>E2E_BASE_URL</c> (default
/// <c>http://localhost:4000</c>), self-skipping the whole theory otherwise. The probe is
/// delegated to <see cref="LiveServerFactAttribute"/> so the reachability check stays in
/// exactly one place.
/// </summary>
public sealed class LiveServerTheoryAttribute : TheoryAttribute
{
    public override string? Skip => new LiveServerFactAttribute().Skip;
}
