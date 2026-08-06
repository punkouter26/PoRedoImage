namespace PoRedoImage.Web.Features.Shared;

/// <summary>
/// Opts endpoint groups into real antiforgery validation (§2 Security).
/// </summary>
/// <remarks>
/// <para>
/// <c>app.UseAntiforgery()</c> on its own does NOT protect a JSON API — before this, the middleware
/// was registered but inert, leaving a cookie-authenticated write API with no CSRF defence beyond
/// the <c>SameSite=Strict</c> cookie. The middleware validates only requests it recognises as form
/// posts; for anything else it merely marks the request "not validated" so that a later
/// <c>ReadFormAsync</c> throws. Every endpoint here takes JSON, so nothing was ever checked.
/// </para>
/// <para>
/// Enforcement therefore lives in <see cref="AntiforgeryValidationFilter"/>, and deliberately does
/// NOT stamp <c>IAntiforgeryMetadata.RequiresValidation = true</c> on the endpoint. Doing both is
/// not additive — it is broken: the middleware would tag the request as unvalidated, and the
/// filter's own <c>ValidateRequestAsync</c> then trips that guard inside the token store and fails
/// even when the caller presented a correct cookie/header pair. One owner of the check, not two.
/// </para>
/// <para>
/// Applied at the GROUP level on purpose: the filter ignores safe methods, so tagging a whole group
/// costs the reads nothing and means a newly added POST inside an existing group is protected by
/// default rather than by memory.
/// </para>
/// </remarks>
public static class AntiforgeryExtensions
{
    /// <summary>Header the WASM client sends the request token in; mirrored in the client's handler.</summary>
    public const string TokenHeaderName = "X-CSRF-TOKEN";

    /// <summary>Requires a valid antiforgery token on every unsafe (POST/PUT/PATCH/DELETE) request in the group.</summary>
    public static RouteGroupBuilder RequireAntiforgeryValidation(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<AntiforgeryValidationFilter>();
        return group;
    }

    /// <summary>Requires a valid antiforgery token on this endpoint when the method is unsafe.</summary>
    public static RouteHandlerBuilder RequireAntiforgeryValidation(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter<AntiforgeryValidationFilter>();
        return builder;
    }
}
