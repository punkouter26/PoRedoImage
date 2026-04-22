using System.ComponentModel.DataAnnotations;

namespace PoRedoImage.Web.Features;

/// <summary>
/// Generic endpoint filter that runs DataAnnotations validation on the first argument of type <typeparamref name="T"/>.
/// Minimal APIs do not validate [Range]/[Required] attributes automatically — this filter bridges the gap.
/// Reusable across all feature endpoint groups.
/// </summary>
internal sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
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
