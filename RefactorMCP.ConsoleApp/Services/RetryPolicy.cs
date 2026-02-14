using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace RefactorMCP.ConsoleApp.Services;

/// <summary>
/// Progress reporting for solution loading operations
/// </summary>
public class SolutionLoadProgress
{
    public string Phase { get; set; } = string.Empty;
    public int PercentComplete { get; set; }
    public string? Details { get; set; }
}

/// <summary>
/// Retry policy with exponential backoff for resilient operations
/// </summary>
public class RetryPolicy
{
    private readonly ILogger<RetryPolicy> _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;

    public RetryPolicy(ILogger<RetryPolicy> logger, int maxRetries = 3, TimeSpan? baseDelay = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// Executes an operation with retry logic and exponential backoff
    /// </summary>
    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation, 
        string operationName,
        CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                _logger.LogInformation("[{OperationName}] Attempt {Attempt}/{MaxRetries} starting", 
                    operationName, attempt, _maxRetries);

                var result = await operation();
                
                stopwatch.Stop();
                _logger.LogInformation("[{OperationName}] Completed in {Duration}ms on attempt {Attempt}", 
                    operationName, stopwatch.ElapsedMilliseconds, attempt);
                    
                return result;
            }
            catch (Exception ex) when (attempt < _maxRetries && IsRetryableException(ex))
            {
                lastException = ex;
                var delay = CalculateDelay(attempt);
                
                _logger.LogWarning(ex, "[{OperationName}] Failed on attempt {Attempt}, retrying in {Delay}s", 
                    operationName, attempt, delay.TotalSeconds);
                
                await Task.Delay(delay, cancellationToken);
            }
        }

        // Last attempt - let exception propagate or throw the last one
        _logger.LogError("[{OperationName}] Failed after {MaxRetries} attempts", operationName, _maxRetries);
        throw lastException ?? new InvalidOperationException($"Operation {operationName} failed after {_maxRetries} attempts");
    }

    /// <summary>
    /// Executes an operation with retry logic (non-generic version)
    /// </summary>
    public async Task ExecuteWithRetryAsync(
        Func<Task> operation, 
        string operationName,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () => 
        {
            await operation();
            return true;
        }, operationName, cancellationToken);
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        // Exponential backoff: 2s, 4s, 8s, 16s...
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1) * _baseDelay.TotalSeconds);
        
        // Cap at 30 seconds to avoid excessive delays
        return delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
    }

    private static bool IsRetryableException(Exception ex)
    {
        // Determine if an exception is retryable
        return ex is TimeoutException ||
               ex is OperationCanceledException ||
               (ex is InvalidOperationException && ex.Message.Contains("timeout")) ||
               (ex is System.IO.IOException && ex.Message.Contains("being used")) ||
               (ex.Message.Contains("MSBuild") && ex.Message.Contains("failed"));
    }
}

/// <summary>
/// Configuration for solution loading operations
/// </summary>
public class SolutionLoaderConfiguration
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);
    public int MaxRetries { get; set; } = 3;
    public bool EnableProgressLogging { get; set; } = true;
    public bool ValidateSolutionAfterLoad { get; set; } = true;
    public string? ForceMsBuildVersion { get; set; }
}
