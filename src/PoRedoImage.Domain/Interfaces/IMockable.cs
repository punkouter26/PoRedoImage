namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Marker interface for mock/stub service implementations.
/// </summary>
/// <remarks>
/// Interface Segregation Principle (SOLID-I): kept minimal so any mock service can implement it
/// without taking on unrelated obligations. When any service implementing <see cref="IMockable"/>
/// is resolved from the DI container, the UI displays a "USING MOCK DATA" warning banner,
/// making mock-mode immediately visible to developers and LLMs reviewing the running app.
/// </remarks>
public interface IMockable
{
    /// <summary>Gets a human-readable description of what data this mock provides.</summary>
    string MockDescription { get; }
}
