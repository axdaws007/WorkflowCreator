namespace WorkflowCreator.Services
{
    public class SemanticKernelConfig
    {
        public CloudAIConfig Cloud { get; set; } = new();
        public LocalAIConfig Local { get; set; } = new();
    }
}
