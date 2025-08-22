namespace WorkflowCreator.Models
{
    public class SqlGenerationResult
    {
        public bool Success { get; set; }
        public string? GeneratedSql { get; set; }
        public string? ErrorMessage { get; set; }
        public int TokensUsed { get; set; }
        public long GenerationTimeMs { get; set; }
        public string ModelUsed { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string SystemPrompt { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
    }
}
