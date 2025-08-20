using System.Text.Json.Serialization;

namespace WorkflowCreator.Models
{
    /// <summary>
    /// View model for the AI connection testing page.
    /// Contains configuration information and testing results for both cloud and local AI services.
    /// </summary>
    public class ConnectionTestViewModel
    {
        /// <summary>
        /// Name of the cloud AI provider (e.g., "OpenAI", "Anthropic", "Azure").
        /// </summary>
        public string CloudProvider { get; set; } = "";

        /// <summary>
        /// Name of the local AI provider (e.g., "Ollama", "LocalAI").
        /// </summary>
        public string LocalProvider { get; set; } = "";

        /// <summary>
        /// Display-friendly endpoint for the cloud AI service.
        /// </summary>
        public string CloudEndpoint { get; set; } = "";

        /// <summary>
        /// Endpoint URL for the local AI service.
        /// </summary>
        public string LocalEndpoint { get; set; } = "";

        /// <summary>
        /// Model ID being used for cloud AI analysis.
        /// </summary>
        public string CloudModelId { get; set; } = "";

        /// <summary>
        /// Model ID being used for local AI SQL generation.
        /// </summary>
        public string LocalModelId { get; set; } = "";

        /// <summary>
        /// Whether the system is configured for hybrid cloud+local setup.
        /// </summary>
        public bool IsHybridSetup { get; set; }

        /// <summary>
        /// Current environment (Development, Production, etc.).
        /// </summary>
        public string Environment { get; set; } = "";

        /// <summary>
        /// Last successful connection test result for cloud service.
        /// Null if never tested or last test failed.
        /// </summary>
        public ConnectionTestResult? LastCloudTest { get; set; }

        /// <summary>
        /// Last successful connection test result for local service.
        /// Null if never tested or last test failed.
        /// </summary>
        public ConnectionTestResult? LastLocalTest { get; set; }

        /// <summary>
        /// List of available features based on current configuration.
        /// </summary>
        public List<string> AvailableFeatures { get; set; } = new();

        /// <summary>
        /// List of configuration warnings or missing components.
        /// </summary>
        public List<string> ConfigurationWarnings { get; set; } = new();

        /// <summary>
        /// List of optimization suggestions for better performance.
        /// </summary>
        public List<string> OptimizationSuggestions { get; set; } = new();

        /// <summary>
        /// Estimated cost per workflow analysis (cloud AI usage).
        /// Based on current model and average token usage.
        /// </summary>
        public decimal? EstimatedCostPerWorkflow { get; set; }

        /// <summary>
        /// System performance metrics.
        /// </summary>
        public Dictionary<string, object> PerformanceMetrics { get; set; } = new();

        /// <summary>
        /// Gets whether both services are properly configured.
        /// </summary>
        [JsonIgnore]
        public bool IsFullyConfigured => !string.IsNullOrEmpty(CloudProvider) &&
                                       !string.IsNullOrEmpty(LocalProvider) &&
                                       !ConfigurationWarnings.Any();

        /// <summary>
        /// Gets whether at least one AI service is configured.
        /// </summary>
        [JsonIgnore]
        public bool HasAnyConfiguration => !string.IsNullOrEmpty(CloudProvider) ||
                                         !string.IsNullOrEmpty(LocalProvider);

        /// <summary>
        /// Gets the recommended setup type based on configuration.
        /// </summary>
        [JsonIgnore]
        public string RecommendedSetup
        {
            get
            {
                if (IsHybridSetup && IsFullyConfigured)
                    return "Optimal (Hybrid Cloud + Local)";
                if (!string.IsNullOrEmpty(CloudProvider) && string.IsNullOrEmpty(LocalProvider))
                    return "Cloud Only (Higher costs)";
                if (string.IsNullOrEmpty(CloudProvider) && !string.IsNullOrEmpty(LocalProvider))
                    return "Local Only (Limited analysis quality)";
                return "Incomplete Configuration";
            }
        }

        /// <summary>
        /// Gets display-friendly configuration summary.
        /// </summary>
        [JsonIgnore]
        public string ConfigurationSummary
        {
            get
            {
                var parts = new List<string>();

                if (!string.IsNullOrEmpty(CloudProvider))
                    parts.Add($"Cloud: {CloudProvider} ({CloudModelId})");

                if (!string.IsNullOrEmpty(LocalProvider))
                    parts.Add($"Local: {LocalProvider} ({LocalModelId})");

                return parts.Any() ? string.Join(" | ", parts) : "No AI services configured";
            }
        }

        /// <summary>
        /// Creates a basic view model with minimal configuration.
        /// Used when configuration is incomplete or during initial setup.
        /// </summary>
        /// <param name="cloudProvider">Cloud provider name</param>
        /// <param name="localProvider">Local provider name</param>
        /// <returns>Minimal view model</returns>
        public static ConnectionTestViewModel CreateBasic(string? cloudProvider = null, string? localProvider = null)
        {
            return new ConnectionTestViewModel
            {
                CloudProvider = cloudProvider ?? "Not configured",
                LocalProvider = localProvider ?? "Not configured",
                CloudEndpoint = "Not configured",
                LocalEndpoint = "Not configured",
                ConfigurationWarnings = new List<string> { "Configuration incomplete" }
            };
        }

        /// <summary>
        /// Updates the view model with fresh test results.
        /// </summary>
        /// <param name="cloudResult">Latest cloud test result</param>
        /// <param name="localResult">Latest local test result</param>
        public void UpdateTestResults(ConnectionTestResult? cloudResult, ConnectionTestResult? localResult)
        {
            if (cloudResult?.IsConnected == true)
                LastCloudTest = cloudResult;

            if (localResult?.IsConnected == true)
                LastLocalTest = localResult;

            // Update performance metrics
            if (cloudResult != null)
                PerformanceMetrics["CloudResponseTimeMs"] = cloudResult.ResponseTimeMs;

            if (localResult != null)
                PerformanceMetrics["LocalResponseTimeMs"] = localResult.ResponseTimeMs;
        }
    }
}
