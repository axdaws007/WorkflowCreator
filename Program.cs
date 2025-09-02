using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
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
// SEMANTIC KERNEL CONFIGURATION (CLOUD AI ONLY)
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
                logger.LogError("No valid cloud AI provider configured. Please set AI:Cloud:Provider to 'OpenAI', 'Azure', or 'Anthropic'");
                throw new InvalidOperationException("Cloud AI provider is required for workflow analysis");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure Analysis Kernel");
        throw; // Analysis kernel is critical, don't continue without it
    }

    // Add common services
    kernelBuilder.Services.AddLogging();

    var kernel = kernelBuilder.Build();
    logger.LogInformation("Analysis Kernel configured successfully");

    return kernel;
});

// ================================================================
// APPLICATION SERVICES REGISTRATION
// ================================================================

// Core workflow services
builder.Services.AddScoped<IWorkflowAnalysisService, SemanticKernelWorkflowService>();
builder.Services.AddScoped<ISqlGenerationService, ProgrammaticSqlGenerationService>(); // New programmatic service
builder.Services.AddScoped<IConnectionTestService, SemanticKernelConnectionTestService>(); // Updated connection test service
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
            sqlGeneration = new
            {
                isConnected = true, // Always available for programmatic generation
                responseTimeMs = 0,
                provider = "Programmatic"
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
    return Results.Ok(new
    {
        cloudProvider = configuration["AI:Cloud:Provider"] ?? "Not configured",
        sqlProvider = "Programmatic Generator",
        isCloudOnlySetup = true,
        environment = app.Environment.EnvironmentName,
        version = "2.1.0"
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
startupLogger.LogInformation("SQL Generation: Programmatic (No LLM required)");
startupLogger.LogInformation("Setup Type: Cloud AI + Programmatic SQL Generation");

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
        startupLogger.LogInformation("  SQL Generation: Always Available (Programmatic)");

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
    logger.LogError("Anthropic provider not yet implemented");
    throw new NotImplementedException("Anthropic provider support is not yet implemented");
}