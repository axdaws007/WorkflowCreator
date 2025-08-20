using Microsoft.Extensions.Caching.Memory;
using Microsoft.SemanticKernel;
using System.Text.Json;
using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    public class SemanticKernelWorkflowService : IWorkflowAnalysisService
    {
        private readonly Kernel _analysisKernel;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SemanticKernelWorkflowService> _logger;
        private readonly IConfiguration _configuration;

        public SemanticKernelWorkflowService(
            [FromKeyedServices("analysis")] Kernel analysisKernel,
            IMemoryCache cache,
            ILogger<SemanticKernelWorkflowService> logger,
            IConfiguration configuration)
        {
            _analysisKernel = analysisKernel;
            _cache = cache;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<WorkflowAnalysisResult> AnalyzeWorkflowAsync(string description)
        {
            var cacheKey = $"workflow_analysis_{description.GetHashCode()}";

            // Check cache first
            if (_cache.TryGetValue(cacheKey, out WorkflowAnalysisResult? cachedResult))
            {
                _logger.LogInformation("Returning cached workflow analysis result");
                return cachedResult!;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new WorkflowAnalysisResult();

            try
            {
                _logger.LogInformation("Starting AI-powered workflow analysis");

                // Step 1: Extract workflow name
                _logger.LogDebug("Phase 1: Extracting workflow name");
                var workflowName = await ExtractWorkflowNameAsync(description);
                result.WorkflowName = workflowName;

                // Step 2: Analyze workflow steps
                _logger.LogDebug("Phase 2: Analyzing workflow steps");
                var steps = await AnalyzeWorkflowStepsAsync(description);
                result.Steps = steps;

                // Step 3: Determine required statuses
                _logger.LogDebug("Phase 3: Determining required statuses");
                var statusAnalysis = await DetermineRequiredStatusesAsync(description, steps);
                result.RequiredStatuses = statusAnalysis.RequiredStatuses;
                result.ExistingStatuses = statusAnalysis.ExistingStatuses;

                stopwatch.Stop();

                result.Success = true;
                result.AnalysisMetadata = new Dictionary<string, object>
                {
                    ["AnalysisTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["StepsFound"] = steps?.Count ?? 0,
                    ["StatusesRequired"] = statusAnalysis.RequiredStatuses?.Count ?? 0,
                    ["NewStatusesNeeded"] = statusAnalysis.RequiredStatuses?.Count(s => !s.IsExisting) ?? 0,
                    ["CachedResult"] = false,
                    ["AIProvider"] = _configuration["AI:Cloud:Provider"] ?? "Unknown"
                };

                // Cache successful results for 1 hour
                _cache.Set(cacheKey, result, TimeSpan.FromHours(1));

                _logger.LogInformation("Workflow analysis completed successfully in {ms}ms. Found {steps} steps and {statuses} required statuses",
                    stopwatch.ElapsedMilliseconds, steps?.Count ?? 0, statusAnalysis.RequiredStatuses?.Count ?? 0);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error during AI workflow analysis after {ms}ms", stopwatch.ElapsedMilliseconds);

                return new WorkflowAnalysisResult
                {
                    Success = false,
                    ErrorMessage = $"AI analysis failed: {ex.Message}",
                    AnalysisMetadata = new Dictionary<string, object>
                    {
                        ["AnalysisTimeMs"] = stopwatch.ElapsedMilliseconds,
                        ["ErrorType"] = ex.GetType().Name,
                        ["CachedResult"] = false
                    }
                };
            }
        }

        private async Task<string> ExtractWorkflowNameAsync(string description)
        {
            var function = _analysisKernel.CreateFunctionFromPrompt(
                promptTemplate: """
                You are an expert at analyzing business workflows.
                Extract a concise, professional workflow name from the description.
                
                Rules:
                - Name should be 3-8 words maximum
                - Use title case (e.g., "Purchase Order Approval")
                - Should be suitable for a business system
                - Focus on the main business process
                - Avoid generic terms like "Process" or "System"
                
                Examples:
                - "Employee Onboarding"
                - "Purchase Order Approval" 
                - "Expense Report Review"
                - "Customer Support Ticket"
                - "Invoice Processing"
                
                Description: {{$description}}
                
                Extract the workflow name:
                """,
                functionName: "ExtractWorkflowName",
                description: "Extracts a concise workflow name from a description"
            );

            var result = await _analysisKernel.InvokeAsync(function, new KernelArguments
            {
                ["description"] = description
            });

            var workflowName = result.GetValue<string>()?.Trim();

            if (string.IsNullOrEmpty(workflowName))
            {
                _logger.LogWarning("AI returned empty workflow name, using fallback");
                return ExtractFallbackName(description);
            }

            _logger.LogDebug("Extracted workflow name: '{name}'", workflowName);
            return workflowName;
        }

        private async Task<List<WorkflowStep>?> AnalyzeWorkflowStepsAsync(string description)
        {
            var function = _analysisKernel.CreateFunctionFromPrompt(
                promptTemplate: """
                You are an expert business analyst specializing in workflow design.
                Analyze the workflow description and extract the sequential steps.
                
                For each step, identify:
                1. Order - Sequential step number (starting from 1)
                2. Title - Concise step name (3-6 words)
                3. Description - What happens in this step (1-2 sentences)
                4. AssignedRole - Who performs this step (if mentioned or can be inferred)
                5. PossibleOutcomes - What decisions/actions can result from this step
                
                Important guidelines:
                - Focus on the main workflow steps, not sub-tasks
                - Include decision points and approval steps
                - Consider parallel processes as separate steps
                - Think about error/rejection paths
                
                Return ONLY valid JSON in this exact format:
                {
                  "steps": [
                    {
                      "order": 1,
                      "title": "Submit Request",
                      "description": "Employee submits the initial request with required documentation.",
                      "assignedRole": "Employee",
                      "possibleOutcomes": ["Submitted", "Draft Saved", "Validation Error"]
                    },
                    {
                      "order": 2,
                      "title": "Manager Review",
                      "description": "Direct manager reviews the request for approval or rejection.",
                      "assignedRole": "Manager", 
                      "possibleOutcomes": ["Approved", "Rejected", "Request More Info"]
                    }
                  ]
                }
                
                Workflow Description: {{$description}}
                
                JSON Response:
                """,
                functionName: "AnalyzeWorkflowSteps",
                description: "Analyzes workflow description and extracts structured steps"
            );

            var result = await _analysisKernel.InvokeAsync(function, new KernelArguments
            {
                ["description"] = description
            });

            var jsonResponse = result.GetValue<string>();
            var steps = ParseStepsFromJson(jsonResponse);

            _logger.LogDebug("Analyzed {count} workflow steps", steps?.Count ?? 0);
            return steps;
        }

        private async Task<(List<WorkflowStatus>? RequiredStatuses, List<WorkflowStatus>? ExistingStatuses)>
            DetermineRequiredStatusesAsync(string description, List<WorkflowStep>? steps)
        {
            var existingStatuses = GetExistingStatuses();
            var existingStatusText = string.Join("\n", existingStatuses.Select(s => $"- {s.Name} (ID: {s.ExistingId}) - {s.Description}"));

            var stepsText = steps != null && steps.Any()
                ? string.Join("\n", steps.Select(s => $"{s.Order}. {s.Title}: {s.Description}"))
                : "No structured steps available";

            var function = _analysisKernel.CreateFunctionFromPrompt(
                promptTemplate: """
                You are a workflow system analyst. Analyze the workflow and determine what statuses are needed.

                EXISTING STATUSES in the PAWS system:
                {{$existingStatuses}}

                WORKFLOW DESCRIPTION: 
                {{$description}}
                
                ANALYZED STEPS:
                {{$steps}}

                Your task:
                1. Identify all status transitions mentioned in the workflow
                2. Map them to existing statuses where possible (use exact matches or close equivalents)
                3. Identify any new statuses that need to be created
                4. Consider the workflow steps and their possible outcomes

                Guidelines:
                - Prefer existing statuses when they match the workflow needs
                - Only suggest new statuses when existing ones don't fit
                - Consider status transitions: what triggers moving from one status to another
                - Think about error states and rejection paths

                Return ONLY valid JSON in this format:
                {
                  "requiredStatuses": [
                    {
                      "name": "Submitted for Approval",
                      "description": "Request has been submitted and is waiting for manager review",
                      "isExisting": true,
                      "existingId": 2
                    },
                    {
                      "name": "Pending HR Review", 
                      "description": "Request approved by manager, now needs HR verification",
                      "isExisting": false,
                      "existingId": null
                    }
                  ]
                }

                JSON Response:
                """,
                functionName: "DetermineRequiredStatuses",
                description: "Determines required workflow statuses and maps to existing ones"
            );

            var result = await _analysisKernel.InvokeAsync(function, new KernelArguments
            {
                ["existingStatuses"] = existingStatusText,
                ["description"] = description,
                ["steps"] = stepsText
            });

            var jsonResponse = result.GetValue<string>();
            var statusAnalysis = ParseStatusAnalysis(jsonResponse);

            _logger.LogDebug("Determined {required} required statuses ({existing} existing, {new} new)",
                statusAnalysis.RequiredStatuses?.Count ?? 0,
                statusAnalysis.ExistingStatuses?.Count ?? 0,
                statusAnalysis.RequiredStatuses?.Count(s => !s.IsExisting) ?? 0);

            return statusAnalysis;
        }

        public async Task<bool> TestCloudConnectionAsync()
        {
            try
            {
                var testFunction = _analysisKernel.CreateFunctionFromPrompt(
                    "You are testing connectivity. Respond with exactly: 'Connection test successful'",
                    functionName: "TestConnection"
                );

                var result = await _analysisKernel.InvokeAsync(testFunction);
                var response = result.GetValue<string>();

                var isConnected = !string.IsNullOrEmpty(response) && response.Contains("successful");
                _logger.LogInformation("Cloud AI connection test result: {result}", isConnected ? "Success" : "Failed");

                return isConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cloud AI connection test failed");
                return false;
            }
        }

        public async Task<AIServiceInfo> GetCloudServiceInfoAsync()
        {
            var provider = _configuration["AI:Cloud:Provider"] ?? "Unknown";
            var modelId = provider.ToLower() switch
            {
                "openai" => _configuration["AI:Cloud:OpenAI:ModelId"] ?? "Unknown",
                "azure" => _configuration["AI:Cloud:Azure:ModelId"] ?? "Unknown",
                _ => "Unknown"
            };

            var endpoint = provider.ToLower() switch
            {
                "openai" => "https://api.openai.com",
                "azure" => _configuration["AI:Cloud:Azure:Endpoint"] ?? "Unknown",
                _ => "Local fallback"
            };

            var isAvailable = await TestCloudConnectionAsync();

            return new AIServiceInfo
            {
                Provider = provider,
                ModelId = modelId,
                Endpoint = endpoint,
                IsAvailable = isAvailable,
                Metadata = new Dictionary<string, object>
                {
                    ["Temperature"] = _configuration.GetValue<double>("AI:Cloud:Temperature", 0.7),
                    ["MaxTokens"] = _configuration.GetValue<int>("AI:Cloud:MaxTokens", 2000),
                    ["Purpose"] = "Workflow Analysis"
                }
            };
        }

        #region Helper Methods

        private string ExtractFallbackName(string description)
        {
            var words = description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4);
            var name = string.Join(" ", words);
            return name.Length > 50 ? name.Substring(0, 47) + "..." : name;
        }

        private List<WorkflowStep>? ParseStepsFromJson(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var cleanJson = ExtractJsonFromResponse(json);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var parsed = JsonSerializer.Deserialize<JsonElement>(cleanJson, options);

                if (parsed.TryGetProperty("steps", out var stepsElement))
                {
                    var steps = new List<WorkflowStep>();

                    foreach (var stepElement in stepsElement.EnumerateArray())
                    {
                        var step = new WorkflowStep
                        {
                            Order = stepElement.TryGetProperty("order", out var orderProp) ? orderProp.GetInt32() : 0,
                            Title = stepElement.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
                            Description = stepElement.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "",
                            AssignedRole = stepElement.TryGetProperty("assignedRole", out var roleProp) ? roleProp.GetString() : null
                        };

                        if (stepElement.TryGetProperty("possibleOutcomes", out var outcomesProp))
                        {
                            step.PossibleOutcomes = outcomesProp.EnumerateArray()
                                .Select(outcome => outcome.GetString() ?? "")
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();
                        }

                        steps.Add(step);
                    }

                    return steps.OrderBy(s => s.Order).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse steps JSON: {json}", json?.Substring(0, Math.Min(200, json.Length)));
            }

            return null;
        }

        private (List<WorkflowStatus>? RequiredStatuses, List<WorkflowStatus>? ExistingStatuses) ParseStatusAnalysis(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return (null, null);

            try
            {
                var cleanJson = ExtractJsonFromResponse(json);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var parsed = JsonSerializer.Deserialize<JsonElement>(cleanJson, options);

                if (parsed.TryGetProperty("requiredStatuses", out var statusesElement))
                {
                    var allStatuses = new List<WorkflowStatus>();

                    foreach (var statusElement in statusesElement.EnumerateArray())
                    {
                        var status = new WorkflowStatus
                        {
                            Name = statusElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                            Description = statusElement.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "",
                            IsExisting = statusElement.TryGetProperty("isExisting", out var existingProp) && existingProp.GetBoolean(),
                            ExistingId = statusElement.TryGetProperty("existingId", out var idProp) && idProp.ValueKind != JsonValueKind.Null
                                ? idProp.GetInt32() : null
                        };

                        allStatuses.Add(status);
                    }

                    var required = allStatuses.Where(s => !s.IsExisting).ToList();
                    var existing = allStatuses.Where(s => s.IsExisting).ToList();

                    return (required, existing);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse status analysis JSON: {json}", json?.Substring(0, Math.Min(200, json.Length)));
            }

            return (null, null);
        }

        private string ExtractJsonFromResponse(string response)
        {
            // Remove markdown code blocks if present
            var lines = response.Split('\n');
            var jsonLines = new List<string>();
            bool inJsonBlock = false;

            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("```"))
                {
                    inJsonBlock = !inJsonBlock;
                    continue;
                }

                if (inJsonBlock || (!response.Contains("```") && (line.TrimStart().StartsWith("{") || jsonLines.Any())))
                {
                    jsonLines.Add(line);
                    if (line.TrimEnd().EndsWith("}") && jsonLines.Count > 1 && !inJsonBlock)
                    {
                        break;
                    }
                }
            }

            return jsonLines.Any() ? string.Join("\n", jsonLines) : response;
        }

        private List<WorkflowStatus> GetExistingStatuses()
        {
            return new List<WorkflowStatus>
            {
                new() { Name = "Pending", ExistingId = 1, IsExisting = true, Description = "Initial state, waiting for action" },
                new() { Name = "Submitted for Approval", ExistingId = 2, IsExisting = true, Description = "Request submitted and waiting for approval" },
                new() { Name = "Approved", ExistingId = 3, IsExisting = true, Description = "Request has been approved" },
                new() { Name = "Rejected", ExistingId = 7, IsExisting = true, Description = "Request has been rejected" },
                new() { Name = "Submit Draft", ExistingId = 17, IsExisting = true, Description = "Save as draft for later submission" },
                new() { Name = "Submit for Review", ExistingId = 18, IsExisting = true, Description = "Submit for initial review" },
                new() { Name = "Review and Close", ExistingId = 19, IsExisting = true, Description = "Final review and close the process" }
            };
        }

        #endregion
    }
}
