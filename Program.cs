using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Threading;
using WorkflowCreator.Services;
using WorkflowCreator.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ================================================================
// CORE SERVICES CONFIGURATION
// ================================================================

// Add core MVC services
builder.Services.AddControllersWithViews();

builder.Services.ConfigureHttpClientDefaults(builder =>
{
    builder.ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(15);
    });
}); 

builder.Services.AddHttpClient();

builder.Services.AddMemoryCache();

// Add health checks for monitoring
builder.Services.AddHealthChecks()
    .AddCheck<AIServicesHealthCheck>("ai-services");

// Add logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddFilter("Microsoft.SemanticKernel", LogLevel.Information);
    builder.Logging.AddFilter("WorkflowCreator", LogLevel.Debug);
}

// ================================================================
// SEMANTIC KERNEL CONFIGURATION
// ================================================================

// ANALYSIS KERNEL (Cloud AI for Natural Language Understanding)
builder.Services.AddKeyedSingleton<Kernel>("analysis", (serviceProvider, key) =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

    var provider = config["AI:Cloud:Provider"]?.ToLower();
    logger.LogInformation("Configuring Analysis Kernel with provider: {provider}", provider ?? "none");

    try
    {
        switch (provider)
        {
            case "openai":
                ConfigureOpenAIAnalysis(kernelBuilder, config, logger);
                break;

            case "azure":
                ConfigureAzureOpenAIAnalysis(kernelBuilder, config, logger);
                break;

            case "anthropic":
                ConfigureAnthropicAnalysis(kernelBuilder, config, logger);
                break;

            default:
                // Fallback to local Ollama for analysis if no cloud provider configured
                logger.LogWarning("No cloud AI provider configured, falling back to local Ollama for analysis");
                ConfigureOllamaAnalysis(kernelBuilder, config, logger);
                break;
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure Analysis Kernel, falling back to Ollama");
        ConfigureOllamaAnalysis(kernelBuilder, config, logger);
    }

    // Add common services
    kernelBuilder.Services.AddLogging();

    var kernel = kernelBuilder.Build();
    logger.LogInformation("Analysis Kernel configured successfully");

    return kernel;
});

// SQL GENERATION KERNEL (Local AI for Code Generation)
builder.Services.AddKeyedSingleton<Kernel>("sql", (serviceProvider, key) =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Configuring SQL Generation Kernel with Ollama");

    try
    {
        ConfigureOllamaSQL(kernelBuilder, config, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure SQL Generation Kernel");
        throw; // SQL kernel is critical, don't continue without it
    }
    
    kernelBuilder.Services.AddLogging();

    var kernel = kernelBuilder.Build();
    logger.LogInformation("SQL Generation Kernel configured successfully");

    return kernel;
});

// ADVANCED KERNEL (Function Calling and Complex Operations)
builder.Services.AddKeyedSingleton<Kernel>("advanced", (serviceProvider, key) =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

    var provider = config["AI:Cloud:Provider"]?.ToLower();
    logger.LogInformation("Configuring Advanced Kernel with provider: {provider}", provider ?? "local");

    try
    {
        if (provider == "openai")
        {
            // Use OpenAI for function calling capabilities
            ConfigureOpenAIAdvanced(kernelBuilder, config, logger);
        }
        else
        {
            // Fallback to local model with function calling support
            ConfigureOllamaAdvanced(kernelBuilder, config, logger);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure Advanced Kernel, falling back to basic Ollama");
        ConfigureOllamaAdvanced(kernelBuilder, config, logger);
    }

    kernelBuilder.Services.AddLogging();

    var kernel = kernelBuilder.Build();
    logger.LogInformation("Advanced Kernel configured successfully");

    return kernel;
});

// ================================================================
// APPLICATION SERVICES REGISTRATION
// ================================================================

// Core workflow services
builder.Services.AddScoped<IWorkflowAnalysisService, SemanticKernelWorkflowService>();
builder.Services.AddScoped<ISqlGenerationService, SemanticKernelSqlService>();
builder.Services.AddScoped<IConnectionTestService, SemanticKernelConnectionTestService>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();

// Health check service
builder.Services.AddScoped<AIServicesHealthCheck>();

// Configuration validation
builder.Services.AddSingleton<IConfigurationValidator, ConfigurationValidator>();

// ================================================================
// CONFIGURATION VALIDATION
// ================================================================

// Validate configuration on startup
var config = builder.Configuration;
var tempServiceProvider = builder.Services.BuildServiceProvider();
var configValidator = tempServiceProvider.GetRequiredService<IConfigurationValidator>();
var validationResult = configValidator.ValidateConfiguration();

