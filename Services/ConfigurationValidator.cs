using WorkflowCreator.Models;
using WorkflowCreator.Services.Interfaces;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Service for validating application configuration on startup.
    /// Provides comprehensive checking of AI service configurations, API keys, and dependencies.
    /// </summary>
    public class ConfigurationValidator : IConfigurationValidator
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConfigurationValidator> _logger;

        public ConfigurationValidator(IConfiguration configuration, ILogger<ConfigurationValidator> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public ValidationResult ValidateConfiguration()
        {
            var result = new ValidationResult();

            _logger.LogInformation("Starting comprehensive configuration validation");

            // Validate AI configuration
            var aiValidation = ValidateAIConfiguration();
            result.Merge(aiValidation);

            // Validate schema configuration
            var schemaValidation = ValidateSchemaConfiguration();
            result.Merge(schemaValidation);

            // Validate general application settings
            ValidateGeneralConfiguration(result);

            // Validate security settings
            ValidateSecurityConfiguration(result);

            _logger.LogInformation("Configuration validation completed: {summary}", result.GetSummary());

            return result;
        }

        public ValidationResult ValidateAIConfiguration()
        {
            var result = new ValidationResult();

            _logger.LogDebug("Validating AI service configuration");

            // Validate cloud AI configuration
            var cloudProvider = _configuration["AI:Cloud:Provider"];
            if (!string.IsNullOrEmpty(cloudProvider))
            {
                switch (cloudProvider.ToLower())
                {
                    case "openai":
                        ValidateOpenAIConfig(result);
                        break;
                    case "azure":
                        ValidateAzureConfig(result);
                        break;
                    case "anthropic":
                        ValidateAnthropicConfig(result);
                        break;
                    default:
                        result.AddWarning($"Unknown cloud provider: {cloudProvider}");
                        break;
                }
            }
            else
            {
                result.AddWarning("No cloud AI provider configured - application will use local AI only");
            }

            // Validate local AI configuration
            ValidateLocalAIConfig(result);

            // Validate hybrid configuration
            ValidateHybridConfiguration(result);

            // Check for at least one working AI provider
            var hasCloudAI = !string.IsNullOrEmpty(_configuration["AI:Cloud:Provider"]);
            var hasLocalAI = !string.IsNullOrEmpty(_configuration["AI:Local:ModelId"]);

            if (!hasCloudAI && !hasLocalAI)
            {
                result.AddError("No AI providers configured - application cannot function");
            }

            return result;
        }

        public ValidationResult ValidateSchemaConfiguration()
        {
            var result = new ValidationResult();

            _logger.LogDebug("Validating workflow schema configuration");

            var useSchemaFile = _configuration.GetValue<bool>("WorkflowSchema:UseSchemaFile", true);
            if (useSchemaFile)
            {
                var schemaFile = _configuration["WorkflowSchema:SchemaFile"] ?? "workflow-schema.sql";
                var schemaPath = Path.Combine(Directory.GetCurrentDirectory(), schemaFile);

                if (!File.Exists(schemaPath))
                {
                    result.AddError($"Workflow schema file not found: {schemaPath}");
                }
                else
                {
                    // Validate schema file content
                    try
                    {
                        var schemaContent = File.ReadAllText(schemaPath);
                        if (string.IsNullOrWhiteSpace(schemaContent))
                        {
                            result.AddError("Workflow schema file is empty");
                        }
                        else if (!schemaContent.Contains("PAWSProcessTemplate") ||
                                !schemaContent.Contains("PAWSActivity") ||
                                !schemaContent.Contains("PAWSActivityTransition"))
                        {
                            result.AddWarning("Schema file may be incomplete - missing expected PAWS tables");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AddError($"Failed to read schema file: {ex.Message}");
                    }
                }
            }

            var validateOnStartup = _configuration.GetValue<bool>("WorkflowSchema:ValidateSchemaOnStartup", true);
            if (!validateOnStartup)
            {
                result.AddWarning("Schema validation on startup is disabled");
            }

            return result;
        }

        public List<string> GetConfigurationRecommendations()
        {
            var recommendations = new List<string>();

            var cloudProvider = _configuration["AI:Cloud:Provider"];
            var localModel = _configuration["AI:Local:ModelId"];
            var isHybridSetup = !string.IsNullOrEmpty(cloudProvider) && !string.IsNullOrEmpty(localModel);

            // Hybrid setup recommendations
            if (!isHybridSetup)
            {
                if (string.IsNullOrEmpty(cloudProvider))
                {
                    recommendations.Add("Consider adding cloud AI provider for better natural language analysis");
                }

                if (string.IsNullOrEmpty(localModel))
                {
                    recommendations.Add("Consider adding local AI model for cost-effective SQL generation");
                }
            }

            // Model recommendations
            var openAiModel = _configuration["AI:Cloud:OpenAI:ModelId"];
            if (openAiModel == "gpt-3.5-turbo")
            {
                recommendations.Add("Consider upgrading to gpt-4o-mini for better workflow analysis quality");
            }

            if (localModel == "codellama:7b")
            {
                recommendations.Add("For better SQL quality, consider upgrading to codellama:13b if you have sufficient RAM");
            }

            // Performance recommendations
            var cacheEnabled = _configuration.GetValue<bool>("AI:Hybrid:EnableResultCaching", true);
            if (!cacheEnabled)
            {
                recommendations.Add("Enable result caching to reduce AI API costs and improve response times");
            }

            // Security recommendations
            var apiKey = _configuration["AI:Cloud:OpenAI:ApiKey"];
            if (apiKey == "sk-your-openai-api-key-here")
            {
                recommendations.Add("Replace the placeholder OpenAI API key with your actual key");
            }

            var maskKeys = _configuration.GetValue<bool>("Security:MaskApiKeysInLogs", true);
            if (!maskKeys)
            {
                recommendations.Add("Enable API key masking in logs for better security");
            }

            return recommendations;
        }

        #region Private Validation Methods

        private void ValidateOpenAIConfig(ValidationResult result)
        {
            var apiKey = _configuration["AI:Cloud:OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                result.AddError("OpenAI API key not configured");
            }
            else if (apiKey == "sk-your-openai-api-key-here")
            {
                result.AddError("OpenAI API key is still the placeholder value - replace with your actual key");
            }
            else if (!apiKey.StartsWith("sk-"))
            {
                result.AddWarning("OpenAI API key format appears invalid (should start with 'sk-')");
            }

            var modelId = _configuration["AI:Cloud:OpenAI:ModelId"];
            if (string.IsNullOrEmpty(modelId))
            {
                result.AddWarning("OpenAI model ID not specified, using default");
            }

            var orgId = _configuration["AI:Cloud:OpenAI:OrganizationId"];
            if (!string.IsNullOrEmpty(orgId) && !orgId.StartsWith("org-"))
            {
                result.AddWarning("OpenAI organization ID format appears invalid (should start with 'org-')");
            }
        }

        private void ValidateAzureConfig(ValidationResult result)
        {
            var endpoint = _configuration["AI:Cloud:Azure:Endpoint"];
            var apiKey = _configuration["AI:Cloud:Azure:ApiKey"];

            if (string.IsNullOrEmpty(endpoint))
            {
                result.AddError("Azure OpenAI endpoint not configured");
            }
            else if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                result.AddError("Azure OpenAI endpoint is not a valid URL");
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                result.AddError("Azure OpenAI API key not configured");
            }

            var modelId = _configuration["AI:Cloud:Azure:ModelId"];
            if (string.IsNullOrEmpty(modelId))
            {
                result.AddWarning("Azure OpenAI deployment name not specified");
            }
        }

        private void ValidateAnthropicConfig(ValidationResult result)
        {
            var apiKey = _configuration["AI:Cloud:Anthropic:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                result.AddError("Anthropic API key not configured");
            }

            result.AddWarning("Anthropic provider is not yet fully implemented");
        }

        private void ValidateLocalAIConfig(ValidationResult result)
        {
            var endpoint = _configuration["AI:Local:Endpoint"] ?? "http://localhost:11434";
            var modelId = _configuration["AI:Local:ModelId"];

            if (string.IsNullOrEmpty(modelId))
            {
                result.AddWarning("Local AI model not specified, using default");
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                result.AddWarning("Local AI endpoint is not a valid URL");
            }

            // Check for common Ollama models
            var recommendedModels = new[] { "codellama:7b", "codellama:13b", "deepseek-coder:6.7b" };
            if (!string.IsNullOrEmpty(modelId) && !recommendedModels.Contains(modelId))
            {
                result.AddWarning($"Model '{modelId}' is not in the recommended list for SQL generation");
            }
        }

        private void ValidateHybridConfiguration(ValidationResult result)
        {
            var preferCloud = _configuration.GetValue<bool>("AI:Hybrid:PreferCloudForAnalysis", true);
            var enableFallback = _configuration.GetValue<bool>("AI:Hybrid:EnableCloudFallback", true);

            var hasCloudProvider = !string.IsNullOrEmpty(_configuration["AI:Cloud:Provider"]);
            var hasLocalProvider = !string.IsNullOrEmpty(_configuration["AI:Local:ModelId"]);

            if (preferCloud && !hasCloudProvider)
            {
                result.AddWarning("Configured to prefer cloud AI but no cloud provider is set up");
            }

            if (enableFallback && !hasLocalProvider)
            {
                result.AddWarning("Cloud fallback enabled but no local AI provider configured");
            }

            if (hasCloudProvider && hasLocalProvider)
            {
                result.AddWarning("Hybrid setup detected - excellent configuration for optimal cost and performance");
            }
        }

        private void ValidateGeneralConfiguration(ValidationResult result)
        {
            // Check logging configuration
            var defaultLogLevel = _configuration["Logging:LogLevel:Default"];
            if (string.IsNullOrEmpty(defaultLogLevel))
            {
                result.AddWarning("Default log level not configured");
            }

            // Check allowed hosts
            var allowedHosts = _configuration["AllowedHosts"];
            if (allowedHosts == "*")
            {
                result.AddWarning("AllowedHosts is set to '*' - consider restricting for production");
            }

            // Check feature flags
            var featuresSection = _configuration.GetSection("Features");
            if (!featuresSection.Exists())
            {
                result.AddWarning("No feature flags configured - using defaults");
            }
        }

        private void ValidateSecurityConfiguration(ValidationResult result)
        {
            var validateApiKeys = _configuration.GetValue<bool>("Security:ValidateApiKeys", true);
            if (!validateApiKeys)
            {
                result.AddWarning("API key validation is disabled - security risk in production");
            }

            var maskApiKeys = _configuration.GetValue<bool>("Security:MaskApiKeysInLogs", true);
            if (!maskApiKeys)
            {
                result.AddWarning("API key masking in logs is disabled - security risk");
            }

            var maxRequestSize = _configuration.GetValue<long>("Security:MaxRequestSize", 1048576);
            if (maxRequestSize > 10485760) // 10MB
            {
                result.AddWarning("Max request size is very large - consider reducing for security");
            }
        }

        #endregion
    }
}