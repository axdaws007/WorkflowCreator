// Controllers/WorkflowController.cs
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using WorkflowCreator.Models;
using WorkflowCreator.Services;

namespace WorkflowCreator.Controllers
{
    public class WorkflowController : Controller
    {
        private readonly IWorkflowAnalysisService _analysisService;
        private readonly ISqlGenerationService _sqlService;
        private readonly IConnectionTestService _connectionTestService;
        private readonly IWorkflowService _workflowService;
        private readonly ILogger<WorkflowController> _logger;
        private readonly IConfiguration _configuration;

        public WorkflowController(
            IWorkflowAnalysisService analysisService,
            ISqlGenerationService sqlService,
            IConnectionTestService connectionTestService,
            IWorkflowService workflowService,
            ILogger<WorkflowController> logger,
            IConfiguration configuration)
        {
            _analysisService = analysisService;
            _sqlService = sqlService;
            _connectionTestService = connectionTestService;
            _workflowService = workflowService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// GET: /Workflow
        /// Displays the workflow creation form
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var viewModel = new WorkflowCreateViewModel();

            // Add configuration info for the UI
            ViewBag.CloudProvider = _configuration["AI:Cloud:Provider"] ?? "Not configured";
            ViewBag.LocalProvider = _configuration["AI:Local:Provider"] ?? "Ollama";
            ViewBag.IsHybridSetup = !string.IsNullOrEmpty(_configuration["AI:Cloud:Provider"]) &&
                                   !string.IsNullOrEmpty(_configuration["AI:Local:ModelId"]);

            // Quick health check for status indicators
            try
            {
                var healthCheck = await _connectionTestService.TestAllConnectionsAsync();
                ViewBag.SystemHealth = healthCheck.IsHealthy;
                ViewBag.HealthWarnings = healthCheck.Warnings;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to perform health check on Index page");
                ViewBag.SystemHealth = false;
                ViewBag.HealthWarnings = new List<string> { "Unable to check AI service status" };
            }

            return View(viewModel);
        }

        /// <summary>
        /// POST: /Workflow/Create
        /// Processes workflow description using AI services and generates SQL
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(WorkflowCreateViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.WorkflowDescription))
            {
                ModelState.AddModelError("WorkflowDescription", "Please enter a workflow description.");
                return View("Index", model);
            }

            var overallStopwatch = Stopwatch.StartNew();
            var processingSteps = new List<string>();

