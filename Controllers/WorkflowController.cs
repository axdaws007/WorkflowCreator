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
        /// Updated for cloud-only setup with programmatic SQL
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var viewModel = new WorkflowCreateViewModel();

            // Updated configuration info for the UI
            ViewBag.CloudProvider = _configuration["AI:Cloud:Provider"] ?? "Not configured";
            ViewBag.SqlProvider = "Programmatic Generator"; // Changed from LocalProvider
            ViewBag.IsCloudOnlySetup = true; // Changed from IsHybridSetup

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
                ViewBag.HealthWarnings = new List<string> { "Unable to check cloud AI service status" };
            }

            return View(viewModel);
        }

        /// <summary>
        /// POST: /Workflow/Create
        /// Updated workflow creation with programmatic SQL generation
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
                _logger.LogInformation("Starting cloud AI + programmatic SQL workflow creation");

                // PHASE 1: AI-Powered Workflow Analysis (Cloud AI - unchanged)
                processingSteps.Add("Analyzing workflow with cloud AI");
                var analysisStopwatch = Stopwatch.StartNew();
                var analysisResult = await _analysisService.AnalyzeWorkflowAsync(model.WorkflowDescription);
                analysisStopwatch.Stop();

                if (!analysisResult.Success)
                {
                    _logger.LogError("Workflow analysis failed: {error}", analysisResult.ErrorMessage);
                    return View("Result", new WorkflowResultViewModel
                    {
                        Success = false,
                        Message = $"Cloud AI analysis failed: {analysisResult.ErrorMessage}",
                        ResponseTimeMs = overallStopwatch.ElapsedMilliseconds,
                        ProcessingSteps = processingSteps
                    });
                }

                processingSteps.Add($"✓ Analysis completed: '{analysisResult.WorkflowName}' with {analysisResult.StepCount} steps");

                // Create workflow model
                var workflow = new WorkflowModel
                {
                    Id = await GetNextWorkflowIdAsync(),
                    Name = analysisResult.WorkflowName ?? "Unnamed Workflow",
                    Description = model.WorkflowDescription,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Analyzed"
                };

                // PHASE 2: Programmatic SQL Generation (NEW - much faster)
                processingSteps.Add("Generating SQL with programmatic engine");
                var sqlStopwatch = Stopwatch.StartNew();
                var schemaContext = GetWorkflowSchema();

                // Use programmatic generation instead of LLM
                var sqlResult = await _sqlService.GenerateEnhancedSqlAsync(analysisResult, schemaContext);
                sqlStopwatch.Stop();

                processingSteps.Add(sqlResult.Success
                    ? $"✓ SQL generated instantly ({sqlResult.GenerationTimeMs}ms)" // Will be ~0ms
                    : $"✗ SQL generation failed: {sqlResult.ErrorMessage}");

                // Build comprehensive result
                var result = new WorkflowResultViewModel
                {
                    Success = sqlResult.Success,
                    Workflow = workflow,
                    Steps = analysisResult.Steps?.Select(s => $"{s.Title}: {s.Description}").ToList(),
                    FlowTransitions = analysisResult.FlowTransitions,
                    ProcessingSteps = processingSteps,
                    RequiredStatuses = analysisResult.RequiredStatuses,
                    ExistingStatuses = analysisResult.ExistingStatuses,
                    SqlWarnings = sqlResult.Warnings
                };

                // Add updated metadata for cloud-only setup
                result.AnalysisMetadata = new Dictionary<string, object>
                {
                    ["TotalTimeMs"] = overallStopwatch.ElapsedMilliseconds,
                    ["AnalysisTimeMs"] = analysisStopwatch.ElapsedMilliseconds,
                    ["SqlGenerationTimeMs"] = sqlStopwatch.ElapsedMilliseconds, // Will be ~0ms
                    ["CloudProvider"] = analysisResult.AIProvider,
                    ["SqlProvider"] = "Programmatic Generator", // Changed
                    ["SqlMethod"] = "Code-based", // New
                    ["RequiresLocalAI"] = false, // New
                    ["WorkflowName"] = analysisResult.WorkflowName ?? "",
                    ["StepCount"] = analysisResult.StepCount,
                    ["StatusCount"] = analysisResult.AllStatuses.Count,
                    ["SetupType"] = "Cloud-Only" // New
                };

                if (sqlResult.Success)
                {
                    result.GeneratedSql = sqlResult.GeneratedSql;
                    workflow.GeneratedSql = sqlResult.GeneratedSql;
                    workflow.Status = "Generated";
                    result.Message = "Workflow analyzed with cloud AI and SQL generated programmatically!";
                }
                else
                {
                    result.Message = $"Cloud AI analysis successful, but SQL generation failed: {sqlResult.ErrorMessage}";
                    workflow.Status = "Analysis Complete";
                }

                await _workflowService.SaveWorkflowAsync(workflow);

                overallStopwatch.Stop();
                result.ResponseTimeMs = overallStopwatch.ElapsedMilliseconds;

                _logger.LogInformation("Cloud-only workflow processing completed in {ms}ms",
                    overallStopwatch.ElapsedMilliseconds);

                return View("Result", result);
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                _logger.LogError(ex, "Error in cloud-only workflow creation");

                return View("Result", new WorkflowResultViewModel
                {
                    Success = false,
                    Message = $"Error during cloud AI processing: {ex.Message}",
                    ResponseTimeMs = overallStopwatch.ElapsedMilliseconds,
                    ProcessingSteps = processingSteps
                });
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

        // <summary>
        /// POST: /Workflow/TestLocalConnection
        /// Updated to test programmatic SQL generation instead of local AI
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestLocalConnection()
        {
            try
            {
                // Test programmatic SQL generation instead of local AI
                var result = await _connectionTestService.TestLocalConnectionAsync();
                return Json(new
                {
                    isConnected = result.IsConnected, // Always true for programmatic
                    message = result.Message, // "Programmatic SQL generation is always available"
                    provider = "Programmatic Generator",
                    modelId = "Code-based SQL Generation v1.0",
                    responseTimeMs = result.ResponseTimeMs, // Always 0
                    details = result.Details
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing SQL generation");
                return Json(new
                {
                    isConnected = false,
                    message = $"SQL generation test error: {ex.Message}",
                    provider = "Programmatic Generator"
                });
            }
        }

        /// <summary>
        /// POST: /Workflow/TestAllConnections  
        /// Updated for cloud-only setup
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
                        responseTimeMs = health.CloudService.ResponseTimeMs,
                        provider = health.CloudService.Provider
                    },
                    sqlGeneration = new // Changed from localService
                    {
                        isConnected = true, // Always available
                        message = "Programmatic SQL generation ready",
                        responseTimeMs = 0, // Instant
                        provider = "Programmatic Generator"
                    },
                    warnings = health.Warnings,
                    recommendations = health.Recommendations,
                    setupType = "Cloud-Only" // New field
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing all connections");
                return Json(new
                {
                    isHealthy = false,
                    message = $"System health check failed: {ex.Message}",
                    setupType = "Cloud-Only"
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


        /// <summary>
        /// Updated helper method - no longer needs to build LLM prompts
        /// </summary>
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