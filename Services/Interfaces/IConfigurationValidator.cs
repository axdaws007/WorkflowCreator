using WorkflowCreator.Models;

namespace WorkflowCreator.Services.Interfaces
{
    /// <summary>
    /// Service for validating application configuration on startup.
    /// Provides comprehensive checking of AI service configurations, API keys, and dependencies.
    /// </summary>
    public interface IConfigurationValidator
    {
        /// <summary>
        /// Validates the complete application configuration.
        /// Checks AI provider settings, API keys, file paths, and other critical configuration.
        /// </summary>
        /// <returns>Validation result containing any errors or warnings found</returns>
        ValidationResult ValidateConfiguration();

        /// <summary>
        /// Validates specifically the AI service configuration.
        /// Checks cloud and local AI provider settings for completeness and validity.
        /// </summary>
        /// <returns>AI-specific validation result</returns>
        ValidationResult ValidateAIConfiguration();

        /// <summary>
        /// Validates the workflow schema configuration.
        /// Checks that schema files exist and are properly configured.
        /// </summary>
        /// <returns>Schema-specific validation result</returns>
        ValidationResult ValidateSchemaConfiguration();

        /// <summary>
        /// Gets configuration recommendations based on current setup.
        /// Provides suggestions for optimizing the configuration.
        /// </summary>
        /// <returns>List of configuration recommendations</returns>
        List<string> GetConfigurationRecommendations();
    }
}
