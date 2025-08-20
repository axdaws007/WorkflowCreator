namespace WorkflowCreator.Models
{
    public class AISystemDiagnostics
    {
        public SystemHealthResult Health { get; set; } = new();
        public List<LocalModelInfo> AvailableLocalModels { get; set; } = new();
        public AIServiceInfo CloudServiceInfo { get; set; } = new();
        public Dictionary<string, object> PerformanceMetrics { get; set; } = new();
        public List<string> ConfigurationIssues { get; set; } = new();
        public List<string> OptimizationSuggestions { get; set; } = new();
    }
}
