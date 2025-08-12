using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using WorkflowCreator.Models;
using WorkflowCreator.Services;

namespace WorkflowCreator.Controllers
{
    public class WorkflowController : Controller
    {
        private readonly IWorkflowService _workflowService;
        private readonly IOllamaService _ollamaService;
        private readonly ILogger<WorkflowController> _logger;
        private readonly IConfiguration _configuration;

        public WorkflowController(IWorkflowService workflowService, IOllamaService ollamaService,
            ILogger<WorkflowController> logger, IConfiguration configuration)
        {
            _workflowService = workflowService;
            _ollamaService = ollamaService;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(new WorkflowCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(WorkflowCreateViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.WorkflowDescription))
            {
                ModelState.AddModelError("WorkflowDescription", "Please enter a workflow description.");
                return View("Index", model);
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Process the workflow description
                var result = _workflowService.ProcessWorkflowDescription(model.WorkflowDescription);

                // Build prompts for Ollama
                var systemPrompt = BuildSystemPrompt();
                var userPrompt = BuildUserPrompt(model.WorkflowDescription, result.Steps);

                result.SystemPrompt = systemPrompt;
                result.UserPrompt = userPrompt;

                // Call Ollama to generate SQL
                var sqlResult = await _ollamaService.GenerateSqlAsync(systemPrompt, userPrompt);

                if (sqlResult.Success && result.Workflow != null)
                {
                    result.GeneratedSql = sqlResult.GeneratedSql;
                    result.Workflow.GeneratedSql = sqlResult.GeneratedSql;
                }
                else if (!sqlResult.Success)
                {
                    result.Message = $"Workflow created but SQL generation failed: {sqlResult.ErrorMessage}";
                }

                stopwatch.Stop();
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

                return View("Result", result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating workflow");
                var errorResult = new WorkflowResultViewModel
                {
                    Success = false,
                    Message = $"Error processing workflow: {ex.Message}"
                };
                return View("Result", errorResult);
            }
        }

        [HttpGet]
        public IActionResult List()
        {
            var workflows = _workflowService.GetAllWorkflows();
            return View(workflows);
        }

        [HttpGet]
        public IActionResult TestConnection()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> TestOllamaConnection()
        {
            var result = await _ollamaService.TestConnectionAsync();
            return Json(new { isConnected = result.IsConnected, message = result.Message });
        }

        private string BuildSystemPrompt()
        {
            string existingSchema = "";

            // Try to load schema from file if configured
            if (_configuration.GetValue<bool>("WorkflowSchema:UseSchemaFile"))
            {
                var schemaFile = _configuration["WorkflowSchema:SchemaFile"] ?? "workflow-schema.sql";
                var schemaPath = Path.Combine(Directory.GetCurrentDirectory(), schemaFile);


                if (System.IO.File.Exists(schemaPath))
                {
                    existingSchema = System.IO.File.ReadAllText(schemaPath);
                    _logger.LogInformation($"Loaded schema from {schemaFile}");
                }
                else
                {
                    _logger.LogWarning($"Schema file {schemaFile} not found. Using placeholder schema.");
                    existingSchema = GetPlaceholderSchema();
                }
            }
            else
            {
                existingSchema = GetPlaceholderSchema();
            }

            return $@"You are an expert SQL developer working with an existing workflow management system database. 
Your task is to generate ONLY SQL INSERT statements to populate the existing tables based on workflow descriptions.

CRITICAL RULES:
1. DO NOT generate any CREATE TABLE, ALTER TABLE, CREATE PROCEDURE, or any other DDL statements
2. ONLY generate INSERT statements for data
3. Use only the tables and columns that exist in the provided schema
4. Ensure all foreign key relationships are respected
5. Generate realistic and appropriate data based on the workflow description
6. Include proper values for all required (NOT NULL) columns
7. Use appropriate default values where necessary
8. Generate multiple related INSERT statements to fully represent the workflow

EXISTING DATABASE SCHEMA:
{existingSchema}

When generating INSERT statements:
- Start with parent table records before child table records (respect foreign key dependencies)
- Use meaningful, descriptive values based on the workflow description
- Include comments to explain what each INSERT represents
- Group related INSERTs together
- Ensure data integrity across all related tables
- Use proper SQL Server syntax and formatting
- Include SET IDENTITY_INSERT ON/OFF if inserting specific ID values

Output ONLY valid SQL INSERT statements that can be executed directly in SQL Server Management Studio.
Do not include any explanatory text outside of SQL comments.";
        }

        private string GetPlaceholderSchema()
        {
            return @"
-- IMPORTANT: Replace this with your actual workflow schema
-- Create a file named 'workflow-schema.sql' in your application root directory
-- Or modify this method to return your actual schema

-- Example schema structure:
-- CREATE TABLE YourWorkflowTable (
--     Id INT IDENTITY(1,1) PRIMARY KEY,
--     WorkflowName NVARCHAR(200) NOT NULL,
--     Status NVARCHAR(50),
--     CreatedDate DATETIME DEFAULT GETDATE()
-- );

-- Your actual tables should be defined here
";
        }


        private string BuildUserPrompt(string description, List<string>? steps)
        {
            var prompt = $"Generate SQL INSERT statements for the following workflow:\n\n";
            prompt += $"Workflow Description: {description}\n\n";

            if (steps != null && steps.Any())
            {
                prompt += "Identified Workflow Steps:\n";
                for (int i = 0; i < steps.Count; i++)
                {
                    prompt += $"{i + 1}. {steps[i]}\n";
                }
            }

            prompt += "\nPlease generate:\n";
            prompt += "1. INSERT statements for all necessary workflow records\n";
            prompt += "2. Ensure all foreign key relationships are properly maintained\n";
            prompt += "3. Include appropriate data for workflow steps, transitions, and any related tables\n";
            prompt += "4. Add SQL comments to explain each section of INSERTs\n";
            prompt += "5. Ensure all SQL is compatible with SQL Server 2019 or later";

            return prompt;
        }
    }
}
