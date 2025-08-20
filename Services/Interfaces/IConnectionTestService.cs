using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Service for testing connectivity to AI services.
    /// Provides health checks and diagnostics for both cloud and local AI services.
    /// </summary>
    public interface IConnectionTestService
    {
        /// <summary>
        /// Tests connection to the cloud AI service (used for workflow analysis).
        /// Performs actual API call to verify service availability.
        /// </summary>
        /// <returns>Connection status and diagnostic information</returns>
        Task<ConnectionTestResult> TestCloudConnectionAsync();

        /// <summary>
        /// Tests connection to the local AI service (used for SQL generation).
        /// Verifies local service availability and model accessibility.
        /// </summary>
        /// <returns>Connection status and diagnostic information</returns>
        Task<ConnectionTestResult> TestLocalConnectionAsync();

        /// <summary>
        /// Tests all configured AI services simultaneously.
        /// Provides comprehensive health check of the entire AI system.
        /// </summary>
        /// <returns>Overall system health and individual service status</returns>
        Task<SystemHealthResult> TestAllConnectionsAsync();

        /// <summary>
        /// Performs a comprehensive diagnostic of AI services.
        /// Includes connection tests, model availability, and performance metrics.
        /// </summary>
        /// <returns>Detailed diagnostic information</returns>
        Task<AISystemDiagnostics> PerformSystemDiagnosticsAsync();

        /// <summary>
        /// Gets current configuration summary for display to users.
        /// Shows which providers and models are configured.
        /// </summary>
        /// <returns>Human-readable configuration summary</returns>
        Task<AIConfigurationSummary> GetConfigurationSummaryAsync();
    }
}
