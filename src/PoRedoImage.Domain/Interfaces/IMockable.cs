namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Marker interface for services that operate against in-memory or pre-canned mock data
/// rather than the production AI / storage backends. The Client layout resolves every
/// registered <see cref="IMockable"/> from DI and renders a "USING MOCK DATA" banner when
/// at least one is present, so QA / dev runs are visually distinguishable from production.
/// </summary>
/// <remarks>
/// Apply this interface to a service registration only in Development or Test environments.
/// Production services should never implement <see cref="IMockable"/> — wiring a production
/// implementation of an <see cref="IMockable"/>-decorated interface would silently flip the
/// banner to a false positive.
/// </remarks>
public interface IMockable
{
    /// <summary>Human-readable reason this service is mocked — surfaced in the banner tooltip.</summary>
    string MockReason { get; }
}
