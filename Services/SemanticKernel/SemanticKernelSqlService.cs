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
                    
                    Generate comprehensive SQL INSERT statements following the system requirements.
                    
                    Requirements:
                    - Return ONLY valid SQL code with comments
                    - Use proper SQL Server syntax
                    - Include detailed comments explaining each section
                    - Follow the specified INSERT order and foreign key constraints
                    - Use NEWID() for uniqueidentifier columns
                    - Ensure all data is realistic and appropriate
                    
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
                    }
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
                    }
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

            var enhancedPrompt = BuildEnhancedPrompt(analysisResult, schemaContext);
            var systemPrompt = BuildEnhancedSystemPrompt(schemaContext);

            return await GenerateSqlAsync(systemPrompt, enhancedPrompt);
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
            prompt.AppendLine("Generate SQL INSERT statements for this AI-analyzed workflow:");
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
                    if (!string.IsNullOrEmpty(step.AssignedRole))
                        prompt.AppendLine($"   Role: {step.AssignedRole}");
                    if (step.PossibleOutcomes != null && step.PossibleOutcomes.Any())
                        prompt.AppendLine($"   Outcomes: {string.Join(", ", step.PossibleOutcomes)}");
                    prompt.AppendLine();
                }
            }

            if (analysisResult.RequiredStatuses != null && analysisResult.RequiredStatuses.Any())
            {
                prompt.AppendLine("STATUS MAPPING:");
                var existingStatuses = analysisResult.RequiredStatuses.Where(s => s.IsExisting).ToList();
                var newStatuses = analysisResult.RequiredStatuses.Where(s => !s.IsExisting).ToList();

                if (existingStatuses.Any())
                {
                    prompt.AppendLine("Use these existing status IDs:");
                    foreach (var status in existingStatuses)
                    {
                        prompt.AppendLine($"- {status.Name} (ID: {status.ExistingId})");
                    }
                }

                if (newStatuses.Any())
                {
                    prompt.AppendLine("New statuses needed (add comments about manual creation):");
                    foreach (var status in newStatuses)
                    {
                        prompt.AppendLine($"- {status.Name}: {status.Description}");
                    }
                }
            }

            return prompt.ToString();
        }

        private string BuildEnhancedSystemPrompt(string schemaContext)
        {
            return $@"You are an expert SQL developer working with the PAWS workflow management system.
Generate ONLY SQL INSERT statements based on AI-analyzed workflow information.

CRITICAL RULES:
1. Generate ONLY INSERT statements, no DDL
2. Follow INSERT order: PAWSProcessTemplate → PAWSActivity → PAWSActivityTransition  
3. Use NEWID() for uniqueidentifier columns
4. Number activities sequentially starting from 1
5. Create logical transitions based on workflow steps and outcomes
6. Use existing status IDs where provided
7. Add comments for any new statuses that need manual creation

DATABASE SCHEMA:
{schemaContext}

Generate comprehensive INSERT statements with detailed comments explaining the workflow logic.";
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
