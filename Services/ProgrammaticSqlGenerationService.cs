using System.Text;
using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Programmatic SQL generation service that creates INSERT statements
    /// based on analyzed workflow data without requiring a local LLM.
    /// </summary>
    public class ProgrammaticSqlGenerationService : ISqlGenerationService
    {
        private readonly ILogger<ProgrammaticSqlGenerationService> _logger;
        private readonly IConfiguration _configuration;

        public ProgrammaticSqlGenerationService(
            ILogger<ProgrammaticSqlGenerationService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<SqlGenerationResult> GenerateSqlAsync(string systemPrompt, string userPrompt)
        {
            // This method is kept for interface compatibility but not used in the new approach
            return await Task.FromResult(new SqlGenerationResult
            {
                Success = false,
                ErrorMessage = "Use GenerateEnhancedSqlAsync method for programmatic SQL generation"
            });
        }

        public async Task<SqlGenerationResult> GenerateEnhancedSqlAsync(
            WorkflowAnalysisResult analysisResult,
            string schemaContext)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Starting programmatic SQL generation for workflow: {workflowName}",
                    analysisResult.WorkflowName);

                if (!analysisResult.Success)
                {
                    return new SqlGenerationResult
                    {
                        Success = false,
                        ErrorMessage = "Cannot generate SQL from failed analysis result",
                        GenerationTimeMs = stopwatch.ElapsedMilliseconds,
                        ModelUsed = "Programmatic Generator"
                    };
                }

                var sql = new StringBuilder();
                var warnings = new List<string>();

                // Generate SQL header
                GenerateSqlHeader(sql, analysisResult);

                // Generate ProcessTemplate INSERT
                GenerateProcessTemplateInsert(sql, analysisResult);

                // Generate Activity INSERTs
                GenerateActivityInserts(sql, analysisResult, warnings);

                // Generate ActivityStatus INSERTs for new statuses
                GenerateActivityStatusInserts(sql, analysisResult, warnings);

                // Generate ActivityTransition INSERTs
                GenerateActivityTransitionInserts(sql, analysisResult, warnings);

                stopwatch.Stop();

                var result = new SqlGenerationResult
                {
                    Success = true,
                    GeneratedSql = sql.ToString(),
                    GenerationTimeMs = stopwatch.ElapsedMilliseconds,
                    ModelUsed = "Programmatic Generator v1.0",
                    Warnings = warnings,
                    Metadata = new Dictionary<string, object>
                    {
                        ["GeneratedLines"] = sql.ToString().Split('\n').Length,
                        ["ProcessTemplateCount"] = 1,
                        ["ActivityCount"] = analysisResult.Steps?.Count ?? 0,
                        ["NewStatusCount"] = analysisResult.RequiredStatuses?.Count ?? 0,
                        ["TransitionCount"] = analysisResult.FlowTransitions?.Count ?? 0,
                        ["GenerationMethod"] = "Programmatic"
                    }
                };

                _logger.LogInformation("Programmatic SQL generation completed in {ms}ms with {warnings} warnings",
                    stopwatch.ElapsedMilliseconds, warnings.Count);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error in programmatic SQL generation");

                return new SqlGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"SQL generation failed: {ex.Message}",
                    GenerationTimeMs = stopwatch.ElapsedMilliseconds,
                    ModelUsed = "Programmatic Generator",
                    Metadata = new Dictionary<string, object>
                    {
                        ["ErrorType"] = ex.GetType().Name,
                        ["GenerationMethod"] = "Programmatic"
                    }
                };
            }
        }

        public async Task<bool> TestLocalConnectionAsync()
        {
            // No local AI service to test - always return true for programmatic generation
            await Task.CompletedTask;
            _logger.LogInformation("Programmatic SQL generation is always available");
            return true;
        }

        public async Task<List<LocalModelInfo>> GetAvailableModelsAsync()
        {
            // Return information about the programmatic generator
            await Task.CompletedTask;

            return new List<LocalModelInfo>
            {
                new LocalModelInfo
                {
                    Name = "Programmatic Generator",
                    Description = "Built-in programmatic SQL generation engine",
                    SizeBytes = 0, // No model file
                    IsAvailable = true,
                    Capabilities = new[] { "sql-generation", "workflow-sql", "paws-system" },
                    Parameters = new Dictionary<string, object>
                    {
                        ["generation_method"] = "programmatic",
                        ["response_time"] = "instant",
                        ["accuracy"] = "100%"
                    }
                }
            };
        }

        #region SQL Generation Methods

        private void GenerateSqlHeader(StringBuilder sql, WorkflowAnalysisResult analysisResult)
        {
            sql.AppendLine("-- ================================================");
            sql.AppendLine("-- AI-GENERATED WORKFLOW SQL");
            sql.AppendLine("-- ================================================");
            sql.AppendLine($"-- Workflow Name: {analysisResult.WorkflowName}");
            sql.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sql.AppendLine($"-- Steps: {analysisResult.Steps?.Count ?? 0}");
            sql.AppendLine($"-- Transitions: {analysisResult.FlowTransitions?.Count ?? 0}");
            sql.AppendLine($"-- New Statuses: {analysisResult.RequiredStatuses?.Count ?? 0}");
            sql.AppendLine("-- Generation Method: Programmatic (AI Analysis + Code Generation)");
            sql.AppendLine("-- ================================================");
            sql.AppendLine();
            sql.AppendLine("DECLARE @processId uniqueidentifier = NEWID();");
            sql.AppendLine();
        }

        private void GenerateProcessTemplateInsert(StringBuilder sql, WorkflowAnalysisResult analysisResult)
        {
            sql.AppendLine("-- ================================================");
            sql.AppendLine("-- 1. WORKFLOW PROCESS TEMPLATE");
            sql.AppendLine("-- ================================================");
            sql.AppendLine();

            sql.AppendLine("INSERT INTO [paws].[PAWSProcessTemplate] (");
            sql.AppendLine("    [processTemplateID],");
            sql.AppendLine("    [title],");
            sql.AppendLine("    [IsArchived],");
            sql.AppendLine("    [ProcessSeed],");
            sql.AppendLine("    [ReassignEnabled],");
            sql.AppendLine("    [ReassignCapabilityID]");
            sql.AppendLine(") VALUES (");
            sql.AppendLine("    @processId,");
            sql.AppendLine($"    '{EscapeSqlString(analysisResult.WorkflowName ?? "Unknown Workflow")}',");
            sql.AppendLine("    0,");
            sql.AppendLine("    0,");
            sql.AppendLine("    0,");
            sql.AppendLine("    NULL");
            sql.AppendLine(");");
            sql.AppendLine();
        }

        private void GenerateActivityInserts(StringBuilder sql, WorkflowAnalysisResult analysisResult, List<string> warnings)
        {
            if (analysisResult.Steps == null || !analysisResult.Steps.Any())
            {
                warnings.Add("No workflow steps found - no activities will be generated");
                return;
            }

            sql.AppendLine("-- ================================================");
            sql.AppendLine("-- 2. WORKFLOW ACTIVITIES (STEPS)");
            sql.AppendLine("-- ================================================");
            sql.AppendLine();

            var sortedSteps = analysisResult.Steps.OrderBy(s => s.Order).ToList();

            foreach (var step in sortedSteps)
            {
                sql.AppendLine($"-- Activity {step.Order}: {step.Title}");
                sql.AppendLine("INSERT INTO [paws].[PAWSActivity] (");
                sql.AppendLine("    [activityID],");
                sql.AppendLine("    [title],");
                sql.AppendLine("    [description],");
                sql.AppendLine("    [processTemplateID],");
                sql.AppendLine("    [DefaultOwnerRoleID],");
                sql.AppendLine("    [IsRemoved],");
                sql.AppendLine("    [SignoffText],");
                sql.AppendLine("    [ShowSignoffText],");
                sql.AppendLine("    [RequirePassword]");
                sql.AppendLine(") VALUES (");
                sql.AppendLine($"    ISNULL((SELECT MAX(activityID) FROM [paws].[PAWSActivity]), 0) + {step.Order},");
                sql.AppendLine($"    '{EscapeSqlString(step.Title)}',");
                sql.AppendLine($"    '{EscapeSqlString(step.Description)}',");
                sql.AppendLine("    @processId,");
                sql.AppendLine("    NULL,");
                sql.AppendLine("    0,");
                sql.AppendLine("    NULL,");
                sql.AppendLine("    0,");
                sql.AppendLine("    0");
                sql.AppendLine(");");
                sql.AppendLine();
            }
        }

        private void GenerateActivityStatusInserts(StringBuilder sql, WorkflowAnalysisResult analysisResult, List<string> warnings)
        {
            if (analysisResult.RequiredStatuses == null || !analysisResult.RequiredStatuses.Any())
            {
                sql.AppendLine("-- ================================================");
                sql.AppendLine("-- 3. NEW ACTIVITY STATUSES");
                sql.AppendLine("-- ================================================");
                sql.AppendLine("-- No new statuses required - using existing PAWS statuses");
                sql.AppendLine();
                return;
            }

            sql.AppendLine("-- ================================================");
            sql.AppendLine("-- 3. NEW ACTIVITY STATUSES");
            sql.AppendLine("-- ================================================");
            sql.AppendLine("-- These statuses need to be added to PAWSActivityStatus");
            sql.AppendLine();

            foreach (var status in analysisResult.RequiredStatuses)
            {
                sql.AppendLine($"-- New Status: {status.Name}");
                sql.AppendLine("INSERT INTO [paws].[PAWSActivityStatus] (");
                sql.AppendLine("    [ActivityStatusID],");
                sql.AppendLine("    [title]");
                sql.AppendLine(") VALUES (");
                sql.AppendLine("    ISNULL((SELECT MAX(ActivityStatusID) FROM [paws].[PAWSActivityStatus]), 0) + 1,");
                sql.AppendLine($"    '{EscapeSqlString(status.Name)}'");
                sql.AppendLine(");");
                sql.AppendLine();
            }
        }

        private void GenerateActivityTransitionInserts(StringBuilder sql, WorkflowAnalysisResult analysisResult, List<string> warnings)
        {
            if (analysisResult.FlowTransitions == null || !analysisResult.FlowTransitions.Any())
            {
                warnings.Add("No workflow transitions found - workflow may not function properly");
                return;
            }

            sql.AppendLine("-- ================================================");
            sql.AppendLine("-- 4. WORKFLOW TRANSITIONS (FLOW LOGIC)");
            sql.AppendLine("-- ================================================");
            sql.AppendLine("-- These define how the workflow moves between steps");
            sql.AppendLine();

            foreach (var transition in analysisResult.FlowTransitions)
            {
                var sourceDesc = transition.SourceStep ?? "WORKFLOW START";
                var destDesc = transition.DestinationStep ?? "WORKFLOW END";
                var transitionType = transition.IsProgressive ? 1 : 2; // 1=Forward, 2=Backward

                sql.AppendLine($"-- Transition: {sourceDesc} → [{transition.TriggerStatus}] → {destDesc}");
                sql.AppendLine("INSERT INTO [paws].[PAWSActivityTransition] (");
                sql.AppendLine("    [SourceActivityID],");
                sql.AppendLine("    [DestinationActivityID],");
                sql.AppendLine("    [TriggerStatusID],");
                sql.AppendLine("    [Operator],");
                sql.AppendLine("    [TransitionType],");
                sql.AppendLine("    [IsCommentRequired],");
                sql.AppendLine("    [DestinationOwnerRequired],");
                sql.AppendLine("    [DestinationTransitionGroup],");
                sql.AppendLine("    [MUTHandler],");
                sql.AppendLine("    [MUTTags]");
                sql.AppendLine(") VALUES (");

                // Source Activity ID
                if (transition.SourceStep == null)
                {
                    sql.AppendLine("    NULL, -- Workflow start");
                }
                else
                {
                    sql.AppendLine($"    (SELECT activityID FROM [paws].[PAWSActivity] WHERE processTemplateID = @processId AND title = '{EscapeSqlString(transition.SourceStep)}'),");
                }

                // Destination Activity ID
                if (transition.DestinationStep == null)
                {
                    sql.AppendLine("    NULL, -- Workflow end");
                }
                else
                {
                    sql.AppendLine($"    (SELECT activityID FROM [paws].[PAWSActivity] WHERE processTemplateID = @processId AND title = '{EscapeSqlString(transition.DestinationStep)}'),");
                }

                // Trigger Status ID - try to map to existing or new status
                var statusId = FindStatusId(transition.TriggerStatus, analysisResult);
                if (statusId.HasValue)
                {
                    sql.AppendLine($"    {statusId.Value}, -- {transition.TriggerStatus}");
                }
                else
                {
                    sql.AppendLine($"    (SELECT ActivityStatusID FROM [paws].[PAWSActivityStatus] WHERE title = '{EscapeSqlString(transition.TriggerStatus)}'), -- {transition.TriggerStatus}");
                    warnings.Add($"Could not map trigger status '{transition.TriggerStatus}' to existing ID - using lookup");
                }

                sql.AppendLine("    0, -- Default operator");
                sql.AppendLine($"    {transitionType}, -- {(transition.IsProgressive ? "Forward" : "Backward")}");
                sql.AppendLine("    0, -- Comment not required");
                sql.AppendLine("    0, -- Owner assignment not required");
                sql.AppendLine("    NULL, -- No destination group");
                sql.AppendLine("    NULL, -- No custom handler");
                sql.AppendLine("    NULL  -- No tags");
                sql.AppendLine(");");
                sql.AppendLine();
            }
        }

        private int? FindStatusId(string triggerStatus, WorkflowAnalysisResult analysisResult)
        {
            // First check existing statuses
            var existingStatus = analysisResult.ExistingStatuses?
                .FirstOrDefault(s => string.Equals(s.Name, triggerStatus, StringComparison.OrdinalIgnoreCase));

            if (existingStatus != null)
                return existingStatus.ExistingId;

            // If it's in required statuses, we can't predict the ID since it will be auto-generated
            var requiredStatus = analysisResult.RequiredStatuses?
                .FirstOrDefault(s => string.Equals(s.Name, triggerStatus, StringComparison.OrdinalIgnoreCase));

            if (requiredStatus != null)
                return null; // Will use lookup in SQL

            // Try common status mappings as fallback
            return triggerStatus.ToLowerInvariant() switch
            {
                "pending" => 1,
                "submit for approval" => 2,
                "approve" => 3,
                "reject" => 7,
                "submit draft" => 17,
                "submit for review" => 18,
                "review and close" => 19,
                _ => null
            };
        }

        private string EscapeSqlString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input.Replace("'", "''").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        }

        #endregion
    }
}