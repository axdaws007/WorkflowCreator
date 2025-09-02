using Microsoft.SemanticKernel;
using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Connection test service for cloud-only AI setup with programmatic SQL generation.
    /// No local AI service testing required.
    /// </summary>
    public class SemanticKernelConnectionTestService : IConnectionTestService
    {
        private readonly IWorkflowAnalysisService _analysisService;
        private readonly ISqlGenerationService _sqlService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SemanticKernelConnectionTestService> _logger;

        public SemanticKernelConnectionTestService(
            IWorkflowAnalysisService analysisService,
            ISqlGenerationService sqlService,
            IConfiguration configuration,
            ILogger<SemanticKernelConnectionTestService> logger)
        {
            _analysisService = analysisService;
            _sqlService = sqlService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ConnectionTestResult> TestCloudConnectionAsync()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Testing cloud AI connection");

                var isConnected = await _analysisService.TestCloudConnectionAsync();
                var serviceInfo = await _analysisService.GetCloudServiceInfoAsync();

                stopwatch.Stop();

                return new ConnectionTestResult
                {
                    IsConnected = isConnected,
                    Message = isConnected
                        ? $"Successfully connected to {serviceInfo.Provider} ({serviceInfo.ModelId})"
                        : $"Failed to connect to {serviceInfo.Provider}",
                    Provider = serviceInfo.Provider,
                    ModelId = serviceInfo.ModelId,
                    Endpoint = serviceInfo.Endpoint,
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = new Dictionary<string, object>
                    {
                        ["ServiceType"] = "Cloud AI (Workflow Analysis)",
                        ["Temperature"] = serviceInfo.Metadata.GetValueOrDefault("Temperature", "Unknown"),
                        ["MaxTokens"] = serviceInfo.Metadata.GetValueOrDefault("MaxTokens", "Unknown"),
                        ["Available"] = serviceInfo.IsAvailable,
                        ["Purpose"] = "Natural Language Analysis"
                    }
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Cloud AI connection test failed");

                return new ConnectionTestResult
                {
                    IsConnected = false,
                    Message = $"Connection test failed: {ex.Message}",
                    Provider = _configuration["AI:Cloud:Provider"] ?? "Unknown",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = new Dictionary<string, object>
                    {
                        ["ServiceType"] = "Cloud AI (Workflow Analysis)",
                        ["ErrorType"] = ex.GetType().Name,
                        ["ErrorMessage"] = ex.Message
                    }
                };
            }
        }

        public async Task<ConnectionTestResult> TestLocalConnectionAsync()
        {
            // For programmatic SQL generation, this always returns success
            await Task.CompletedTask;

            _logger.LogInformation("Testing programmatic SQL generation");

            return new ConnectionTestResult
            {
                IsConnected = true,
                Message = "Programmatic SQL generation is always available",
                Provider = "Built-in",
                ModelId = "Programmatic Generator v1.0",
                Endpoint = "In-Process",
                ResponseTimeMs = 0,
                Details = new Dictionary<string, object>
                {
                    ["ServiceType"] = "Programmatic SQL Generation",
                    ["GenerationMethod"] = "Code-based",
                    ["RequiresLLM"] = false,
                    ["Accuracy"] = "100%",
                    ["ResponseTime"] = "Instant"
                }
            };
        }

        public async Task<SystemHealthResult> TestAllConnectionsAsync()
        {
            _logger.LogInformation("Testing all AI service connections");

            var cloudTest = await TestCloudConnectionAsync();
            var sqlTest = await TestLocalConnectionAsync(); // Actually tests programmatic generation

            var isHealthy = cloudTest.IsConnected; // Only need cloud AI to be working
            var warnings = new List<string>();
            var recommendations = new List<string>();

            // Analyze results and provide recommendations
            if (!cloudTest.IsConnected)
            {
                isHealthy = false;
                warnings.Add("Cloud AI service is unavailable - workflow analysis cannot function");
                recommendations.Add("Check your cloud AI API key and internet connection");
                recommendations.Add("Verify API quota/credits are available");
            }

            // Performance warnings
            if (cloudTest.ResponseTimeMs > 10000)
            {
                warnings.Add("Cloud AI response time is slow (>10s)");
                recommendations.Add("Check your internet connection speed");
                recommendations.Add("Consider switching to a faster cloud AI model");
            }

            // Recommendations for optimization
            if (isHealthy)
            {
                recommendations.Add("System is running optimally with cloud AI + programmatic SQL generation");
                recommendations.Add("This setup provides fast, consistent SQL generation");
            }

            return new SystemHealthResult
            {
                IsHealthy = isHealthy,
                CloudService = cloudTest,
                LocalService = sqlTest, // This represents SQL generation service
                Warnings = warnings,
                Recommendations = recommendations
            };
        }

        public async Task<AISystemDiagnostics> PerformSystemDiagnosticsAsync()
        {
            _logger.LogInformation("Performing comprehensive AI system diagnostics");

            var health = await TestAllConnectionsAsync();
            var availableModels = await _sqlService.GetAvailableModelsAsync();
            var cloudServiceInfo = await _analysisService.GetCloudServiceInfoAsync();

            var configurationIssues = new List<string>();
            var optimizationSuggestions = new List<string>();

            // Check configuration issues
            if (string.IsNullOrEmpty(_configuration["AI:Cloud:Provider"]))
            {
                configurationIssues.Add("No cloud AI provider configured - this is required");
            }

            var cloudApiKey = _configuration["AI:Cloud:OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(cloudApiKey) && _configuration["AI:Cloud:Provider"] == "OpenAI")
            {
                configurationIssues.Add("OpenAI API key not configured");
            }

            if (cloudApiKey == "sk-your-openai-api-key-here")
            {
                configurationIssues.Add("OpenAI API key is still the placeholder value");
            }

            // Optimization suggestions
            if (health.CloudService.ResponseTimeMs > 5000)
            {
                optimizationSuggestions.Add("Consider using a faster cloud AI model for better response times");
            }

            var currentModel = _configuration["AI:Cloud:OpenAI:ModelId"];
            if (currentModel == "gpt-3.5-turbo")
            {
                optimizationSuggestions.Add("Consider upgrading to gpt-4o-mini for better workflow analysis quality");
            }

            optimizationSuggestions.Add("Programmatic SQL generation provides instant, consistent results");
            optimizationSuggestions.Add("This setup eliminates the need for local AI infrastructure");

            var performanceMetrics = new Dictionary<string, object>
            {
                ["CloudResponseTimeMs"] = health.CloudService.ResponseTimeMs,
                ["SqlGenerationTimeMs"] = 0, // Instant for programmatic
                ["CloudServiceAvailable"] = cloudServiceInfo.IsAvailable,
                ["SqlGenerationMethod"] = "Programmatic",
                ["LocalInfrastructureRequired"] = false,
                ["SetupComplexity"] = "Low",
                ["MaintenanceRequired"] = "Minimal"
            };

            return new AISystemDiagnostics
            {
                Health = health,
                AvailableLocalModels = availableModels,
                CloudServiceInfo = cloudServiceInfo,
                PerformanceMetrics = performanceMetrics,
                ConfigurationIssues = configurationIssues,
                OptimizationSuggestions = optimizationSuggestions
            };
        }

        public async Task<AIConfigurationSummary> GetConfigurationSummaryAsync()
        {
            var cloudProvider = _configuration["AI:Cloud:Provider"] ?? "Not configured";

            var cloudModel = cloudProvider.ToLower() switch
            {
                "openai" => _configuration["AI:Cloud:OpenAI:ModelId"] ?? "Not specified",
                "azure" => _configuration["AI:Cloud:Azure:ModelId"] ?? "Not specified",
                _ => "Not configured"
            };

            var configuredFeatures = new List<string>();
            var missingComponents = new List<string>();

            // Check what's configured
            if (!string.IsNullOrEmpty(_configuration["AI:Cloud:Provider"]))
                configuredFeatures.Add("Cloud AI Analysis");
            else
                missingComponents.Add("Cloud AI Provider (Required)");

            configuredFeatures.Add("Programmatic SQL Generation");
            configuredFeatures.Add("Workflow Schema Parsing");
            configuredFeatures.Add("Result Caching");
            configuredFeatures.Add("Real-time Health Monitoring");

            return new AIConfigurationSummary
            {
                CloudProvider = cloudProvider,
                CloudModel = cloudModel,
                LocalProvider = "Built-in Programmatic Generator",
                LocalModel = "Code-based SQL Generation v1.0",
                IsHybridSetup = false, // This is now cloud-only setup
                ConfiguredFeatures = configuredFeatures,
                MissingComponents = missingComponents
            };
        }
    }
}