using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Service responsible for SQL generation using local AI models.
    /// Specializes in code generation, particularly SQL INSERT statements for workflow systems.
    /// </summary>
    public interface ISqlGenerationService
    {
        /// <summary>
        /// Generates SQL INSERT statements based on analyzed workflow information.
        /// Uses specialized local AI models optimized for code generation.
        /// </summary>
        /// <param name="systemPrompt">System-level instructions for SQL generation context</param>
        /// <param name="userPrompt">User prompt containing analyzed workflow data and requirements</param>
        /// <returns>Generated SQL statements or error information</returns>
        Task<SqlGenerationResult> GenerateSqlAsync(string systemPrompt, string userPrompt);

        /// <summary>
        /// Generates SQL with enhanced context from workflow analysis.
        /// This overload provides structured input for better SQL generation.
        /// </summary>
        /// <param name="analysisResult">Structured workflow analysis result</param>
        /// <param name="schemaContext">Database schema information</param>
        /// <returns>Generated SQL statements with enhanced context</returns>
        Task<SqlGenerationResult> GenerateEnhancedSqlAsync(
            WorkflowAnalysisResult analysisResult,
            string schemaContext);

        /// <summary>
        /// Tests connectivity to the local AI service used for SQL generation.
        /// </summary>
        /// <returns>True if local AI service is accessible and responding</returns>
        Task<bool> TestLocalConnectionAsync();

        /// <summary>
        /// Gets information about available local models for SQL generation.
        /// Useful for model selection and diagnostics.
        /// </summary>
        /// <returns>Available models and their capabilities</returns>
        Task<List<LocalModelInfo>> GetAvailableModelsAsync();
    }
}
