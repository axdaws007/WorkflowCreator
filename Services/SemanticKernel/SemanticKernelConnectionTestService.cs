using Microsoft.SemanticKernel;
using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
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
                        ["Available"] = serviceInfo.IsAvailable
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
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Testing local AI connection");

                var isConnected = await _sqlService.TestLocalConnectionAsync();
                var availableModels = await _sqlService.GetAvailableModelsAsync();

                stopwatch.Stop();

                var provider = _configuration["AI:Local:Provider"] ?? "Unknown";
                var modelId = _configuration["AI:Local:ModelId"] ?? "Unknown";
                var endpoint = _configuration["AI:Local:Endpoint"] ?? "Unknown";

                return new ConnectionTestResult
                {
                    IsConnected = isConnected,
                    Message = isConnected
                        ? $"Successfully connected to {provider} ({modelId})"
                        : $"Failed to connect to {provider}",
                    Provider = provider,
                    ModelId = modelId,
                    Endpoint = endpoint,
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = new Dictionary<string, object>
                    {
                        ["ServiceType"] = "Local AI (SQL Generation)",
                        ["AvailableModels"] = availableModels.Count,
                        ["ModelsInstalled"] = availableModels.Count(m => m.IsAvailable),
                        ["Temperature"] = _configuration.GetValue<double>("AI:Local:Temperature", 0.1),
                        ["MaxTokens"] = _configuration.GetValue<int>("AI:Local:MaxTokens", 3000)
                    }
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Local AI connection test failed");

                return new ConnectionTestResult
                {
                    IsConnected = false,
                    Message = $"Connection test failed: {ex.Message}",
                    Provider = _configuration["AI:Local:Provider"] ?? "Unknown",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = new Dictionary<string, object>
                    {
                        ["ServiceType"] = "Local AI (SQL Generation)",
                        ["ErrorType"] = ex.GetType().Name,
                        ["ErrorMessage"] = ex.Message
                    }
                };
            }
        }

        public async Task<SystemHealthResult> TestAllConnectionsAsync()
        {
            _logger.LogInformation("Testing all AI service connections");

            var cloudTest = await TestCloudConnectionAsync();
            var localTest = await TestLocalConnectionAsync();

            var isHealthy = cloudTest.IsConnected || localTest.IsConnected; // At least one should work
            var warnings = new List<string>();
            var recommendations = new List<string>();

            // Analyze results and provide recommendations
            if (!cloudTest.IsConnected && !localTest.IsConnected)
            {
                isHealthy = false;
                warnings.Add("No AI services are available");
                recommendations.Add("Check your internet connection and Ollama service status");
            }
            else if (!cloudTest.IsConnected)
            {
                warnings.Add("Cloud AI service is unavailable - using local AI for all tasks");
                recommendations.Add("Check your cloud AI API key and internet connection");
            }
            else if (!localTest.IsConnected)
            {
                warnings.Add("Local AI service is unavailable - using cloud AI for all tasks (may increase costs)");
                recommendations.Add("Check that Ollama is running and models are downloaded");
            }

            // Performance warnings
            if (cloudTest.ResponseTimeMs > 10000)
            {
                warnings.Add("Cloud AI response time is slow (>10s)");
                recommendations.Add("Check your internet connection speed");
            }

            if (localTest.ResponseTimeMs > 30000)
            {
                warnings.Add("Local AI response time is very slow (>30s)");
                recommendations.Add("Consider using a smaller model or upgrading hardware");
            }

            return new SystemHealthResult
            {
                IsHealthy = isHealthy,
                CloudService = cloudTest,
                LocalService = localTest,
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
                configurationIssues.Add("No cloud AI provider configured");
            }

            if (string.IsNullOrEmpty(_configuration["AI:Local:ModelId"]))
            {
                configurationIssues.Add("No local AI model specified");
            }

            var cloudApiKey = _configuration["AI:Cloud:OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(cloudApiKey) && _configuration["AI:Cloud:Provider"] == "OpenAI")
            {
                configurationIssues.Add("OpenAI API key not configured");
            }

            // Optimization suggestions
            if (!availableModels.Any(m => m.IsAvailable))
            {
                optimizationSuggestions.Add("Download at least one local model for SQL generation");
            }

            if (availableModels.Any(m => m.Name.Contains("7b")) && !availableModels.Any(m => m.Name.Contains("13b")))
            {
                optimizationSuggestions.Add("Consider downloading a larger model (13b) for better SQL quality");
            }

            if (health.CloudService.ResponseTimeMs > 5000)
            {
                optimizationSuggestions.Add("Consider using a faster cloud AI model or check network connectivity");
            }

            var performanceMetrics = new Dictionary<string, object>
            {
                ["CloudResponseTimeMs"] = health.CloudService.ResponseTimeMs,
                ["LocalResponseTimeMs"] = health.LocalService.ResponseTimeMs,
                ["AvailableLocalModels"] = availableModels.Count(m => m.IsAvailable),
                ["TotalLocalModels"] = availableModels.Count,
                ["CloudServiceAvailable"] = cloudServiceInfo.IsAvailable,
                ["HybridSetup"] = health.CloudService.IsConnected && health.LocalService.IsConnected
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
            var localProvider = _configuration["AI:Local:Provider"] ?? "Ollama";

            var cloudModel = cloudProvider.ToLower() switch
            {
                "openai" => _configuration["AI:Cloud:OpenAI:ModelId"] ?? "Not specified",
                "azure" => _configuration["AI:Cloud:Azure:ModelId"] ?? "Not specified",
                _ => "Not configured"
            };

            var localModel = _configuration["AI:Local:ModelId"] ?? "Not specified";

            var configuredFeatures = new List<string>();
            var missingComponents = new List<string>();

            // Check what's configured
            if (!string.IsNullOrEmpty(_configuration["AI:Cloud:Provider"]))
                configuredFeatures.Add("Cloud AI Analysis");
            else
                missingComponents.Add("Cloud AI Provider");

            if (!string.IsNullOrEmpty(_configuration["AI:Local:ModelId"]))
                configuredFeatures.Add("Local AI SQL Generation");
            else
                missingComponents.Add("Local AI Model");

            configuredFeatures.Add("Workflow Schema Parsing");
            configuredFeatures.Add("Result Caching");

            var isHybridSetup = !string.IsNullOrEmpty(_configuration["AI:Cloud:Provider"]) &&
                              !string.IsNullOrEmpty(_configuration["AI:Local:ModelId"]);

            return new AIConfigurationSummary
            {
                CloudProvider = cloudProvider,
                CloudModel = cloudModel,
                LocalProvider = localProvider,
                LocalModel = localModel,
                IsHybridSetup = isHybridSetup,
                ConfiguredFeatures = configuredFeatures,
                MissingComponents = missingComponents
            };
        }
    }
}
