using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using WorkflowCreator.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

builder.Services.AddKeyedSingleton<Kernel>("analysis", (serviceProvider, key) =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    var config = serviceProvider.GetRequiredService<IConfiguration>();

    var provider = config["AI:Cloud:Provider"]?.ToLower();

    switch (provider)
    {
        case "openai":
            var apiKey = config["AI:Cloud:OpenAI:ApiKey"];
            var modelId = config["AI:Cloud:OpenAI:ModelId"] ?? "gpt-4o-mini";

            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("OpenAI API key not configured");

            kernelBuilder.AddOpenAIChatCompletion(
                modelId: modelId,
                apiKey: apiKey);
            break;

        case "azure":
            var azureEndpoint = config["AI:Cloud:Azure:Endpoint"];
            var azureApiKey = config["AI:Cloud:Azure:ApiKey"];
            var azureModel = config["AI:Cloud:Azure:ModelId"] ?? "gpt-4";

            kernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: azureModel,
                endpoint: azureEndpoint!,
                apiKey: azureApiKey!);
            break;

        default:
            // Fallback to local Ollama for analysis
            var endpoint = config["AI:Local:Endpoint"] ?? "http://localhost:11434";
            var model = config["AI:Local:AnalysisModel"] ?? "llama3:8b";


            kernelBuilder.AddOllamaChatCompletion(
                modelId: model,
                endpoint: new Uri(endpoint));
            break;
    }

    // Add logging and telemetry
    kernelBuilder.Services.AddLogging();

    var kernel = kernelBuilder.Build();

    // Configure retry policy for analysis kernel
    //kernel.DefaultRequestSettings = new OpenAIRequestSettings
    //{
    //    Temperature = config.GetValue<double>("AI:Cloud:Temperature", 0.7),
    //    MaxTokens = config.GetValue<int>("AI:Cloud:MaxTokens", 2000),
    //    TopP = 0.9,
    //    FrequencyPenalty = 0.0,
    //    PresencePenalty = 0.0
    //};

    return kernel;
});


// SQL Generation Kernel (Local AI) - for SQL generation  
builder.Services.AddKeyedSingleton<Kernel>("sql", (serviceProvider, key) =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    var config = serviceProvider.GetRequiredService<IConfiguration>();

    var endpoint = config["AI:Local:Endpoint"] ?? "http://localhost:11434";
    var model = config["AI:Local:ModelId"] ?? "codellama:7b";

    kernelBuilder.AddOllamaChatCompletion(
        modelId: model,
        endpoint: new Uri(endpoint));

    kernelBuilder.Services.AddLogging();

    var kernel = kernelBuilder.Build();

    // Optimized settings for SQL generation
    //kernel.DefaultRequestSettings = new OllamaRequestSettings
    //{
    //    Temperature = config.GetValue<double>("AI:Local:Temperature", 0.1), // Low temp for consistent SQL
    //    MaxTokens = config.GetValue<int>("AI:Local:MaxTokens", 3000),
    //    TopK = 10,
    //    TopP = 0.1,
    //    RepeatPenalty = 1.1
    //};

    return kernel;
});

// Advanced Kernel (with function calling capabilities)
builder.Services.AddKeyedSingleton<Kernel>("advanced", (serviceProvider, key) =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    var config = serviceProvider.GetRequiredService<IConfiguration>();

    // Use the best available model for function calling
    var provider = config["AI:Cloud:Provider"]?.ToLower();

    if (provider == "openai")
    {
        var apiKey = config["AI:Cloud:OpenAI:ApiKey"];
        var modelId = config["AI:Cloud:OpenAI:ModelId"] ?? "gpt-4";  // GPT-4 for function calling

        kernelBuilder.AddOpenAIChatCompletion(
            modelId: modelId,
            apiKey: apiKey!);
    }
    else
    {
        // Fallback to local model with function calling support
        var endpoint = config["AI:Local:Endpoint"] ?? "http://localhost:11434";
        var model = config["AI:Local:FunctionModel"] ?? "llama3:8b";

        kernelBuilder.AddOllamaChatCompletion(
            modelId: model,
            endpoint: new Uri(endpoint));
    }

    kernelBuilder.Services.AddLogging();

    var kernel = kernelBuilder.Build();

    // Settings optimized for function calling
    //kernel.DefaultRequestSettings = new OpenAIRequestSettings
    //{
    //    Temperature = 0.3,
    //    MaxTokens = 1500,
    //    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions  // Enable automatic function calling
    //};

    return kernel;
});

builder.Services.AddScoped<IWorkflowAnalysisService, SemanticKernelWorkflowService>();
builder.Services.AddScoped<ISqlGenerationService, SemanticKernelSqlService>();
builder.Services.AddScoped<IConnectionTestService, SemanticKernelConnectionTestService>();

builder.Services.AddScoped<IWorkflowService, EnhancedWorkflowService>();


// Configure Semantic Kernel instances
var aiConfig = builder.Configuration.GetSection("AI");


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Workflow}/{action=Index}/{id?}");

app.Run();
