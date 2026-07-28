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
    [InlineData(null)]
    [InlineData("")]
    public void ImageData_RequiredRejects_Empty(string? imageData)
    {
        var req = new ImageAnalysisRequest { ImageData = imageData!, ContentType = "image/png" };
        Assert.Contains(Validate(req), r => r.MemberNames.Contains(nameof(ImageAnalysisRequest.ImageData)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ContentType_RequiredRejects_Empty(string? contentType)
    {
        var req = new ImageAnalysisRequest { ImageData = "data", ContentType = contentType! };
        Assert.Contains(Validate(req), r => r.MemberNames.Contains(nameof(ImageAnalysisRequest.ContentType)));
    }

    [Theory]
    [InlineData(199)]   // below lower bound
    [InlineData(501)]   // above upper bound
    [InlineData(0)]
    [InlineData(-1)]
    public void DescriptionLength_OutsideRange_Fails(int length)
    {
        var req = new ImageAnalysisRequest
        {
            ImageData = "data",
            ContentType = "image/png",
            DescriptionLength = length,
        };
        Assert.Contains(Validate(req), r => r.MemberNames.Contains(nameof(ImageAnalysisRequest.DescriptionLength)));
    }

    [Theory]
    [InlineData(200)]   // lower bound
    [InlineData(350)]
    [InlineData(500)]   // upper bound
    public void DescriptionLength_OnBoundaries_Passes(int length)
    {
        var req = new ImageAnalysisRequest
        {
            ImageData = "data",
            ContentType = "image/png",
            DescriptionLength = length,
        };
        Assert.Empty(Validate(req));
    }

    // ─── Finding 4: MaxCountAttribute / MaxItemLengthAttribute / StringLength boundaries ────────
    //
    // Precomputed* fields are client-supplied free text that flows verbatim into a metered model's
    // prompt (see ImageAnalysisRequest's remarks); these two theories pin the exact boundary each
    // attribute enforces: PrecomputedTags may hold at most 20 entries (MaxCountAttribute) of at most
    // 100 characters each (MaxItemLengthAttribute), and PrecomputedDescription at most 4000
    // characters (StringLengthAttribute).

    private static ImageAnalysisRequest BaseRequest() => new()
    {
        ImageData = "data",
        ContentType = "image/png",
    };

    [Theory]
    [InlineData("TagCount")]
    [InlineData("TagLength")]
    [InlineData("DescriptionLength")]
    public void PrecomputedFields_AtLimit_Passes(string scenario)
    {
        var req = BaseRequest();
        switch (scenario)
        {
            case "TagCount":
                req.PrecomputedTags = Enumerable.Repeat("tag", 20).ToList();
                break;
            case "TagLength":
                req.PrecomputedTags = [new string('t', 100)];
                break;
            case "DescriptionLength":
                req.PrecomputedDescription = new string('d', 4000);
                break;
        }

        Assert.Empty(Validate(req));
    }

    [Theory]
    [InlineData("TagCount")]
    [InlineData("TagLength")]
    [InlineData("DescriptionLength")]
    public void PrecomputedFields_JustOverLimit_Fails(string scenario)
    {
        var req = BaseRequest();
        string expectedMember;
        switch (scenario)
        {
            case "TagCount":
                req.PrecomputedTags = Enumerable.Repeat("tag", 21).ToList();
                expectedMember = nameof(ImageAnalysisRequest.PrecomputedTags);
                break;
            case "TagLength":
                req.PrecomputedTags = [new string('t', 101)];
                expectedMember = nameof(ImageAnalysisRequest.PrecomputedTags);
                break;
            case "DescriptionLength":
                req.PrecomputedDescription = new string('d', 4001);
                expectedMember = nameof(ImageAnalysisRequest.PrecomputedDescription);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        Assert.Contains(Validate(req), r => r.MemberNames.Contains(expectedMember));
    }
}
