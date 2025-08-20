namespace WorkflowCreator.Services
{
    public class CloudAIConfig
    {
        public string Provider { get; set; } = "OpenAI";
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 2000;
        public OpenAIConfig OpenAI { get; set; } = new();
        public AzureConfig Azure { get; set; } = new();
    }
}
