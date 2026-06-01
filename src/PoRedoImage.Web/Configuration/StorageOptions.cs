using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Strongly-typed binding for the <c>Storage:</c> configuration section.
/// <para>
/// When the connection string is empty, we keep the app booting in Development
/// (the gallery simply shows an empty state) but log a single, persistent warning
/// so misconfigurations are visible in the console (Po2Logic F9 mitigation).
/// </para>
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string? ConnectionString { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    private readonly ILogger<StorageOptionsValidator> _logger;
    public StorageOptionsValidator(ILogger<StorageOptionsValidator> logger) => _logger = logger;

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (options.IsConfigured) return ValidateOptionsResult.Success;

        _logger.LogWarning(
            "Storage:ConnectionString is not configured. The User Images gallery, Bulk Prompt persistence, " +
            "and Table Storage features will be disabled. Set it via user-secrets, Key Vault " +
            "(PoRedoImage-StorageConnectionString), or run Azurite locally and set UseDevelopmentStorage=true.");

        return ValidateOptionsResult.Success; // never block startup
    }
}