            try
            {
                _logger.LogInformation("Starting AI-powered workflow creation for description: {description}",
                    model.WorkflowDescription.Substring(0, Math.Min(100, model.WorkflowDescription.Length)) + "...");

                // PHASE 1: AI-Powered Workflow Analysis (Cloud AI)
                processingSteps.Add("Starting AI analysis of workflow description");
                _logger.LogInformation("Phase 1: Analyzing workflow with cloud AI");

                var analysisStopwatch = Stopwatch.StartNew();
                var analysisResult = await _analysisService.AnalyzeWorkflowAsync(model.WorkflowDescription);
                analysisStopwatch.Stop();

                if (!analysisResult.Success)
                {
                    _logger.LogError("Workflow analysis failed: {error}", analysisResult.ErrorMessage);

                    var errorResult = new WorkflowResultViewModel
                    {
                        Success = false,
                        Message = $"AI workflow analysis failed: {analysisResult.ErrorMessage}",
                        ResponseTimeMs = overallStopwatch.ElapsedMilliseconds,
                        ProcessingSteps = processingSteps,
                        AnalysisMetadata = new Dictionary<string, object>
                        {
                            ["AnalysisTimeMs"] = analysisStopwatch.ElapsedMilliseconds,
                            ["FailurePhase"] = "Analysis",
                            ["CloudProvider"] = _configuration["AI:Cloud:Provider"] ?? "Unknown"
                        }
                    };
                    return View("Result", errorResult);
                }

                processingSteps.Add($"✓ Analysis completed: '{analysisResult.WorkflowName}' with {analysisResult.StepCount} steps");

                // Validate analysis result
                var validationIssues = analysisResult.Validate();
                if (validationIssues.Any())
                {
                    _logger.LogWarning("Analysis validation issues: {issues}", string.Join(", ", validationIssues));
                    processingSteps.Add($"⚠ Analysis warnings: {validationIssues.Count} issues found");
                }

                _logger.LogInformation("Analysis completed: Name='{name}', Steps={stepCount}, Statuses={statusCount}, Time={timeMs}ms",
                    analysisResult.WorkflowName,
                    analysisResult.StepCount,
                    analysisResult.AllStatuses.Count,
                    analysisStopwatch.ElapsedMilliseconds);

                // Create workflow model with analyzed data
                var workflow = new WorkflowModel
                {
                    Id = await GetNextWorkflowIdAsync(),
                    Name = analysisResult.WorkflowName ?? "Unnamed Workflow",
                    Description = model.WorkflowDescription,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Analyzed"
                };

                // PHASE 2: SQL Generation (Local AI)
                processingSteps.Add("Generating SQL statements with specialized AI model");
                _logger.LogInformation("Phase 2: Generating SQL with local AI");

                var sqlStopwatch = Stopwatch.StartNew();
                //var systemPrompt = BuildEnhancedSystemPrompt();
                //var userPrompt = BuildEnhancedUserPrompt(analysisResult);
                var schemaContext = GetWorkflowSchema();

                var sqlResult = await _sqlService.GenerateEnhancedSqlAsync(analysisResult, schemaContext);
                sqlStopwatch.Stop();

                processingSteps.Add(sqlResult.Success
                    ? $"✓ SQL generated successfully ({sqlResult.TokensUsed} tokens, {sqlResult.GenerationTimeMs}ms)"
                    : $"✗ SQL generation failed: {sqlResult.ErrorMessage}");

                // Build comprehensive result
                var result = new WorkflowResultViewModel
                {
                    Success = sqlResult.Success,
                    Workflow = workflow,
                    Steps = analysisResult.Steps?.Select(s => $"{s.Title}: {s.Description}").ToList(),
                    SystemPrompt = sqlResult.SystemPrompt,
                    UserPrompt = sqlResult.UserPrompt,
                    ProcessingSteps = processingSteps,
                    RequiredStatuses = analysisResult.RequiredStatuses,
                    ExistingStatuses = analysisResult.ExistingStatuses,
                    ValidationIssues = validationIssues
                };

                // Add detailed metadata
                result.AnalysisMetadata = new Dictionary<string, object>
                {
                    ["TotalTimeMs"] = overallStopwatch.ElapsedMilliseconds,
                    ["AnalysisTimeMs"] = analysisStopwatch.ElapsedMilliseconds,
                    ["SqlGenerationTimeMs"] = sqlStopwatch.ElapsedMilliseconds,
                    ["CloudProvider"] = analysisResult.AIProvider,
                    ["LocalProvider"] = _configuration["AI:Local:Provider"] ?? "Unknown",
                    ["LocalModel"] = sqlResult.ModelUsed,
                    ["WorkflowName"] = analysisResult.WorkflowName ?? "",
                    ["StepCount"] = analysisResult.StepCount,
                    ["RequiredStatusCount"] = analysisResult.AllStatuses.Count,
                    ["NewStatusCount"] = analysisResult.NewStatusCount,
                    ["ExistingStatusCount"] = analysisResult.ExistingStatusCount,
                    ["WasCached"] = analysisResult.WasCached,
                    ["SqlTokensUsed"] = sqlResult.TokensUsed,
                    ["SqlWarnings"] = sqlResult.Warnings,
                    ["ValidationIssues"] = validationIssues.Count
                };

                if (sqlResult.Success)
                {
                    result.GeneratedSql = sqlResult.GeneratedSql;
                    workflow.GeneratedSql = sqlResult.GeneratedSql;
                    workflow.Status = "Generated";
                    result.Message = "Workflow successfully analyzed with AI and SQL generated!";

                    _logger.LogInformation("SQL generation completed successfully");
                }
                else
                {
                    result.Message = $"AI analysis successful, but SQL generation failed: {sqlResult.ErrorMessage}";
                    workflow.Status = "Analysis Complete";
                    _logger.LogWarning("SQL generation failed: {error}", sqlResult.ErrorMessage);
                }

                // Store workflow
                await _workflowService.SaveWorkflowAsync(workflow);

                overallStopwatch.Stop();
                result.ResponseTimeMs = overallStopwatch.ElapsedMilliseconds;

                _logger.LogInformation("Complete workflow processing finished in {ms}ms. Success: {success}",
                    overallStopwatch.ElapsedMilliseconds, result.Success);

                return View("Result", result);
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                _logger.LogError(ex, "Error in AI-powered workflow creation");

                processingSteps.Add($"✗ Unexpected error: {ex.Message}");

                var errorResult = new WorkflowResultViewModel
                {
                    Success = false,
                    Message = $"Unexpected error during AI processing: {ex.Message}",
                    ResponseTimeMs = overallStopwatch.ElapsedMilliseconds,
                    ProcessingSteps = processingSteps,
                    AnalysisMetadata = new Dictionary<string, object>
                    {
                        ["ErrorType"] = ex.GetType().Name,
                        ["TotalTimeMs"] = overallStopwatch.ElapsedMilliseconds
                    }
                };
                return View("Result", errorResult);
            }
        }

        /// <summary>
        /// GET: /Workflow/List
        /// Displays all workflows
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> List()
        {
            try
            {
                var workflows = await _workflowService.GetAllWorkflowsAsync();

                // Add metadata for enhanced display
                ViewBag.TotalWorkflows = workflows.Count;
                ViewBag.GeneratedWorkflows = workflows.Count(w => !string.IsNullOrEmpty(w.GeneratedSql));
                ViewBag.RecentWorkflows = workflows.Count(w => w.CreatedAt > DateTime.UtcNow.AddDays(-7));

                return View(workflows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving workflow list");
                TempData["ErrorMessage"] = "Error loading workflows. Please try again.";
                return View(new List<WorkflowModel>());
            }
        }

        /// <summary>
        /// GET: /Workflow/Details/{id}
        /// Shows detailed view of a specific workflow
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var workflow = await _workflowService.GetWorkflowByIdAsync(id);
                if (workflow == null)
                {
                    TempData["ErrorMessage"] = $"Workflow with ID {id} not found.";
                    return RedirectToAction(nameof(List));
                }

                return View(workflow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving workflow {id}", id);
                TempData["ErrorMessage"] = "Error loading workflow details.";
                return RedirectToAction(nameof(List));
            }
        }

        /// <summary>
        /// POST: /Workflow/ReAnalyze/{id}
        /// Re-analyzes an existing workflow with current AI models
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReAnalyze(int id)
        {
            try
            {
                var result = await _workflowService.ReAnalyzeWorkflowAsync(id);
                if (result.Success)
                {
                    TempData["SuccessMessage"] = "Workflow re-analyzed successfully with updated AI models.";
                    return View("Result", result);
                }
                else
                {
                    TempData["ErrorMessage"] = $"Re-analysis failed: {result.Message}";
                    return RedirectToAction(nameof(Details), new { id });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error re-analyzing workflow {id}", id);
                TempData["ErrorMessage"] = "Error during re-analysis. Please try again.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        /// <summary>
        /// GET: /Workflow/TestConnection
        /// Displays AI service connection testing page
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                var configSummary = await _connectionTestService.GetConfigurationSummaryAsync();
                var diagnostics = await _connectionTestService.PerformSystemDiagnosticsAsync();

                var viewModel = new ConnectionTestViewModel
                {
                    CloudProvider = configSummary.CloudProvider,
                    LocalProvider = configSummary.LocalProvider,
                    CloudModelId = configSummary.CloudModel,
                    LocalModelId = configSummary.LocalModel,
                    IsHybridSetup = configSummary.IsHybridSetup,
                    Environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Unknown",
                    CloudEndpoint = GetCloudEndpointDisplay(),
                    LocalEndpoint = _configuration["AI:Local:Endpoint"] ?? "http://localhost:11434",
                    AvailableFeatures = configSummary.ConfiguredFeatures,
                    ConfigurationWarnings = configSummary.MissingComponents,
                    OptimizationSuggestions = diagnostics.OptimizationSuggestions,
                    PerformanceMetrics = diagnostics.PerformanceMetrics
                };

                // Set last successful test results if available
                if (diagnostics.Health.CloudService.IsConnected)
                    viewModel.LastCloudTest = diagnostics.Health.CloudService;

                if (diagnostics.Health.LocalService.IsConnected)
                    viewModel.LastLocalTest = diagnostics.Health.LocalService;

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading connection test page");

                var basicViewModel = ConnectionTestViewModel.CreateBasic(
                    _configuration["AI:Cloud:Provider"],
                    _configuration["AI:Local:Provider"]);

                basicViewModel.ConfigurationWarnings.Add("Error loading diagnostics");
                return View(basicViewModel);
            }
        }

        /// <summary>
        /// POST: /Workflow/TestCloudConnection
        /// Tests cloud AI service connection
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestCloudConnection()
        {
            try
            {
                var result = await _connectionTestService.TestCloudConnectionAsync();
                return Json(new
                {
                    isConnected = result.IsConnected,
                    message = result.Message,
                    provider = result.Provider,
                    modelId = result.ModelId,
                    responseTimeMs = result.ResponseTimeMs,
                    details = result.Details
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing cloud connection");
                return Json(new
                {
                    isConnected = false,
                    message = $"Connection test error: {ex.Message}",
                    provider = _configuration["AI:Cloud:Provider"] ?? "Unknown"
                });
            }
        }

        /// <summary>
        /// POST: /Workflow/TestLocalConnection
        /// Tests local AI service connection
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestLocalConnection()
        {
            try
            {
                var result = await _connectionTestService.TestLocalConnectionAsync();
                return Json(new
                {
                    isConnected = result.IsConnected,
                    message = result.Message,
                    provider = result.Provider,
                    modelId = result.ModelId,
                    responseTimeMs = result.ResponseTimeMs,
                    details = result.Details
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing local connection");
                return Json(new
                {
                    isConnected = false,
                    message = $"Connection test error: {ex.Message}",
                    provider = _configuration["AI:Local:Provider"] ?? "Unknown"
                });
            }
        }

        /// <summary>
        /// POST: /Workflow/TestAllConnections
        /// Tests both cloud and local AI services
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestAllConnections()
        {
            try
            {
                var health = await _connectionTestService.TestAllConnectionsAsync();
                return Json(new
                {
                    isHealthy = health.IsHealthy,
                    cloudService = new
                    {
                        isConnected = health.CloudService.IsConnected,
                        message = health.CloudService.Message,
                        responseTimeMs = health.CloudService.ResponseTimeMs
                    },
                    localService = new
                    {
                        isConnected = health.LocalService.IsConnected,
                        message = health.LocalService.Message,
                        responseTimeMs = health.LocalService.ResponseTimeMs
                    },
                    warnings = health.Warnings,
                    recommendations = health.Recommendations
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing all connections");
                return Json(new
                {
                    isHealthy = false,
                    message = $"System health check failed: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// GET: /Workflow/SystemDiagnostics
        /// Returns detailed system diagnostics as JSON
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SystemDiagnostics()
        {
            try
            {
                var diagnostics = await _connectionTestService.PerformSystemDiagnosticsAsync();
                return Json(diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing system diagnostics");
                return Json(new { error = $"Diagnostics failed: {ex.Message}" });
            }
        }

        #region Helper Methods

        private string BuildEnhancedUserPrompt(WorkflowAnalysisResult analysisResult)
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
                    prompt.AppendLine();
                }
            }

            if (analysisResult.AllStatuses.Any())
            {
                prompt.AppendLine("STATUS MAPPING:");
                var existingStatuses = analysisResult.ExistingStatuses ?? new List<WorkflowStatus>();
                var newStatuses = analysisResult.RequiredStatuses ?? new List<WorkflowStatus>();

                if (existingStatuses.Any())
                {
                    prompt.AppendLine("Use these existing status IDs:");
                    foreach (var status in existingStatuses)
                    {
                        prompt.AppendLine($"- {status.Name} (Use existing ID: {status.ExistingId})");
                    }
                }

                if (newStatuses.Any())
                {
                    prompt.AppendLine("New statuses needed:");
                    foreach (var status in newStatuses)
                    {
                        prompt.AppendLine($"- {status.Name}: {status.Description}");
                    }
                }
            }

            prompt.AppendLine();
            prompt.AppendLine("Generate SQL including:");
            prompt.AppendLine("1. INSERT for PAWSProcessTemplate using the workflow name");
            prompt.AppendLine("2. INSERT statements for PAWSActivity based on the workflow steps");
            prompt.AppendLine("3. INSERT statements for PAWSActivityStatus for any new workflow statuses. Include comments for any new statuses.");
            prompt.AppendLine("4. INSERT statements for PAWSActivityTransition based on the workflow steps and their outcomes");
            prompt.AppendLine("5. Comments explaining how AI analysis drove each SQL statement");

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
                else
                {
                    _logger.LogWarning("Schema file {schemaFile} not found at {schemaPath}", schemaFile, schemaPath);
                }
            }

            return GetPlaceholderSchema();
        }

        private string GetPlaceholderSchema()
        {
            return @"-- Please ensure your actual PAWS workflow schema is loaded
-- from the workflow-schema.sql file in your project root
-- This placeholder schema is used when the schema file is not found";
        }

        private string GetCloudEndpointDisplay()
        {
            var provider = _configuration["AI:Cloud:Provider"]?.ToLower();
            return provider switch
            {
                "openai" => "https://api.openai.com",
                "azure" => _configuration["AI:Cloud:Azure:Endpoint"] ?? "Azure OpenAI",
                "anthropic" => "https://api.anthropic.com",
                _ => "Local fallback"
            };
        }

        private async Task<int> GetNextWorkflowIdAsync()
        {
            // In a real application, this would be handled by the database
            // For now, we'll use a simple in-memory counter
            var workflows = await _workflowService.GetAllWorkflowsAsync();
            return workflows.Any() ? workflows.Max(w => w.Id) + 1 : 1;
        }

        #endregion
    }
}