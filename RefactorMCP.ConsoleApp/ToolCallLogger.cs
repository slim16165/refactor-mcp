using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

internal static class ToolCallLogger
{
    private const string LogFileEnvVar = "REFACTOR_MCP_LOG_FILE";
    private const string DisableLoggingEnvVar = "REFACTOR_MCP_DISABLE_LOGGING";
    private static string _logFile = "tool-call-log.jsonl";
    private static readonly object _fileLock = new();
    private static ILogger? _logger;
    private static bool _loggingDisabled = false;
    private static bool _initialized = false;

    public static string DefaultLogFile => _logFile;

    private static bool ParseDisableLoggingFlag(string? disableLogging)
    {
        if (string.IsNullOrEmpty(disableLogging))
            return false;
            
        // Support multiple boolean representations, case-insensitive
        var normalized = disableLogging.Trim().ToLowerInvariant();
        return normalized is "true" or "1" or "yes" or "on";
    }

    public static void InitializeLogger(ILogger logger)
    {
        _logger = logger;
        // Check if logging is disabled via environment variable
        var disableLogging = Environment.GetEnvironmentVariable(DisableLoggingEnvVar);
        _loggingDisabled = ParseDisableLoggingFlag(disableLogging);
        _initialized = true;
    }

    private static void EnsureInitialized()
    {
        if (!_initialized)
        {
            lock (_fileLock)
            {
                if (!_initialized) // Double-check inside lock
                {
                    // Lazy initialization - check environment variable if not explicitly initialized
                    var disableLogging = Environment.GetEnvironmentVariable(DisableLoggingEnvVar);
                    _loggingDisabled = ParseDisableLoggingFlag(disableLogging);
                    _initialized = true;
                }
            }
        }
    }

