using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text;
using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    public class SemanticKernelSqlService : ISqlGenerationService
    {
        private readonly Kernel _sqlKernel;
        private readonly ILogger<SemanticKernelSqlService> _logger;
        private readonly IConfiguration _configuration;

        public SemanticKernelSqlService(
            [FromKeyedServices("sql")] Kernel sqlKernel,
            ILogger<SemanticKernelSqlService> logger,
            IConfiguration configuration)
        {
            _sqlKernel = sqlKernel;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<SqlGenerationResult> GenerateSqlAsync(string systemPrompt, string userPrompt)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Starting SQL generation using Semantic Kernel");

                var sqlFunction = _sqlKernel.CreateFunctionFromPrompt(
                    promptTemplate: """
                    {{$systemPrompt}}
                    
                    {{$userPrompt}}
                    
                    SQL Response:
                    """,
                    functionName: "GenerateWorkflowSql",
                    description: "Generates SQL INSERT statements for PAWS workflow systems"
                );

                var result = await _sqlKernel.InvokeAsync(sqlFunction, new KernelArguments
                {
                    ["systemPrompt"] = systemPrompt,
                    ["userPrompt"] = userPrompt
                });

                stopwatch.Stop();

                var sqlResponse = result.GetValue<string>();

                if (string.IsNullOrEmpty(sqlResponse))
                {
                    _logger.LogError("Received empty response from SQL generation kernel");
                    return new SqlGenerationResult
                    {
                        Success = false,
                        ErrorMessage = "Empty response from SQL generation service",
                        GenerationTimeMs = stopwatch.ElapsedMilliseconds,
                        ModelUsed = GetCurrentModelId()
                    };
                }

                var cleanedSql = ExtractSqlFromResponse(sqlResponse);
                var warnings = ValidateSqlOutput(cleanedSql);

                _logger.LogInformation("SQL generation completed successfully in {ms}ms", stopwatch.ElapsedMilliseconds);

                return new SqlGenerationResult
                {
                    Success = true,
                    GeneratedSql = cleanedSql,
                    TokensUsed = EstimateTokenCount(sqlResponse),
                    GenerationTimeMs = stopwatch.ElapsedMilliseconds,
                    ModelUsed = GetCurrentModelId(),
                    Warnings = warnings,
                    Metadata = new Dictionary<string, object>
                    {
                        ["OriginalResponseLength"] = sqlResponse.Length,
                        ["CleanedSqlLength"] = cleanedSql.Length,
                        ["HasWarnings"] = warnings.Any(),
                        ["Provider"] = _configuration["AI:Local:Provider"] ?? "Unknown"
                    },
                    SystemPrompt = systemPrompt,
                    UserPrompt = userPrompt
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generating SQL with Semantic Kernel after {ms}ms", stopwatch.ElapsedMilliseconds);

                return new SqlGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"SQL generation failed: {ex.Message}",
                    GenerationTimeMs = stopwatch.ElapsedMilliseconds,
                    ModelUsed = GetCurrentModelId(),
                    Metadata = new Dictionary<string, object>
                    {
                        ["ErrorType"] = ex.GetType().Name,
                        ["Provider"] = _configuration["AI:Local:Provider"] ?? "Unknown"
                    },
                    SystemPrompt = systemPrompt,
                    UserPrompt = userPrompt 
                };
            }
        }

        public async Task<SqlGenerationResult> GenerateEnhancedSqlAsync(
            WorkflowAnalysisResult analysisResult,
            string schemaContext)
        {
            if (!analysisResult.Success)
            {
                return new SqlGenerationResult
                {
                    Success = false,
                    ErrorMessage = "Cannot generate SQL from failed analysis result"
                };
            }

            var systemPrompt = BuildEnhancedSystemPrompt(schemaContext);
            var userPrompt = BuildEnhancedPrompt(analysisResult, schemaContext);

            return await GenerateSqlAsync(systemPrompt, userPrompt);
        }

        public async Task<bool> TestLocalConnectionAsync()
        {
            try
            {
                var testFunction = _sqlKernel.CreateFunctionFromPrompt(
                    "-- Connection test\nSELECT 1 AS test_connection;\n-- Please respond with a simple SQL comment about the connection",
                    functionName: "TestSqlConnection"
                );

                var result = await _sqlKernel.InvokeAsync(testFunction);
                var response = result.GetValue<string>();

                var isConnected = !string.IsNullOrEmpty(response);
                _logger.LogInformation("Local AI connection test result: {result}", isConnected ? "Success" : "Failed");

                return isConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local AI connection test failed");
                return false;
            }
        }

        public async Task<List<LocalModelInfo>> GetAvailableModelsAsync()
        {
            try
            {
                // For Ollama, we would make an API call to get available models
                // This is a simplified implementation
                var models = new List<LocalModelInfo>
                {
                    new()
                    {
                        Name = "codellama:7b",
                        Description = "Code Llama 7B - Specialized for code generation",
                        SizeBytes = 3800000000, // ~3.8GB
                        IsAvailable = true,
                        Capabilities = new[] { "sql", "code-generation", "programming" },
                        Parameters = new Dictionary<string, object>
                        {
                            ["context_length"] = 4096,
                            ["recommended_temperature"] = 0.1
                        }
                    },
                    new()
                    {
                        Name = "codellama:13b",
                        Description = "Code Llama 13B - Larger model for complex code generation",
                        SizeBytes = 7300000000, // ~7.3GB
                        IsAvailable = false, // Would check if actually downloaded
                        Capabilities = new[] { "sql", "code-generation", "programming", "complex-logic" },
                        Parameters = new Dictionary<string, object>
                        {
                            ["context_length"] = 4096,
                            ["recommended_temperature"] = 0.1
                        }
                    }
                };

                return models;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available local models");
                return new List<LocalModelInfo>();
            }
        }

        #region Helper Methods

        private string ExtractSqlFromResponse(string response)
        {
            var lines = response.Split('\n');
            var sqlLines = new List<string>();
            bool inSqlBlock = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("```"))
                {
                    inSqlBlock = !inSqlBlock;
                    continue;
                }

                if (inSqlBlock ||
                    trimmedLine.StartsWith("--") ||
                    trimmedLine.StartsWith("/*") ||
                    trimmedLine.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("SET", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("GO", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.Contains("(") ||
                    trimmedLine.Contains(")") ||
                    trimmedLine.Contains(";"))
                {
                    sqlLines.Add(line);
                }
                else if (sqlLines.Count > 0 && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    // Include continuation lines that are indented
                    if (line.StartsWith(" ") || line.StartsWith("\t"))
                    {
                        sqlLines.Add(line);
                    }
                }
            }

            return string.Join("\n", sqlLines);
        }

        private List<string> ValidateSqlOutput(string sql)
        {
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(sql))
            {
                warnings.Add("Generated SQL is empty");
                return warnings;
            }

            if (!sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("No INSERT statements found in generated SQL");
            }

            if (!sql.Contains("PAWSProcessTemplate", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("No PAWSProcessTemplate table references found");
            }

            if (!sql.Contains("NEWID()", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("No NEWID() calls found for uniqueidentifier columns");
            }

            var insertCount = CountOccurrences(sql, "INSERT", StringComparison.OrdinalIgnoreCase);
            if (insertCount < 3)
            {
                warnings.Add($"Only {insertCount} INSERT statements found, expected at least 3 for complete workflow");
            }

            return warnings;
        }

        private string BuildEnhancedPrompt(WorkflowAnalysisResult analysisResult, string schemaContext)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Generate SQL INSERT statements for this workflow:");
            prompt.AppendLine();
            prompt.AppendLine($"WORKFLOW NAME: {analysisResult.WorkflowName}");
            prompt.AppendLine();

            if (analysisResult.Steps != null && analysisResult.Steps.Any())
            {
                prompt.AppendLine("WORKFLOW STEPS:");
                foreach (var step in analysisResult.Steps.OrderBy(s => s.Order))
                {
                    prompt.AppendLine($"{step.Order}. {step.Title}");
                    prompt.AppendLine($"   Description: {step.Description}");
                    //if (!string.IsNullOrEmpty(step.AssignedRole))
                    //    prompt.AppendLine($"   Role: {step.AssignedRole}");
                    if (step.PossibleOutcomes != null && step.PossibleOutcomes.Any())
                        prompt.AppendLine($"   Outcomes: {string.Join(", ", step.PossibleOutcomes)}");
                    prompt.AppendLine();
                }
            }

            if (analysisResult.RequiredStatuses != null && analysisResult.RequiredStatuses.Any())
            {
                prompt.AppendLine("REQUIRED STATUSES:");
                var existingStatuses = analysisResult.RequiredStatuses.Where(s => s.IsExisting).ToList();
                var newStatuses = analysisResult.RequiredStatuses.Where(s => !s.IsExisting).ToList();

                if (existingStatuses.Any())
                {
                    prompt.AppendLine("These are pre-existing statuses which you do not need generate INSERT statements for but the IDs will be used in the transitions:");
                    foreach (var status in existingStatuses)
                    {
                        prompt.AppendLine($"- {status.Name} (ID: {status.ExistingId})");
                    }
                }

                if (newStatuses.Any())
                {
                    prompt.AppendLine("These are new statuses that required INSERT statements in the PAWSActivityStatus table.  The IDs will also be used in the activity transitions:");
                    foreach (var status in newStatuses)
                    {
                        prompt.AppendLine($"- {status.Name}: {status.Description}");
                    }
                }
            }

            prompt.AppendLine();
            prompt.AppendLine("SPECIAL REQUIREMENTS:");
            prompt.AppendLine("1. Create the initial NULL->Step1 transition with TriggerStatusID=1");
            prompt.AppendLine("2. Define all forward paths with TransitionType=1");
            prompt.AppendLine("3. Define any rejection/rework paths with TransitionType=2");
            prompt.AppendLine("4. Mark terminal transitions with DestinationActivityID=NULL");
            prompt.AppendLine("5. Use a single GUID variable for processTemplateID consistency");
            prompt.AppendLine();
            prompt.AppendLine("Generate complete SQL following all rules. Start with DECLARE @processId uniqueidentifier = NEWID();");

            return prompt.ToString();
        }

        private string BuildEnhancedSystemPrompt(string schemaContext)
        {
            return $@"You are a SQL code generator for the PAWS workflow management system. Your task is to generate ONLY valid SQL INSERT statements that populate the workflow tables. You must follow strict rules to maintain data integrity and foreign key constraints.

CRITICAL REQUIREMENTS:
1. Generate ONLY SQL INSERT statements - no explanations, no DDL, no other text
2. Use SQL Server T-SQL syntax exclusively
3. Include SQL comments (--) to document each section
4. Follow the EXACT table insertion order to respect foreign key constraints
5. Every workflow MUST start with a NULL source transition

DATABASE SCHEMA OVERVIEW:
- PAWSProcessTemplate: Master workflow definition (uses uniqueidentifier)
- PAWSActivity: Individual workflow steps (uses sequential integers starting from 1)
- PAWSActivityStatus: Predefined trigger statuses (reference table with fixed IDs)
- PAWSActivityTransition: Defines flow between activities (the workflow logic)

DATABASE SCHEMA:
{schemaContext}

INSERTION ORDER (MANDATORY):
1. PAWSProcessTemplate (one row per workflow)
2. PAWSActivity (one row per step)
3. PAWSActivityTransition (multiple rows defining the flow)

PAWSProcessTemplate RULES:
- processTemplateID: Always use NEWID()
- title: Workflow name (max 100 chars)
- IsArchived: Always 0 for new workflows
- ProcessSeed: Default 0
- ReassignEnabled: Default 0
- ReassignCapabilityID: Default NULL

PAWSActivity RULES:
- activityID: Sequential integers starting from 1
- title: Step name (max 100 chars)
- description: Step description (max 500 chars)
- processTemplateID: Must match the NEWID() from PAWSProcessTemplate
- DefaultOwnerRoleID: NULL or valid GUID
- IsRemoved: Always 0 for active steps
- SignoffText: NULL unless approval required
- ShowSignoffText: 0 unless displaying approval text
- RequirePassword: 0 unless password required

PAWSActivityTransition RULES (CRITICAL):
- FIRST ROW RULE: Every workflow MUST have an initial transition with:
  * SourceActivityID = NULL (workflow entry point)
  * DestinationActivityID = 1 (first step)
  * TriggerStatusID = 1 (Pending status)
  * TransitionType = 1 (forward)
- TERMINATION RULE: Final steps have DestinationActivityID = NULL
- FORWARD TRANSITIONS: TransitionType = 1 (moving to next step)
- BACKWARD TRANSITIONS: TransitionType = 2 (rejection/rework scenarios)
- Operator: Default 0
- IsCommentRequired: 0 or 1 based on business needs
- DestinationOwnerRequired: Default 0

STANDARD STATUS IDs (PAWSActivityStatus):
1 = Pending (ALWAYS used for workflow start)
2 = Submitted for Approval
3 = Approve
7 = Reject
17 = Submit Draft
18 = Submit for Review
19 = Review and Close

WORKFLOW PATTERNS:
- Linear: Step1 -> Step2 -> Step3 -> End
- Approval: Step -> (Approved->Next) OR (Rejected->Previous)
- Multi-level: Each level can approve forward or reject backward

OUTPUT FORMAT:
-- Always start with a header comment
-- Group INSERTs by table
-- Use consistent formatting
-- Include inline comments for complex transitions";
        }

        private string GetCurrentModelId()
        {
            return _configuration["AI:Local:ModelId"] ?? "Unknown";
        }

        private int EstimateTokenCount(string text)
        {
            // Rough estimation: 1 token ≈ 4 characters
            return text.Length / 4;
        }

        private int CountOccurrences(string text, string searchString, StringComparison comparison)
        {
            int count = 0;
            int index = 0;

            while ((index = text.IndexOf(searchString, index, comparison)) != -1)
            {
                count++;
                index += searchString.Length;
            }

            return count;
        }

        #endregion
    }
}
