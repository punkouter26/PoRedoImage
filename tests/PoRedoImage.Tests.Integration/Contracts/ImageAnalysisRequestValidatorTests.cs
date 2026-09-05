using System.ComponentModel.DataAnnotations;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration.Contracts;

/// <summary>
/// Contract tests for the DataAnnotations validation rules on
/// <see cref="ImageAnalysisRequest"/>. This is the same validator the
/// <c>ImageAnalysisEndpoints</c> rely on (via the framework's model-binding
/// validation pipeline). Asserts the public contract — required fields and the
/// <see cref="RangeAttribute"/> on <c>DescriptionLength</c>.
///
/// Lives in the Integration tier (not Unit) because it pins a DTO/validation CONTRACT
/// consumed across the HTTP boundary — see the "Contractual Integration Testing" audit
/// item and ADR-013/ADR-018.
///
/// Each rule is ONE theory covering both its passing and failing side, rather than a
/// pass-method plus a fail-method per rule. The tier ceiling counts methods, not cases, and
/// this file previously spent 7 of the 50 on 4 rules; the coverage below is identical.
/// </summary>
public class ImageAnalysisRequestValidatorTests
{
    private static List<ValidationResult> Validate(ImageAnalysisRequest request)
    {
        var ctx = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, ctx, results, validateAllProperties: true);
        return results;
    }

    private static ImageAnalysisRequest BaseRequest() => new()
    {
        ImageData = "data",
        ContentType = "image/png",
    };

    [Fact]
    public void ValidRequest_PassesAllRules()
    {
        var req = new ImageAnalysisRequest
        {
            ImageData = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            ContentType = "image/png",
            DescriptionLength = 300,
        };
        Assert.Empty(Validate(req));
    }

    [Theory]
    [InlineData(nameof(ImageAnalysisRequest.ImageData), null)]
    [InlineData(nameof(ImageAnalysisRequest.ImageData), "")]
    [InlineData(nameof(ImageAnalysisRequest.ContentType), null)]
    [InlineData(nameof(ImageAnalysisRequest.ContentType), "")]
    public void RequiredStringFields_RejectNullOrEmpty(string member, string? value)
    {
        var req = BaseRequest();
        if (member == nameof(ImageAnalysisRequest.ImageData))
            req.ImageData = value!;
        else
            req.ContentType = value!;

        Assert.Contains(Validate(req), r => r.MemberNames.Contains(member));
    }

    [Theory]
    [InlineData(199, false)]   // below lower bound
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(501, false)]   // above upper bound
    [InlineData(200, true)]    // lower bound
    [InlineData(350, true)]
    [InlineData(500, true)]    // upper bound
    public void DescriptionLength_IsBoundedToItsRange(int length, bool shouldPass)
    {
        var req = BaseRequest();
        req.DescriptionLength = length;

        var results = Validate(req);
        if (shouldPass)
            Assert.Empty(results);
        else
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(ImageAnalysisRequest.DescriptionLength)));
    }

    // Precomputed* fields are client-supplied free text that flows verbatim into a metered model's
    // prompt (see ImageAnalysisRequest's remarks). This pins the exact boundary each attribute
    // enforces: PrecomputedTags may hold at most 20 entries (MaxCountAttribute) of at most 100
    // characters each (MaxItemLengthAttribute), and PrecomputedDescription at most 4000 characters
    // (StringLengthAttribute) — asserting both the last accepted value and the first rejected one.
    [Theory]
    [InlineData("TagCount", true)]
    [InlineData("TagCount", false)]
    [InlineData("TagLength", true)]
    [InlineData("TagLength", false)]
    [InlineData("DescriptionLength", true)]
    [InlineData("DescriptionLength", false)]
    public void PrecomputedFields_AreBoundedAtTheirLimit(string scenario, bool atLimit)
    {
        var req = BaseRequest();
        string member;

        switch (scenario)
        {
            case "TagCount":
                req.PrecomputedTags = Enumerable.Repeat("tag", atLimit ? 20 : 21).ToList();
                member = nameof(ImageAnalysisRequest.PrecomputedTags);
                break;
            case "TagLength":
                req.PrecomputedTags = [new string('t', atLimit ? 100 : 101)];
                member = nameof(ImageAnalysisRequest.PrecomputedTags);
                break;
            case "DescriptionLength":
                req.PrecomputedDescription = new string('d', atLimit ? 4000 : 4001);
                member = nameof(ImageAnalysisRequest.PrecomputedDescription);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var results = Validate(req);
        if (atLimit)
            Assert.Empty(results);
        else
            Assert.Contains(results, r => r.MemberNames.Contains(member));
    }
}
