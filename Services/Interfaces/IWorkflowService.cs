using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Main workflow service that orchestrates the complete workflow creation process.
    /// Combines AI analysis and SQL generation to provide end-to-end workflow processing.
    /// </summary>
    public interface IWorkflowService
    {
        /// <summary>
        /// Processes a workflow description end-to-end using AI services.
        /// Performs analysis and generates SQL in a coordinated workflow.
        /// </summary>
        /// <param name="description">Natural language workflow description</param>
        /// <returns>Complete workflow processing result with generated SQL</returns>
        Task<WorkflowResultViewModel> ProcessWorkflowDescriptionAsync(string description);

        /// <summary>
        /// Gets all workflows stored in the system.
        /// </summary>
        /// <returns>List of all workflow models</returns>
        Task<List<WorkflowModel>> GetAllWorkflowsAsync();

        /// <summary>
        /// Gets workflow by ID.
        /// </summary>
        /// <param name="id">Workflow ID</param>
        /// <returns>Workflow model or null if not found</returns>
        Task<WorkflowModel?> GetWorkflowByIdAsync(int id);

        /// <summary>
        /// Saves a workflow to the system.
        /// In production, this would persist to a database.
        /// </summary>
        /// <param name="workflow">Workflow to save</param>
        /// <returns>Saved workflow with assigned ID</returns>
        Task<WorkflowModel> SaveWorkflowAsync(WorkflowModel workflow);

        /// <summary>
        /// Deletes a workflow from the system.
        /// </summary>
        /// <param name="id">Workflow ID to delete</param>
        /// <returns>True if deleted successfully</returns>
        Task<bool> DeleteWorkflowAsync(int id);

        /// <summary>
        /// Re-analyzes an existing workflow with current AI models.
        /// Useful for improving workflows as AI capabilities advance.
        /// </summary>
        /// <param name="workflowId">ID of workflow to re-analyze</param>
        /// <returns>Updated workflow with fresh AI analysis</returns>
        Task<WorkflowResultViewModel> ReAnalyzeWorkflowAsync(int workflowId);

        /// <summary>
        /// Searches workflows by name or description.
        /// </summary>
        /// <param name="searchTerm">Search term to match against name or description</param>
        /// <returns>Matching workflows</returns>
        Task<List<WorkflowModel>> SearchWorkflowsAsync(string searchTerm);

        /// <summary>
        /// Gets workflows created within a date range.
        /// </summary>
        /// <param name="startDate">Start date (inclusive)</param>
        /// <param name="endDate">End date (inclusive)</param>
        /// <returns>Workflows created within the date range</returns>
        Task<List<WorkflowModel>> GetWorkflowsByDateRangeAsync(DateTime startDate, DateTime endDate);
    }
}
