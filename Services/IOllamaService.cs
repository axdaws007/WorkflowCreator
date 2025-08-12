namespace WorkflowCreator.Services
{
    public interface IOllamaService
    {
        Task<(bool Success, string? GeneratedSql, string? ErrorMessage)> GenerateSqlAsync(string systemPrompt, string userPrompt);
        Task<(bool IsConnected, string Message)> TestConnectionAsync();
    }
}