    public static void SetLogDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        _logFile = Path.Combine(directory, $"tool-call-log-{timestamp}-{Guid.NewGuid():N}.jsonl");
        Environment.SetEnvironmentVariable(LogFileEnvVar, _logFile);
    }

    public static void RestoreFromEnvironment()
    {
        var file = Environment.GetEnvironmentVariable(LogFileEnvVar);
        if (!string.IsNullOrEmpty(file))
            _logFile = file;
    }

    /// <summary>
    /// Starts logging a tool call with detailed tracking
    /// </summary>
    public static ToolCallScope LogToolStart(string toolName, object? parameters = null)
    {
        var callId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;
        
        // Log to structured logger
        _logger?.LogInformation("[{CallId}] TOOL_START: {ToolName} {@Parameters}", 
            callId, toolName, parameters);
            
        // Log to JSONL file
        var startRecord = new DetailedToolCallRecord
        {
            CallId = callId,
            Tool = toolName,
            Parameters = parameters,
            Timestamp = startTime,
            Status = "START",
            DurationMs = null
        };
        
        LogDetailedRecord(startRecord);
        
        return new ToolCallScope(callId, toolName, startTime, parameters);
    }

    /// <summary>
    /// Logs a simple tool call (legacy compatibility)
    /// </summary>
    public static void Log(string toolName, Dictionary<string, string?> parameters, string? logFile = null)
    {
        EnsureInitialized();
        if (_loggingDisabled) return;
        
        var file = logFile ?? DefaultLogFile;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var record = new ToolCallRecord
        {
            Tool = toolName,
            Parameters = parameters,
            Timestamp = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(record);
        AppendLineShared(file, json);
    }

    private static void LogDetailedRecord(DetailedToolCallRecord record)
    {
        EnsureInitialized();
        if (_loggingDisabled) return;
        
        var json = JsonSerializer.Serialize(record);
        AppendLineShared(_logFile, json);
    }

    private static void AppendLineShared(string path, string line)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Retry logic for file access conflicts - minimal blocking inside lock
        const int maxRetries = 3;
        const int retryDelayMs = 10; // Shorter delay to reduce lock time
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                lock (_fileLock)
                {
                    using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var sw = new StreamWriter(fs, new UTF8Encoding(false));
                    sw.WriteLine(line);
                }
                return; // Success, exit retry loop
            }
            catch (IOException ex) when (attempt < maxRetries - 1) // Catch ALL IOExceptions for retry
            {
                _logger?.LogWarning(ex, "File access conflict on {FilePath}, retry {Attempt}/{MaxRetries}", path, attempt + 1, maxRetries);
                // Sleep OUTSIDE lock to avoid blocking other threads
                Thread.Sleep(retryDelayMs * (attempt + 1)); // Exponential backoff
            }
        }
        
        // Final attempt - swallow any exception to avoid crashing, just warn
        try
        {
            lock (_fileLock)
            {
                using var finalFs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var finalSw = new StreamWriter(finalFs, new UTF8Encoding(false));
                finalSw.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            // Logging is non-critical - just warn and continue
            _logger?.LogWarning(ex, "Failed to write to log file after {MaxRetries} retries: {FilePath}", maxRetries, path);
        }
    }

    /// <summary>
    /// Scope for tracking tool call lifecycle
    /// </summary>
    public class ToolCallScope : IDisposable
    {
        private readonly string _callId;
        private readonly string _toolName;
        private readonly DateTime _startTime;
        private readonly object? _parameters;
        private string _status = "Running";
        private bool _disposed = false;

        public ToolCallScope(string callId, string toolName, DateTime startTime, object? parameters)
        {
            _callId = callId;
            _toolName = toolName;
            _startTime = startTime;
            _parameters = parameters;
        }

        public void SetStatus(string status)
        {
            _status = status;
        }

        public void SetSuccess()
        {
            _status = "Success";
        }

        public void SetError(Exception? exception = null)
        {
            _status = "Error";
            if (exception != null)
            {
                _logger?.LogError(exception, "[{_callId}] TOOL_ERROR: {_toolName}", _callId, _toolName);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                var duration = DateTime.UtcNow - _startTime;
                
                // Log to structured logger
                _logger?.LogInformation("[{_callId}] TOOL_END: {_toolName} DURATION={Duration}ms STATUS={Status} {@Parameters}", 
                    _callId, _toolName, duration.TotalMilliseconds, _status, _parameters);
                
                // Log to JSONL file
                var endRecord = new DetailedToolCallRecord
                {
                    CallId = _callId,
                    Tool = _toolName,
                    Parameters = _parameters,
                    Timestamp = DateTime.UtcNow,
                    Status = _status,
                    DurationMs = (long)duration.TotalMilliseconds
                };
                
                LogDetailedRecord(endRecord);
                
                _disposed = true;
            }
        }
    }

    public static async Task Playback(string logFilePath)
    {
        if (!File.Exists(logFilePath))
        {
            Console.Error.WriteLine($"Log file '{logFilePath}' not found");
            return;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (var line in await File.ReadAllLinesAsync(logFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            ToolCallRecord? record = null;
            try
            {
                record = JsonSerializer.Deserialize<ToolCallRecord>(line, options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Invalid log entry: {ex.Message}");
            }
            if (record != null)
                await InvokeTool(record.Tool, record.Parameters);
        }
    }

    private static async Task InvokeTool(string toolName, Dictionary<string, string?> parameters)
    {
        var method = GetToolMethod(toolName);
        if (method == null)
        {
            Console.Error.WriteLine($"Unknown tool in log: {toolName}");
            return;
        }

        var paramInfos = method.GetParameters();
        var invokeArgs = new object?[paramInfos.Length];
        for (int i = 0; i < paramInfos.Length; i++)
        {
            var p = paramInfos[i];
            parameters.TryGetValue(p.Name!, out var raw);
            if (string.IsNullOrEmpty(raw))
            {
                if (p.HasDefaultValue)
                    invokeArgs[i] = p.DefaultValue;
                else
                {
                    Console.Error.WriteLine($"Missing parameter {p.Name} for {toolName}");
                    return;
                }
            }
            else
            {
                invokeArgs[i] = ConvertInput(raw!, p.ParameterType);
            }
        }

        var result = method.Invoke(null, invokeArgs);
        if (result is Task<string> taskStr)
            Console.Error.WriteLine(await taskStr);
        else if (result is Task task)
        {
            await task;
            Console.Error.WriteLine("Done");
        }
        else if (result != null)
        {
            Console.Error.WriteLine(result.ToString());
        }
    }

    private static MethodInfo? GetToolMethod(string toolName)
    {
        return typeof(LoadSolutionTool).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(McpServerToolTypeAttribute), false).Length > 0)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0 &&
                                 m.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
    }

    private static object? ConvertInput(string value, Type targetType)
    {
        if (targetType == typeof(string))
            return value;
        if (targetType == typeof(string[]))
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (targetType == typeof(int))
            return int.Parse(value);
        if (targetType == typeof(bool))
            return bool.Parse(value);
        return Convert.ChangeType(value, targetType);
    }

    private class ToolCallRecord
    {
        public string Tool { get; set; } = string.Empty;
        public Dictionary<string, string?> Parameters { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    private class DetailedToolCallRecord
    {
        public string CallId { get; set; } = string.Empty;
        public string Tool { get; set; } = string.Empty;
        public object? Parameters { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } = string.Empty; // START, Success, Error, Timeout
        public long? DurationMs { get; set; }
    }
}

