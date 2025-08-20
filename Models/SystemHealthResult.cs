namespace WorkflowCreator.Models
{
    public class SystemHealthResult
    {
        public bool IsHealthy { get; set; }
        public ConnectionTestResult CloudService { get; set; } = new();
        public ConnectionTestResult LocalService { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }
}
