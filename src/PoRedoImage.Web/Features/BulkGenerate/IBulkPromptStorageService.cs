namespace PoRedoImage.Web.Features.BulkGenerate;

public interface IBulkPromptStorageService
{
    Task<string[]?> LoadPromptsAsync(string userId);
    Task SavePromptsAsync(string userId, string[] prompts);
}