if (!validationResult.IsValid)
{
    var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Program>();
    logger.LogWarning("Configuration validation found issues:");
    foreach (var warning in validationResult.Warnings)
    {
        logger.LogWarning("  - {warning}", warning);
    }

    foreach (var error in validationResult.Errors)
    {
        logger.LogError("  - {error}", error);
    }

    if (validationResult.Errors.Any())
    {
        logger.LogError("Critical configuration errors found. Application may not function correctly.");
    }
}

// ================================================================
// BUILD APPLICATION
// ================================================================

var app = builder.Build();

// ================================================================
// MIDDLEWARE PIPELINE CONFIGURATION
// ================================================================

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

// Security and performance middleware
app.UseHttpsRedirection();
app.UseStaticFiles();

// Add security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    await next();
});

// Routing and authorization
app.UseRouting();
app.UseAuthorization();

// Health checks endpoint
app.MapHealthChecks("/health");

// API endpoints for AJAX calls
app.MapGet("/api/system/status", async (IConnectionTestService connectionService) =>
{
    try
    {
        var health = await connectionService.TestAllConnectionsAsync();
        return Results.Ok(new
        {
            isHealthy = health.IsHealthy,
            cloudService = new
            {
                isConnected = health.CloudService.IsConnected,
                responseTimeMs = health.CloudService.ResponseTimeMs,
                provider = health.CloudService.Provider
            },
            localService = new
            {
                isConnected = health.LocalService.IsConnected,
                responseTimeMs = health.LocalService.ResponseTimeMs,
                provider = health.LocalService.Provider
            },
            warnings = health.Warnings,
            lastChecked = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// Configuration info endpoint
app.MapGet("/api/system/config", (IConfiguration configuration) =>
{
    var isHybridSetup = !string.IsNullOrEmpty(configuration["AI:Cloud:Provider"]) &&
                       !string.IsNullOrEmpty(configuration["AI:Local:ModelId"]);

    return Results.Ok(new
    {
        cloudProvider = configuration["AI:Cloud:Provider"] ?? "Not configured",
        localProvider = configuration["AI:Local:Provider"] ?? "Ollama",
        isHybridSetup = isHybridSetup,
        environment = app.Environment.EnvironmentName,
        version = "2.0.0"
    });
});

// Main application routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Workflow}/{action=Index}/{id?}");

// ================================================================
// STARTUP LOGGING AND DIAGNOSTICS
// ================================================================

var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("=== AI Workflow Creator Starting ===");
startupLogger.LogInformation("Environment: {environment}", app.Environment.EnvironmentName);
startupLogger.LogInformation("Cloud AI Provider: {provider}",
    config["AI:Cloud:Provider"] ?? "Not configured");
startupLogger.LogInformation("Local AI Provider: {provider}",
    config["AI:Local:Provider"] ?? "Ollama");
startupLogger.LogInformation("Hybrid Setup: {isHybrid}",
    !string.IsNullOrEmpty(config["AI:Cloud:Provider"]) &&
    !string.IsNullOrEmpty(config["AI:Local:ModelId"]));

// Test AI services on startup (in background)
_ = Task.Run(async () =>
{
    await Task.Delay(2000); // Give services time to initialize

    try
    {
        var connectionService = app.Services.GetRequiredService<IConnectionTestService>();
        var health = await connectionService.TestAllConnectionsAsync();

        startupLogger.LogInformation("Startup AI Services Health Check:");
        startupLogger.LogInformation("  Cloud AI: {status} ({time}ms)",
            health.CloudService.IsConnected ? "Connected" : "Failed",
            health.CloudService.ResponseTimeMs);
        startupLogger.LogInformation("  Local AI: {status} ({time}ms)",
            health.LocalService.IsConnected ? "Connected" : "Failed",
            health.LocalService.ResponseTimeMs);

        if (health.Warnings.Any())
        {
            startupLogger.LogWarning("AI Services Warnings:");
            foreach (var warning in health.Warnings)
            {
                startupLogger.LogWarning("  - {warning}", warning);
            }
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to perform startup AI health check");
    }
});

startupLogger.LogInformation("=== Application Started Successfully ===");

// ================================================================
// RUN APPLICATION
// ================================================================

app.Run();

// ================================================================
// KERNEL CONFIGURATION METHODS
// ================================================================

static void ConfigureOpenAIAnalysis(IKernelBuilder kernelBuilder, IConfiguration config, ILogger logger)
{
    var apiKey = config["AI:Cloud:OpenAI:ApiKey"];
    var modelId = config["AI:Cloud:OpenAI:ModelId"] ?? "gpt-4o-mini";
    var orgId = config["AI:Cloud:OpenAI:OrganizationId"];

    if (string.IsNullOrEmpty(apiKey))
        throw new InvalidOperationException("OpenAI API key not configured");

    logger.LogInformation("Configuring OpenAI with model: {model}", modelId);

    var openAIConfig = new OpenAIPromptExecutionSettings
    {
        Temperature = config.GetValue<double>("AI:Cloud:Temperature", 0.7),
        MaxTokens = config.GetValue<int>("AI:Cloud:MaxTokens", 2000),
        TopP = 0.9,
        FrequencyPenalty = 0.0,
        PresencePenalty = 0.0
    };

    kernelBuilder.AddOpenAIChatCompletion(
        modelId: modelId,
        apiKey: apiKey,
        orgId: orgId);
}

static void ConfigureAzureOpenAIAnalysis(IKernelBuilder kernelBuilder, IConfiguration config, ILogger logger)
{
    var endpoint = config["AI:Cloud:Azure:Endpoint"];
    var apiKey = config["AI:Cloud:Azure:ApiKey"];
    var modelId = config["AI:Cloud:Azure:ModelId"] ?? "gpt-4";

    if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
        throw new InvalidOperationException("Azure OpenAI endpoint and API key must be configured");

    logger.LogInformation("Configuring Azure OpenAI with deployment: {model}", modelId);

    kernelBuilder.AddAzureOpenAIChatCompletion(
        deploymentName: modelId,
        endpoint: endpoint,
        apiKey: apiKey);
}

static void ConfigureAnthropicAnalysis(IKernelBuilder kernelBuilder, IConfiguration config, ILogger logger)
{
    // Placeholder for Anthropic configuration
    // Would need Anthropic connector package
    logger.LogWarning("Anthropic provider not yet implemented, falling back to Ollama");
    ConfigureOllamaAnalysis(kernelBuilder, config, logger);
}

static void ConfigureOllamaAnalysis(IKernelBuilder kernelBuilder, IConfiguration config, ILogger logger)
{
    var endpoint = config["AI:Local:Endpoint"] ?? "http://localhost:11434";
    var model = config["AI:Local:AnalysisModel"] ?? "llama3:8b";

    logger.LogInformation("Configuring Ollama analysis with model: {model} at {endpoint}", model, endpoint);

    kernelBuilder.AddOllamaChatCompletion(
        modelId: model,
        endpoint: new Uri(endpoint));
}

static void ConfigureOllamaSQL(IKernelBuilder kernelBuilder, IConfiguration config, ILogger logger)
{
    var endpoint = config["AI:Local:Endpoint"] ?? "http://localhost:11434";
    var model = config["AI:Local:ModelId"] ?? "codellama:7b";

    logger.LogInformation("Configuring Ollama SQL generation with model: {model} at {endpoint}", model, endpoint);

    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(endpoint),
        Timeout = TimeSpan.FromMilliseconds(config.GetValue<int>("AI:Performance:SqlGenerationTimeout", 600000))
    }; 
    
    kernelBuilder.AddOllamaChatCompletion(
        modelId: model,
        httpClient: httpClient);
}

static void ConfigureOpenAIAdvanced(IKernelBuilder kernelBuilder, IConfiguration config, ILogger logger)
{
    var apiKey = config["AI:Cloud:OpenAI:ApiKey"];
    var modelId = config["AI:Cloud:OpenAI:ModelId"] ?? "gpt-4";

    logger.LogInformation("Configuring OpenAI advanced kernel with model: {model}", modelId);

    var advancedConfig = new OpenAIPromptExecutionSettings
    {
        Temperature = 0.3,
        MaxTokens = 1500,
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
    };

    kernelBuilder.AddOpenAIChatCompletion(
        modelId: modelId,
        apiKey: apiKey!);
}

static void ConfigureOllamaAdvanced(IKernelBuilder kernelBuilder, IConfiguration config, ILogger logger)
{
    var endpoint = config["AI:Local:Endpoint"] ?? "http://localhost:11434";
    var model = config["AI:Local:FunctionModel"] ?? "llama3:8b";

    logger.LogInformation("Configuring Ollama advanced kernel with model: {model} at {endpoint}", model, endpoint);

    kernelBuilder.AddOllamaChatCompletion(
        modelId: model,
        endpoint: new Uri(endpoint));
}
