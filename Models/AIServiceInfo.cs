namespace WorkflowCreator.Models
{
    public class AIServiceInfo
    {
        public string Provider { get; set; } = "";
        public string ModelId { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public bool IsAvailable { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
