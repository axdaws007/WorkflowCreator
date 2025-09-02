using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WorkflowCreator.Services
{
    /// <summary>
    /// Health check implementation for monitoring cloud AI services.
    /// Updated for cloud-only setup with programmatic SQL generation.
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
                _logger.LogDebug("Starting cloud AI services health check");

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
                    ["SqlGenerationAvailable"] = true, // Always true for programmatic generation
                    ["CloudResponseTimeMs"] = health.CloudService.ResponseTimeMs,
                    ["SqlGenerationTimeMs"] = 0, // Instant for programmatic
                    ["CloudProvider"] = health.CloudService.Provider,
                    ["SqlProvider"] = "Programmatic Generator",
                    ["WarningCount"] = health.Warnings.Count,
                    ["CheckDurationMs"] = checkDuration.TotalMilliseconds,
                    ["LastChecked"] = checkStartTime,
                    ["SetupType"] = "Cloud-Only"
                };

                if (health.IsHealthy)
                {
                    var message = $"Cloud AI operational ({health.CloudService.ResponseTimeMs}ms), SQL generation ready (0ms)";
                    _logger.LogDebug("Health check passed: {message}", message);
                    return HealthCheckResult.Healthy(message, healthData);
                }
                else
                {
                    // In cloud-only setup, we only depend on cloud AI
                    var message = $"Cloud AI service issues detected: {health.CloudService.Message}";

                    if (health.Warnings.Any())
                    {
                        message += $". Warnings: {string.Join("; ", health.Warnings)}";
                    }

                    if (!health.CloudService.IsConnected)
                    {
                        _logger.LogError("Health check failed: {message}", message);
                        return HealthCheckResult.Unhealthy(message, null, healthData);
                    }
                    else
                    {
                        _logger.LogWarning("Health check degraded: {message}", message);
                        return HealthCheckResult.Degraded(message, null, healthData);
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                var message = "Cloud AI services health check timed out";
                _logger.LogWarning("Health check timed out after {duration}ms",
                    (DateTime.UtcNow - checkStartTime).TotalMilliseconds);

                return HealthCheckResult.Unhealthy(message, ex, new Dictionary<string, object>
                {
                    ["TimeoutMs"] = (DateTime.UtcNow - checkStartTime).TotalMilliseconds,
                    ["CheckStartTime"] = checkStartTime,
                    ["SetupType"] = "Cloud-Only"
                });
            }
            catch (Exception ex)
            {
                var message = $"Cloud AI services health check failed: {ex.Message}";
                _logger.LogError(ex, "Health check failed");

                return HealthCheckResult.Unhealthy(message, ex, new Dictionary<string, object>
                {
                    ["ErrorType"] = ex.GetType().Name,
                    ["ErrorMessage"] = ex.Message,
                    ["CheckDurationMs"] = (DateTime.UtcNow - checkStartTime).TotalMilliseconds,
                    ["SetupType"] = "Cloud-Only"
                });
            }
        }
    }
}