using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Web.Components.Shared;

/// <summary>
/// Detects whether any <see cref="IMockable"/> service is registered in the DI container.
/// Injected into <c>MainLayout</c> to conditionally display the "USING MOCK DATA" banner.
/// </summary>
/// <remarks>
/// Strategy pattern (GoF): the detection logic is encapsulated here so the layout stays
/// free of DI-inspection concerns. Singleton lifetime matches the app lifetime — mock
/// registrations are fixed at startup and never change at runtime.
/// </remarks>
public sealed class MockDataDetector
{
    private readonly IReadOnlyList<IMockable> _mocks;

    public MockDataDetector(IEnumerable<IMockable> mocks)
    {
        _mocks = mocks.ToList();
    }

    /// <summary>Returns <c>true</c> when at least one mock service is active.</summary>
    public bool IsUsingMockData => _mocks.Count > 0;

    /// <summary>Descriptions of every active mock, for display in the banner.</summary>
    public IReadOnlyList<string> MockDescriptions =>
        _mocks.Select(m => m.MockDescription).ToList();
}
