namespace WorkflowCreator.Models
{
    public class AIConfigurationSummary
    {
        public string CloudProvider { get; set; } = "";
        public string CloudModel { get; set; } = "";
        public string LocalProvider { get; set; } = "";
        public string LocalModel { get; set; } = "";
        public bool IsHybridSetup { get; set; }
        public List<string> ConfiguredFeatures { get; set; } = new();
        public List<string> MissingComponents { get; set; } = new();
    }
}
