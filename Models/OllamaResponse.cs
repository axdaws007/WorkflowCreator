namespace WorkflowCreator.Models
{
    public class OllamaResponse
    {
        public string? model { get; set; }
        public string? response { get; set; }
        public bool done { get; set; }
        public long? total_duration { get; set; }
        public long? eval_count { get; set; }
    }
}
