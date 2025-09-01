using System.Diagnostics;
using System.Text;
using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Main workflow service implementation that orchestrates AI-powered workflow creation.
    /// Coordinates between analysis, SQL generation, and storage services.
    /// </summary>
    public class WorkflowService : IWorkflowService
    {
        private readonly IWorkflowAnalysisService _analysisService;
        private readonly ISqlGenerationService _sqlService;
        private readonly ILogger<WorkflowService> _logger;
        private readonly IConfiguration _configuration;

        // In-memory storage for demo purposes
        // In production, this would be replaced with database operations
        private static readonly List<WorkflowModel> _workflows = new();
        private static readonly object _lockObject = new();
        private static int _nextId = 1;

        public WorkflowService(
            IWorkflowAnalysisService analysisService,
            ISqlGenerationService sqlService,
            ILogger<WorkflowService> logger,
            IConfiguration configuration)
        {
            _analysisService = analysisService;
            _sqlService = sqlService;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<WorkflowResultViewModel> ProcessWorkflowDescriptionAsync(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return new WorkflowResultViewModel
                {
                    Success = false,
                    Message = "Workflow description cannot be empty"
                };
            }

            var overallStopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Starting workflow processing for description length: {length}", description.Length);

                // Phase 1: AI Analysis
                var analysisResult = await _analysisService.AnalyzeWorkflowAsync(description);

                if (!analysisResult.Success)
                {
                    _logger.LogError("Workflow analysis failed: {error}", analysisResult.ErrorMessage);
                    return new WorkflowResultViewModel
                    {
                        Success = false,
                        Message = $"Workflow analysis failed: {analysisResult.ErrorMessage}",
                        ResponseTimeMs = overallStopwatch.ElapsedMilliseconds
                    };
                }

                // Create workflow model
                var workflow = new WorkflowModel
                {
                    Id = GetNextId(),
                    Name = analysisResult.WorkflowName ?? ExtractBasicWorkflowName(description),
                    Description = description,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Analyzed"
                };

                // Phase 2: SQL Generation
                var sqlResult = await GenerateSqlForWorkflow(analysisResult);

                // Build result
                var result = new WorkflowResultViewModel
                {
                    Success = sqlResult.Success,
                    Workflow = workflow,
                    Steps = analysisResult.Steps?.Select(s => FormatStepForDisplay(s)).ToList(),
                    RequiredStatuses = analysisResult.RequiredStatuses,
                    ExistingStatuses = analysisResult.ExistingStatuses,
                    ResponseTimeMs = overallStopwatch.ElapsedMilliseconds
                };

                if (sqlResult.Success)
                {
                    result.GeneratedSql = sqlResult.GeneratedSql;
                    workflow.GeneratedSql = sqlResult.GeneratedSql;
                    workflow.Status = "Generated";
                    result.Message = "Workflow successfully processed and SQL generated!";

                    _logger.LogInformation("Workflow processing completed successfully for '{name}'", workflow.Name);
                }
                else
                {
                    result.Message = $"Analysis completed but SQL generation failed: {sqlResult.ErrorMessage}";
                    workflow.Status = "Analysis Complete";
                    _logger.LogWarning("SQL generation failed for workflow '{name}': {error}", workflow.Name, sqlResult.ErrorMessage);
                }

                // Add comprehensive metadata
                result.AnalysisMetadata = new Dictionary<string, object>
                {
                    ["WorkflowId"] = workflow.Id,
                    ["WorkflowName"] = workflow.Name,
                    ["StepCount"] = analysisResult.Steps?.Count ?? 0,
                    ["StatusCount"] = analysisResult.AllStatuses.Count,
                    ["NewStatusCount"] = analysisResult.NewStatusCount,
                    ["AnalysisProvider"] = analysisResult.AIProvider,
                    ["SqlProvider"] = _configuration["AI:Local:Provider"] ?? "Unknown",
                    ["WasCached"] = analysisResult.WasCached,
                    ["ProcessingTimeMs"] = overallStopwatch.ElapsedMilliseconds
                };

                // Save the workflow
                await SaveWorkflowAsync(workflow);

                overallStopwatch.Stop();
                result.ResponseTimeMs = overallStopwatch.ElapsedMilliseconds;

                return result;
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                _logger.LogError(ex, "Error during workflow processing");

                return new WorkflowResultViewModel
                {
                    Success = false,
                    Message = $"Unexpected error during workflow processing: {ex.Message}",
                    ResponseTimeMs = overallStopwatch.ElapsedMilliseconds
                };
            }
        }

        public async Task<List<WorkflowModel>> GetAllWorkflowsAsync()
        {
            await Task.CompletedTask; // Async for future database operations

            lock (_lockObject)
            {
                // Return copy to avoid external modifications
                return _workflows.ToList();
            }
        }

        public async Task<WorkflowModel?> GetWorkflowByIdAsync(int id)
        {
            await Task.CompletedTask; // Async for future database operations

            lock (_lockObject)
            {
                return _workflows.FirstOrDefault(w => w.Id == id);
            }
        }

        public async Task<WorkflowModel> SaveWorkflowAsync(WorkflowModel workflow)
        {
            await Task.CompletedTask; // Async for future database operations

            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow));

            lock (_lockObject)
            {
                if (workflow.Id == 0)
                {
                    // New workflow
                    workflow.Id = _nextId++;
                    workflow.CreatedAt = DateTime.UtcNow;
                    _workflows.Add(workflow);
                    _logger.LogInformation("Created new workflow with ID {id}: '{name}'", workflow.Id, workflow.Name);
                }
                else
                {
                    // Update existing workflow
                    var existingIndex = _workflows.FindIndex(w => w.Id == workflow.Id);
                    if (existingIndex >= 0)
                    {
                        _workflows[existingIndex] = workflow;
                        _logger.LogInformation("Updated workflow {id}: '{name}'", workflow.Id, workflow.Name);
                    }
                    else
                    {
                        // ID specified but doesn't exist, add as new
                        _workflows.Add(workflow);
                        _logger.LogInformation("Added workflow with specified ID {id}: '{name}'", workflow.Id, workflow.Name);
                    }
                }

                return workflow;
            }
        }

        public async Task<bool> DeleteWorkflowAsync(int id)
        {
            await Task.CompletedTask; // Async for future database operations

            lock (_lockObject)
            {
                var workflow = _workflows.FirstOrDefault(w => w.Id == id);
                if (workflow != null)
                {
                    _workflows.Remove(workflow);
                    _logger.LogInformation("Deleted workflow {id}: '{name}'", id, workflow.Name);
                    return true;
                }

                _logger.LogWarning("Attempted to delete non-existent workflow {id}", id);
                return false;
            }
        }

        public async Task<WorkflowResultViewModel> ReAnalyzeWorkflowAsync(int workflowId)
        {
            var workflow = await GetWorkflowByIdAsync(workflowId);
            if (workflow == null)
            {
                return new WorkflowResultViewModel
                {
                    Success = false,
                    Message = $"Workflow with ID {workflowId} not found"
                };
            }

            _logger.LogInformation("Re-analyzing workflow {id}: '{name}'", workflowId, workflow.Name);

            // Process the original description with current AI models
            var result = await ProcessWorkflowDescriptionAsync(workflow.Description);

            if (result.Success && result.Workflow != null)
            {
                // Update the existing workflow with new analysis
                workflow.Name = result.Workflow.Name;
                workflow.GeneratedSql = result.Workflow.GeneratedSql;
                workflow.Status = result.Workflow.Status;

                // Save updated workflow
                await SaveWorkflowAsync(workflow);

                // Update result to reference the original workflow ID
                result.Workflow = workflow;
                result.Message = "Workflow re-analyzed successfully with updated AI models!";

                _logger.LogInformation("Successfully re-analyzed workflow {id}", workflowId);
            }

            return result;
        }

        public async Task<List<WorkflowModel>> SearchWorkflowsAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return await GetAllWorkflowsAsync();

            var workflows = await GetAllWorkflowsAsync();
            var lowerSearchTerm = searchTerm.ToLowerInvariant();

            return workflows.Where(w =>
                (w.Name?.ToLowerInvariant().Contains(lowerSearchTerm) == true) ||
                (w.Description?.ToLowerInvariant().Contains(lowerSearchTerm) == true)
            ).ToList();
        }

        public async Task<List<WorkflowModel>> GetWorkflowsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var workflows = await GetAllWorkflowsAsync();

            return workflows.Where(w =>
                w.CreatedAt >= startDate &&
                w.CreatedAt <= endDate.AddDays(1) // Include entire end date
            ).OrderByDescending(w => w.CreatedAt).ToList();
        }

        #region Helper Methods

        private async Task<SqlGenerationResult> GenerateSqlForWorkflow(WorkflowAnalysisResult analysisResult)
        {
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(analysisResult);

            return await _sqlService.GenerateEnhancedSqlAsync(analysisResult, systemPrompt);
        }

        private string BuildSystemPrompt()
        {
            var schemaContent = GetWorkflowSchema();

            return $@"You are an expert SQL developer working with the PAWS workflow management system.
Generate ONLY SQL INSERT statements based on AI-analyzed workflow information.

RULES:
1. Generate ONLY INSERT statements, no DDL
2. Follow INSERT order: PAWSProcessTemplate → PAWSActivity → PAWSActivityTransition
3. Use NEWID() for uniqueidentifier columns
4. Number activities sequentially starting from 1
5. Use existing status IDs where provided
6. Add comments for new statuses that need manual creation

DATABASE SCHEMA:
{schemaContent}

Generate comprehensive INSERT statements with detailed comments.";
        }

        private string BuildUserPrompt(WorkflowAnalysisResult analysisResult)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine($"Generate SQL for workflow: {analysisResult.WorkflowName}");
            prompt.AppendLine();

            if (analysisResult.Steps != null && analysisResult.Steps.Any())
            {
                prompt.AppendLine("Steps:");
                foreach (var step in analysisResult.Steps.OrderBy(s => s.Order))
                {
                    prompt.AppendLine($"{step.Order}. {step.Title} - {step.Description}");
                }
                prompt.AppendLine();
            }

            if (analysisResult.AllStatuses.Any())
            {
                prompt.AppendLine("Status Mapping:");
                foreach (var status in analysisResult.AllStatuses)
                {
                    if (status.IsExisting)
                        prompt.AppendLine($"- {status.Name} (Use ID: {status.ExistingId})");
                    else
                        prompt.AppendLine($"- {status.Name} (NEW - needs manual creation)");
                }
            }

            return prompt.ToString();
        }

        private string GetWorkflowSchema()
        {
            if (_configuration.GetValue<bool>("WorkflowSchema:UseSchemaFile"))
            {
                var schemaFile = _configuration["WorkflowSchema:SchemaFile"] ?? "workflow-schema.sql";
                var schemaPath = Path.Combine(Directory.GetCurrentDirectory(), schemaFile);

                if (System.IO.File.Exists(schemaPath))
                {
                    return System.IO.File.ReadAllText(schemaPath);
                }
            }

            return @"-- PAWS workflow schema placeholder
-- Please ensure your actual schema file is configured correctly";
        }

        private int GetNextId()
        {
            lock (_lockObject)
            {
                return _nextId++;
            }
        }

        private string ExtractBasicWorkflowName(string description)
        {
            // Fallback name extraction if AI analysis fails
            var words = description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4);
            var name = string.Join(" ", words);
            return name.Length > 50 ? name.Substring(0, 47) + "..." : name;
        }

        private string FormatStepForDisplay(WorkflowStep step)
        {
            var display = $"{step.Order}. {step.Title}";
            if (!string.IsNullOrEmpty(step.Description))
                display += $": {step.Description}";
            return display;
        }

        #endregion
    }
}
