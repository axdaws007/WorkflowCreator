using System.Text;
using System.Text.Json;
using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    public class OllamaService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OllamaService> _logger;
        private readonly string _baseUrl;
        private readonly string _model;

        public OllamaService(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
            _model = configuration["Ollama:Model"] ?? "codellama:7b";

            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // Ollama can take time for complex requests
        }

        public async Task<(bool Success, string? GeneratedSql, string? ErrorMessage)> GenerateSqlAsync(string systemPrompt, string userPrompt)
        {
            try
            {
                var fullPrompt = $"{systemPrompt}\n\n{userPrompt}";

                // Check approximate token count (rough estimate: 1 token ≈ 4 characters)
                var estimatedTokens = fullPrompt.Length / 4;
                var maxTokens = _configuration.GetValue<int>("Ollama:MaxTokens", 3000);
                var contextWindow = _configuration.GetValue<int>("Ollama:ContextWindow", 4096);

                if (estimatedTokens > (contextWindow - maxTokens))
                {
                    _logger.LogWarning($"Input prompt is large (~{estimatedTokens} tokens). May exceed context window of {contextWindow} tokens.");
                }

                var request = new OllamaRequest
                {
                    model = _model,
                    prompt = fullPrompt,
                    stream = false,
                    options = new OllamaOptions
                    {
                        temperature = _configuration.GetValue<double>("Ollama:Temperature", 0.3),
                        num_predict = maxTokens
                    }
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogInformation($"Sending request to Ollama API using model {_model}...");

                var response = await _httpClient.PostAsync("/api/generate", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseContent);

                    if (ollamaResponse != null && !string.IsNullOrEmpty(ollamaResponse.response))
                    {
                        _logger.LogInformation($"Successfully received response from Ollama (tokens used: {ollamaResponse.eval_count})");

                        // Extract SQL from the response (clean up any non-SQL text)
                        var sql = ExtractSqlFromResponse(ollamaResponse.response);

                        return (true, sql, null);
                    }

                    return (false, null, "Empty response from Ollama");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Ollama API error: {response.StatusCode} - {error}");
                    return (false, null, $"Ollama API error: {response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to connect to Ollama");
                return (false, null, $"Failed to connect to Ollama at {_baseUrl}. Please ensure Ollama is running.");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request to Ollama timed out");
                return (false, null, "Request timed out. The workflow might be too complex.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error calling Ollama");
                return (false, null, $"Unexpected error: {ex.Message}");
            }
        }

        public async Task<(bool IsConnected, string Message)> TestConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/tags");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return (true, $"Successfully connected to Ollama at {_baseUrl}. Available models: {content}");
                }

                return (false, $"Ollama responded with status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to connect to Ollama at {_baseUrl}: {ex.Message}");
            }
        }

        private string ExtractSqlFromResponse(string response)
        {
            // Clean the response to ensure only SQL statements remain
            var lines = response.Split('\n');
            var sqlLines = new List<string>();
            bool inSqlBlock = false;

            foreach (var line in lines)
            {
                // Check if this looks like SQL
                var trimmedLine = line.Trim();

                // Skip markdown code blocks markers
                if (trimmedLine.StartsWith("```"))
                {
                    inSqlBlock = !inSqlBlock;
                    continue;
                }

                // Include lines that look like SQL or comments
                if (inSqlBlock ||
                    trimmedLine.StartsWith("--") ||
                    trimmedLine.StartsWith("/*") ||
                    trimmedLine.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("DROP", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("USE", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("GO", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("END", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("IF", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("EXEC", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.Contains("(") ||
                    trimmedLine.Contains(")") ||
                    trimmedLine.Contains(";"))
                {
                    sqlLines.Add(line);
                }
                else if (sqlLines.Count > 0 && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    // If we've started collecting SQL and this line is indented or continues a statement
                    if (line.StartsWith(" ") || line.StartsWith("\t") || !char.IsLetter(trimmedLine[0]))
                    {
                        sqlLines.Add(line);
                    }
                }
            }

            return string.Join("\n", sqlLines);
        }
    }
}
