using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace PoRedoImage.Web.Features.Shared;

/// <summary>
/// Generic endpoint filter that runs DataAnnotations validation on the first argument of type <typeparamref name="T"/>.
/// Minimal APIs do not validate [Range]/[Required] attributes automatically — this filter bridges the gap.
/// Reusable across all feature endpoint groups.
/// </summary>
/// <remarks>
/// <typeparamref name="T"/> is annotated for the trim analyzer (§1): DataAnnotations discovers
/// <c>[Required]</c>/<c>[Range]</c> by reflecting over the type's properties, so every member the
/// validator may read has to survive trimming. Call sites close the generic over a statically
/// known DTO, which satisfies the annotation at compile time.
/// </remarks>
internal sealed class ValidationFilter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : IEndpointFilter where T : class
{
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DataAnnotations' ValidationContext/Validator are unconditionally " +
                        "[RequiresUnreferencedCode]; the DynamicallyAccessedMembers annotation on T " +
                        "above is what actually keeps the validated DTO's members alive, and this " +
                        "host assembly is never trimmed.")]
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var arg = context.Arguments.OfType<T>().FirstOrDefault();
        if (arg is not null)
        {
            var validationContext = new ValidationContext(arg);
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(arg, validationContext, results, validateAllProperties: true))
            {
                var detail = string.Join("; ", results.Select(r => r.ErrorMessage).Where(e => e is not null));
                return Results.Problem(detail: detail, statusCode: 400, title: "Validation Error");
            }
        }

        return await next(context);
    }
}
