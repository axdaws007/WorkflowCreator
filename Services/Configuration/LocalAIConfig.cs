namespace WorkflowCreator.Services
{
    public class LocalAIConfig
    {
        public string Provider { get; set; } = "Ollama";
        public string Endpoint { get; set; } = "http://localhost:11434";
        public string ModelId { get; set; } = "codellama:7b";
        public string AnalysisModel { get; set; } = "llama3:8b";
        public string FunctionModel { get; set; } = "llama3:8b";
        public double Temperature { get; set; } = 0.1;
        public int MaxTokens { get; set; } = 3000;
    }
}
