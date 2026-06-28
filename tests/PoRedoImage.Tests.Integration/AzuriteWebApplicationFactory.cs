namespace PoRedoImage.Tests.Integration;

/// <summary>
/// A <see cref="CustomWebApplicationFactory"/> wired to the connection string of the shared
/// <see cref="AzuriteContainerFixture"/>, so storage-backed endpoints run against the single
/// session-wide Azurite container instead of a per-class one. Construct it from a test class with
/// the fixture's <see cref="AzuriteContainerFixture.ConnectionString"/>; the factory itself no
/// longer owns a container lifecycle.
/// </summary>
public sealed class AzuriteWebApplicationFactory : CustomWebApplicationFactory
{
    private readonly string _connectionString;

    public AzuriteWebApplicationFactory(string connectionString) => _connectionString = connectionString;

    protected override string StorageConnectionString => _connectionString;
}
