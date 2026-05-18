namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Domain service interface for describing the primary person in an image.
/// Separated from IGenerativeAiService (ISP): only BulkGenerate depends on this.
/// </summary>
public interface IBulkDescribeService
{
    Task<string> DescribePersonAsync(byte[] imageData, CancellationToken ct = default);
}
