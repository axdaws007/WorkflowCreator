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
                var workflowResponse = await AnalyzeWorkflowStepsAsync(description);
                result.Steps = workflowResponse.Steps;
                result.FlowTransitions = workflowResponse.Transitions;

                // Step 3: Determine required statuses
                _logger.LogDebug("Phase 3: Determining required statuses");
                var statusAnalysis = await DetermineRequiredStatusesAsync(description, workflowResponse.Steps, workflowResponse.Transitions);
                result.RequiredStatuses = statusAnalysis.RequiredStatuses;
                result.ExistingStatuses = statusAnalysis.ExistingStatuses;

                stopwatch.Stop();

                result.Success = true;
                result.AnalysisMetadata = new Dictionary<string, object>
                {
                    ["AnalysisTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["StepsFound"] = workflowResponse.Steps?.Count ?? 0,
                    ["StatusesRequired"] = statusAnalysis.RequiredStatuses?.Count ?? 0,
                    ["NewStatusesNeeded"] = statusAnalysis.RequiredStatuses?.Count(s => !s.IsExisting) ?? 0,
                    ["CachedResult"] = false,
                    ["AIProvider"] = _configuration["AI:Cloud:Provider"] ?? "Unknown"
                };

                // Cache successful results for 1 hour
                _cache.Set(cacheKey, result, TimeSpan.FromHours(1));

                _logger.LogInformation("Workflow analysis completed successfully in {ms}ms. Found {steps} steps and {statuses} required statuses",
                    stopwatch.ElapsedMilliseconds, workflowResponse.Steps?.Count ?? 0, statusAnalysis.RequiredStatuses?.Count ?? 0);

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
                - The workflow name may be based upon a statuatory form and might be an acronym.
                - The Description may include the user's preferred name for the workflow.  Use the suggested name if provided.
                
                Examples:
                - "SE"
                - "Topic 5V" 
                - "Enquiry"
                - "Extension Request"
                - "SESOR"
                
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

        private async Task<(List<WorkflowStep>? Steps, List<WorkflowTransition>? Transitions)> AnalyzeWorkflowStepsAsync(string description)
        {
            var function = _analysisKernel.CreateFunctionFromPrompt(
                promptTemplate: """
                You are an expert business workflow analyst specializing in process flow design.
                Analyze the workflow description and extract both the sequential steps AND the complete flow logic.

                Your task is to identify:
                1. **Workflow Steps** - Sequential process phases (states/stages)
                2. **Flow Transitions** - How steps connect via trigger statuses (user actions)

                For each step, determine:
                - Order: Sequential step number (starting from 1)
                - Title: Concise step name (3-6 words)
                - Description: What happens in this step (1-2 sentences)
                - AssignedRole: Who performs this step (if mentioned or can be inferred)

                For the flow logic, identify ALL transitions in this format:
                - SourceStep: The step where the action originates (or NULL for workflow start)
                - TriggerStatus: The user action/decision that causes the transition
                - DestinationStep: The step that results from the action (or NULL for workflow termination)
                - IsProgressive: true if moving forward in the process, false if moving backward

                CRITICAL RULES:
                - Always include the initial transition: NULL → "Pending" → FirstStep → true
                - Trigger statuses are USER ACTIONS (verbs like "Submit", "Approve", "Reject")
                - Step names are STATES (nouns like "Draft", "Open", "Closed")
                - Terminating actions have DestinationStep = NULL
                - Rejection/rework paths are IsProgressive = false
                - Approval/forward paths are IsProgressive = true
                - Use the exact step names and trigger status names from the description

                Return ONLY valid JSON in this exact format:
                {
                  "steps": [
                    {
                      "order": 1,
                      "title": "Draft",
                      "description": "Initial creation phase where the request is prepared."
                    },
                    {
                      "order": 2,
                      "title": "Open", 
                      "description": "Under review by the appropriate approver."
                    }
                  ],
                  "flowTransitions": [
                    {
                      "sourceStep": null,
                      "triggerStatus": "Pending",
                      "destinationStep": "Draft",
                      "isProgressive": true
                    },
                    {
                      "sourceStep": "Draft",
                      "triggerStatus": "Submit for Approval", 
                      "destinationStep": "Open",
                      "isProgressive": true
                    },
                    {
                      "sourceStep": "Draft",
                      "triggerStatus": "Withdraw",
                      "destinationStep": null,
                      "isProgressive": true
                    },
                    {
                      "sourceStep": "Open",
                      "triggerStatus": "Approve",
                      "destinationStep": "Closed", 
                      "isProgressive": true
                    },
                    {
                      "sourceStep": "Open",
                      "triggerStatus": "Reject",
                      "destinationStep": "Draft",
                      "isProgressive": false
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
            var workflowResponse = ParseStepsAndTransitionsFromJson(jsonResponse);

            _logger.LogDebug("Analyzed {count} workflow steps", workflowResponse.Steps?.Count ?? 0);

            return workflowResponse;
        }

        private async Task<(List<WorkflowStatus>? RequiredStatuses, List<WorkflowStatus>? ExistingStatuses)>
            DetermineRequiredStatusesAsync(string description, List<WorkflowStep>? steps, List<WorkflowTransition>? transitions)
        {
            var existingStatuses = GetExistingStatuses();
            var existingStatusText = string.Join("\n", existingStatuses.Select(s => $"- {s.Name} (ID: {s.ExistingId}) - {s.Description}"));

            // Build steps context for better understanding
            var stepsText = string.Empty;
            if (steps != null && steps.Any())
            {
                stepsText = string.Join("\n", steps.Select(s => $"Step {s.Order}. {s.Title}: {s.Description}"));
            }
            else
            {
                stepsText = "No structured steps available";
            }

            // Build transitions context - this is the key information for extracting trigger statuses
            var transitionsText = string.Empty;
            if (transitions != null && transitions.Any())
            {
                transitionsText = "WORKFLOW TRANSITIONS (these contain the trigger statuses):\n";
                foreach (var transition in transitions)
                {
                    var sourceDisplay = transition.SourceStep ?? "WORKFLOW START";
                    var destDisplay = transition.DestinationStep ?? "WORKFLOW END";
                    var progressType = transition.IsProgressive ? "Forward" : "Backward";

                    transitionsText += $"- From '{sourceDisplay}' → Trigger: '{transition.TriggerStatus}' → To '{destDisplay}' ({progressType})\n";
                }
            }
            else
            {
                transitionsText = "No transitions available";
            }

            var prompt = $"""
        You are a workflow system analyst. Analyze the workflow transitions to determine what TRIGGER STATUSES (user action choices) are needed.

        IMPORTANT: Trigger statuses are the ACTION CHOICES available to users at each workflow step, NOT the step names themselves.
        The trigger statuses are explicitly provided in the WORKFLOW TRANSITIONS below.

        EXISTING TRIGGER STATUSES in the PAWS system:
        {existingStatusText}

        WORKFLOW DESCRIPTION: 
        {description}

        WORKFLOW STEPS:
        {stepsText}

        {transitionsText}

        """;

            prompt += """
        Your task:
        1. Extract ALL unique trigger statuses from the workflow transitions above
        2. For each trigger status, map it to existing statuses where possible (exact or close semantic matches)
        3. Identify any new trigger statuses that need to be created
        4. DO NOT include workflow step names (like "Draft", "Open", "Closed") as trigger statuses

        Guidelines:
        - Trigger statuses are VERBS or ACTION PHRASES (e.g., "Submit for Approval", "Reject", "Approve")
        - Step names are NOUNS or STATES (e.g., "Draft", "Open", "Closed") - DO NOT include these
        - Use the EXACT trigger status names from the transitions
        - Consider the business context for appropriate mappings
        - Each trigger status represents a business decision or action

        For mapping existing statuses, be flexible with naming variations:
        - "Approve" maps to existing "Approved" or "Approve"
        - "Submit" variations can map to "Submit for Approval" or "Submitted for Approval"
        - "Reject" maps to existing "Rejected" or "Reject"

        Return ONLY valid JSON in this exact format:
        {
        
            "requiredStatuses": [
            {
        
              "name": "Submit for Approval",
              "description": "User action to submit the item for manager review",
              "isExisting": true,
              "existingId": 2,
              "stepContext": "Available from Draft step"
            },
            {
        
              "name": "Withdraw", 
              "description": "User action to withdraw and terminate the process",
              "isExisting": false,
              "existingId": null,
              "stepContext": "Available from Draft step"
            }
          ]
        }

        JSON Response:
        """;

            var function = _analysisKernel.CreateFunctionFromPrompt(
                promptTemplate: prompt,
                functionName: "DetermineRequiredStatuses",
                description: "Determines required workflow statuses from transitions and maps to existing ones"
            );

            var result = await _analysisKernel.InvokeAsync(function, new KernelArguments
            {
                ["existingStatuses"] = existingStatusText,
                ["description"] = description,
                ["steps"] = stepsText,
                ["transitions"] = transitionsText
            });

            var jsonResponse = result.GetValue<string>();
            var statusAnalysis = ParseStatusAnalysis(jsonResponse);

            _logger.LogDebug("Determined {required} required statuses ({existing} existing, {new} new) from {transitionCount} transitions",
                statusAnalysis.RequiredStatuses?.Count ?? 0,
                statusAnalysis.ExistingStatuses?.Count ?? 0,
                statusAnalysis.RequiredStatuses?.Count(s => !s.IsExisting) ?? 0,
                transitions?.Count ?? 0);

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


        private (List<WorkflowStep>? Steps, List<WorkflowTransition>? FlowTransitions) ParseStepsAndTransitionsFromJson(string? json)
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

                // Parse workflow steps
                List<WorkflowStep>? steps = null;
                if (parsed.TryGetProperty("steps", out var stepsElement))
                {
                    steps = new List<WorkflowStep>();

                    foreach (var stepElement in stepsElement.EnumerateArray())
                    {
                        var step = new WorkflowStep
                        {
                            Order = stepElement.TryGetProperty("order", out var orderProp) ? orderProp.GetInt32() : 0,
                            Title = stepElement.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
                            Description = stepElement.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : ""
                        };

                        steps.Add(step);
                    }

                    steps = steps.OrderBy(s => s.Order).ToList();
                }

                // Parse flow transitions
                List<WorkflowTransition>? transitions = null;
                if (parsed.TryGetProperty("flowTransitions", out var transitionsElement))
                {
                    transitions = new List<WorkflowTransition>();

                    foreach (var transitionElement in transitionsElement.EnumerateArray())
                    {
                        var transition = new WorkflowTransition
                        {
                            SourceStep = transitionElement.TryGetProperty("sourceStep", out var sourceProp) && sourceProp.ValueKind != JsonValueKind.Null
                                ? sourceProp.GetString() : null,
                            TriggerStatus = transitionElement.TryGetProperty("triggerStatus", out var triggerProp)
                                ? triggerProp.GetString() ?? "" : "",
                            DestinationStep = transitionElement.TryGetProperty("destinationStep", out var destProp) && destProp.ValueKind != JsonValueKind.Null
                                ? destProp.GetString() : null,
                            IsProgressive = transitionElement.TryGetProperty("isProgressive", out var progressiveProp)
                                && progressiveProp.GetBoolean()
                        };

                        transitions.Add(transition);
                    }
                }

                _logger.LogDebug("Parsed {stepCount} steps and {transitionCount} transitions",
                    steps?.Count ?? 0, transitions?.Count ?? 0);

                return (steps, transitions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse enhanced workflow JSON: {json}",
                    json?.Substring(0, Math.Min(200, json.Length)));
                return (null, null);
            }
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
                new() { Name = "Submit for Approval", ExistingId = 2, IsExisting = true, Description = "Request submitted and waiting for approval" },
                new() { Name = "Approve", ExistingId = 3, IsExisting = true, Description = "Request has been approved" },
                new() { Name = "Reject", ExistingId = 7, IsExisting = true, Description = "Request has been rejected" },
                new() { Name = "Submit Draft", ExistingId = 17, IsExisting = true, Description = "Save as draft for later submission" },
                new() { Name = "Submit for Review", ExistingId = 18, IsExisting = true, Description = "Submit for initial review" },
                new() { Name = "Review and Close", ExistingId = 19, IsExisting = true, Description = "Final review and close the process" }
            };
        }

        #endregion
    }
}
