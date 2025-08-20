using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Service responsible for AI-powered workflow analysis using cloud-based AI models.
    /// Handles natural language understanding and extracts structured workflow information.
    /// </summary>
    public interface IWorkflowAnalysisService
    {
        /// <summary>
        /// Analyzes a workflow description using AI to extract structured information.
        /// This performs multiple AI operations:
        /// 1. Extract workflow name
        /// 2. Analyze and structure workflow steps
        /// 3. Determine required statuses and map to existing ones
        /// </summary>
        /// <param name="description">Natural language workflow description</param>
        /// <returns>Structured analysis result with workflow components</returns>
        Task<WorkflowAnalysisResult> AnalyzeWorkflowAsync(string description);

        /// <summary>
        /// Tests connectivity to the cloud AI service used for workflow analysis.
        /// Used for health checks and connection validation.
        /// </summary>
        /// <returns>True if cloud AI service is accessible and responding</returns>
        Task<bool> TestCloudConnectionAsync();

        /// <summary>
        /// Gets the current configuration information about the cloud AI service.
        /// Useful for diagnostics and displaying current setup to users.
        /// </summary>
        /// <returns>Configuration details (provider, model, endpoint info)</returns>
        Task<AIServiceInfo> GetCloudServiceInfoAsync();
    }
}
