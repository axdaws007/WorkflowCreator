using Microsoft.Extensions.Configuration;
using WorkflowCreator.Models;
using WorkflowCreator.Services.Interfaces;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Service for validating application configuration on startup.
    /// Updated for cloud-only AI setup with programmatic SQL generation.
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

            _logger.LogInformation("Starting cloud-only configuration validation");

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

            _logger.LogDebug("Validating cloud AI configuration");

            // Validate cloud AI configuration - now required
            var cloudProvider = _configuration["AI:Cloud:Provider"];
            if (string.IsNullOrEmpty(cloudProvider))
            {
                result.AddError("Cloud AI provider is required for this setup - please configure AI:Cloud:Provider");
            }
            else
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
                        result.AddError($"Unknown cloud provider: {cloudProvider}");
                        break;
                }
            }

            // Validate SQL generation configuration (programmatic)
            ValidateProgrammaticSqlConfig(result);

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

            // Cloud provider recommendations
            if (string.IsNullOrEmpty(cloudProvider))
            {
                recommendations.Add("Configure a cloud AI provider (OpenAI recommended for best results)");
            }
            else
            {
                // Model recommendations
                var openAiModel = _configuration["AI:Cloud:OpenAI:ModelId"];
                if (openAiModel == "gpt-3.5-turbo")
                {
                    recommendations.Add("Consider upgrading to gpt-4o-mini for better workflow analysis quality");
                }

                if (cloudProvider.ToLower() == "openai" && openAiModel == "gpt-4")
                {
                    recommendations.Add("Consider using gpt-4o-mini for better cost-effectiveness");
                }
            }

            // Performance recommendations
            var cacheEnabled = _configuration.GetValue<int>("Caching:Workflow:AnalysisCache:DefaultExpiration", 3600) > 0;
            if (!cacheEnabled)
            {
                recommendations.Add("Enable result caching to reduce AI API costs and improve response times");
            }

            // Security recommendations
            var apiKey = _configuration["AI:Cloud:OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                recommendations.Add("Configure your OpenAI API key to enable workflow analysis");
            }
            else if (apiKey == "sk-your-openai-api-key-here")
            {
                recommendations.Add("Replace the placeholder OpenAI API key with your actual key");
            }

            var maskKeys = _configuration.GetValue<bool>("Security:MaskApiKeysInLogs", true);
            if (!maskKeys)
            {
                recommendations.Add("Enable API key masking in logs for better security");
            }

            // Cloud-only setup benefits
            recommendations.Add("Cloud-only setup provides faster, more consistent results with easier deployment");
            recommendations.Add("Programmatic SQL generation eliminates local AI infrastructure requirements");

            return recommendations;
        }

        #region Private Validation Methods

        private void ValidateOpenAIConfig(ValidationResult result)
        {
            var apiKey = _configuration["AI:Cloud:OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                result.AddError("OpenAI API key not configured - this is required for cloud-only setup");
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

        private void ValidateProgrammaticSqlConfig(ValidationResult result)
        {
            // Programmatic SQL generation is always available
            // Just validate that schema file is accessible for generation
            var schemaFile = _configuration["WorkflowSchema:SchemaFile"] ?? "workflow-schema.sql";
            var schemaPath = Path.Combine(Directory.GetCurrentDirectory(), schemaFile);

            if (!File.Exists(schemaPath))
            {
                result.AddWarning($"Schema file not found - may affect SQL generation quality: {schemaPath}");
            }

            // Check programmatic generation settings
            var includeComments = _configuration.GetValue<bool>("WorkflowSchema:IncludeComments", true);
            if (!includeComments)
            {
                result.AddWarning("SQL comments are disabled - consider enabling for better readability");
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

            // Check cloud-only specific settings
            var enableCloudAI = _configuration.GetValue<bool>("Features:EnableCloudAI", true);
            if (!enableCloudAI)
            {
                result.AddError("Cloud AI is disabled but required for this setup");
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