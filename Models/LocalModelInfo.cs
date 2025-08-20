namespace WorkflowCreator.Models
{
    public class LocalModelInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public long SizeBytes { get; set; }
        public bool IsAvailable { get; set; }
        public string[] Capabilities { get; set; } = Array.Empty<string>();
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}
