namespace WorkflowCreator.Models
{
    public class WorkflowResultViewModel
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public WorkflowModel? Workflow { get; set; }
        public List<string>? Steps { get; set; }
        public string? GeneratedSql { get; set; }
        public string? SystemPrompt { get; set; }
        public string? UserPrompt { get; set; }
        public long ResponseTimeMs { get; set; }
    }
}
