namespace PoRedoImage.Web.Features.Shared;

/// <summary>
/// Authorization policy names shared across slices.
/// </summary>
/// <remarks>
/// §2 requires slices to be autonomous — they must not reference each other. A policy is
/// inherently cross-slice: Auth <em>defines</em> it, another slice <em>requires</em> it. Naming the
/// policy here lets both depend on the shared vocabulary instead of the consumer depending on the
/// Auth slice. The policy's behaviour still lives entirely in <c>AuthServiceExtensions</c>.
/// </remarks>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Guards <c>/api/diag</c>. In Production the caller must be on the
    /// <c>Diagnostics:AdminEmails</c> allow-list (fail-closed — an empty list denies everyone);
    /// in non-Production any authenticated user may read diagnostics.
    /// </summary>
    public const string Diagnostics = "DiagnosticsAccess";
}
