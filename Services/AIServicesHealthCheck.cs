using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Health check implementation for monitoring AI services.
    /// Integrates with ASP.NET Core health check system to provide real-time status monitoring.
    /// </summary>
    public class AIServicesHealthCheck : IHealthCheck
    {
        private readonly IConnectionTestService _connectionTestService;
        private readonly ILogger<AIServicesHealthCheck> _logger;
        private readonly IConfiguration _configuration;

        public AIServicesHealthCheck(
            IConnectionTestService connectionTestService,
            ILogger<AIServicesHealthCheck> logger,
            IConfiguration configuration)
        {
            _connectionTestService = connectionTestService;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var checkStartTime = DateTime.UtcNow;

            try
            {
                _logger.LogDebug("Starting AI services health check");

                // Get timeout from configuration
                var timeoutMs = _configuration.GetValue<int>("HealthChecks:TimeoutSeconds", 10) * 1000;
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                var health = await _connectionTestService.TestAllConnectionsAsync();

                var checkDuration = DateTime.UtcNow - checkStartTime;
                var healthData = new Dictionary<string, object>
                {
                    ["CloudServiceConnected"] = health.CloudService.IsConnected,
                    ["LocalServiceConnected"] = health.LocalService.IsConnected,
                    ["CloudResponseTimeMs"] = health.CloudService.ResponseTimeMs,
                    ["LocalResponseTimeMs"] = health.LocalService.ResponseTimeMs,
                    ["CloudProvider"] = health.CloudService.Provider,
                    ["LocalProvider"] = health.LocalService.Provider,
                    ["WarningCount"] = health.Warnings.Count,
                    ["CheckDurationMs"] = checkDuration.TotalMilliseconds,
                    ["LastChecked"] = checkStartTime
                };

                if (health.IsHealthy)
                {
                    var message = $"All AI services operational (Cloud: {health.CloudService.ResponseTimeMs}ms, Local: {health.LocalService.ResponseTimeMs}ms)";
                    _logger.LogDebug("Health check passed: {message}", message);
                    return HealthCheckResult.Healthy(message, healthData);
                }
                else if (health.CloudService.IsConnected || health.LocalService.IsConnected)
                {
                    var workingServices = new List<string>();
                    var failedServices = new List<string>();

                    if (health.CloudService.IsConnected)
                        workingServices.Add($"Cloud ({health.CloudService.Provider})");
                    else
                        failedServices.Add($"Cloud ({health.CloudService.Provider})");

                    if (health.LocalService.IsConnected)
                        workingServices.Add($"Local ({health.LocalService.Provider})");
                    else
                        failedServices.Add($"Local ({health.LocalService.Provider})");

                    var message = $"Partial AI services available. Working: {string.Join(", ", workingServices)}. Failed: {string.Join(", ", failedServices)}";

                    if (health.Warnings.Any())
                    {
                        message += $". Warnings: {string.Join("; ", health.Warnings)}";
                    }

                    _logger.LogWarning("Health check degraded: {message}", message);
                    return HealthCheckResult.Degraded(message, null, healthData);
                }
                else
                {
                    var message = $"No AI services operational. Cloud: {health.CloudService.Message}. Local: {health.LocalService.Message}";

                    if (health.Warnings.Any())
                    {
                        message += $". Additional issues: {string.Join("; ", health.Warnings)}";
                    }

                    _logger.LogError("Health check failed: {message}", message);
                    return HealthCheckResult.Unhealthy(message, null, healthData);
                }
            }
            catch (OperationCanceledException ex)
            {
                var message = "AI services health check timed out";
                _logger.LogWarning("Health check timed out after {duration}ms",
                    (DateTime.UtcNow - checkStartTime).TotalMilliseconds);

                return HealthCheckResult.Unhealthy(message, ex, new Dictionary<string, object>
                {
                    ["TimeoutMs"] = (DateTime.UtcNow - checkStartTime).TotalMilliseconds,
                    ["CheckStartTime"] = checkStartTime
                });
            }
            catch (Exception ex)
            {
                var message = $"AI services health check failed: {ex.Message}";
                _logger.LogError(ex, "Health check failed");

                return HealthCheckResult.Unhealthy(message, ex, new Dictionary<string, object>
                {
                    ["ErrorType"] = ex.GetType().Name,
                    ["ErrorMessage"] = ex.Message,
                    ["CheckDurationMs"] = (DateTime.UtcNow - checkStartTime).TotalMilliseconds
                });
            }
        }
    }
}