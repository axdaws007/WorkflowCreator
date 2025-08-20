namespace WorkflowCreator.Models
{
    public class ConnectionTestResult
    {
        public bool IsConnected { get; set; }
        public string Message { get; set; } = "";
        public string Provider { get; set; } = "";
        public string ModelId { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public long ResponseTimeMs { get; set; }
        public DateTime TestedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Details { get; set; } = new();
    }
}
