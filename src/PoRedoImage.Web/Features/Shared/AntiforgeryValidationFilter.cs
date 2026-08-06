using Microsoft.AspNetCore.Antiforgery;

namespace PoRedoImage.Web.Features.Shared;

/// <summary>
/// Validates the antiforgery token on unsafe (POST/PUT/PATCH/DELETE) JSON requests.
/// </summary>
/// <remarks>
/// <para>
/// This filter exists because <c>app.UseAntiforgery()</c> does not protect a JSON API. The built-in
/// middleware validates only requests it recognises as form posts, so a JSON <c>POST</c> sails
/// through with no token and no error. That was verified against the running host: even with the
/// endpoint's metadata correctly resolving to <c>RequiresValidation = true</c>, an untokened
/// <c>POST /api/bulk-generate/prompts</c> still returned 204.
/// </para>
/// <para>
/// Running as an endpoint filter also puts the check after model binding and authorization, so a
/// rejected token produces a ProblemDetails 400 consistent with the rest of the API surface rather
/// than an exception escaping the middleware.
/// </para>
/// </remarks>
internal sealed class AntiforgeryValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // Safe methods carry no state change, so they need no token — this mirrors the
        // middleware's own method filter and keeps group-level application free for reads.
        if (!IsUnsafeMethod(http.Request.Method))
            return await next(context);

        var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            // The exception message names the missing cookie/header; that is useful in a log but
            // is not echoed to the caller, who only needs to know the token was unacceptable.
            return Results.Problem(
                title: "Invalid antiforgery token",
                detail: "The request did not include a valid antiforgery token. "
                      + "Fetch one from /api/antiforgery/token and resend it in the "
                      + $"{AntiforgeryExtensions.TokenHeaderName} header.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }

    private static bool IsUnsafeMethod(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
