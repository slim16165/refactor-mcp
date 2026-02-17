using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using ModelContextProtocol.Server;

namespace RefactorMCP.ConsoleApp.Infrastructure;

/// <summary>
/// Metadata for refactoring tool discovery and documentation
/// </summary>
public class RefactoringToolMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<ParameterMetadata> Parameters { get; set; } = new();
    public List<string> Examples { get; set; } = new();
    public string? MinimumVersion { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsExperimental { get; set; }
    public string? DocumentationUrl { get; set; }
}

/// <summary>
/// Parameter metadata for tool discovery
/// </summary>
public class ParameterMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public List<string> AllowedValues { get; set; } = new();
    public string? ValidationPattern { get; set; }
    public string? Example { get; set; }
}

/// <summary>
/// Attribute for defining tool metadata
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class RefactoringToolMetadataAttribute : Attribute
{
    public string Category { get; set; } = string.Empty;
    public string[] Examples { get; set; } = Array.Empty<string>();
    public string MinimumVersion { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public bool IsExperimental { get; set; }
    public string DocumentationUrl { get; set; } = string.Empty;
}

/// <summary>
/// Attribute for defining parameter metadata
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public class RefactoringParameterAttribute : Attribute
{
    public string Type { get; set; } = "string";
    public bool IsRequired { get; set; } = true;
    public string DefaultValue { get; set; } = string.Empty;
    public string[] AllowedValues { get; set; } = Array.Empty<string>();
    public string ValidationPattern { get; set; } = string.Empty;
    public string Example { get; set; } = string.Empty;
}

/// <summary>
/// Service for discovering and providing tool metadata
/// </summary>
public static class ToolDiscoveryService
{
    /// <summary>
    /// Get metadata for all refactoring tools
    /// </summary>
    public static List<RefactoringToolMetadata> GetAllToolMetadata()
    {
        var tools = typeof(ToolDiscoveryService).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(McpServerToolTypeAttribute), false).Length > 0);

        var metadata = new List<RefactoringToolMetadata>();
        
        foreach (var toolType in tools)
        {
            var toolMethods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0);
            
            foreach (var method in toolMethods)
            {
                metadata.Add(GetToolMetadata(toolType, method));
            }
        }

        return metadata;
    }

    /// <summary>
    /// Get metadata for a specific tool
    /// </summary>
    public static RefactoringToolMetadata? GetToolMetadata(string toolName)
    {
        var toolMethod = typeof(ToolDiscoveryService).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(McpServerToolTypeAttribute), false).Length > 0)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0 &&
                                m.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));

        if (toolMethod == null)
            return null;

        var toolType = toolMethod.DeclaringType!;
        return GetToolMetadata(toolType, toolMethod);
    }

    private static RefactoringToolMetadata GetToolMetadata(Type toolType, MethodInfo method)
    {
        var toolAttribute = method.GetCustomAttributes(typeof(McpServerToolAttribute), false)
            .FirstOrDefault() as McpServerToolAttribute;

        var descriptionAttribute = method.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .FirstOrDefault() as DescriptionAttribute;

        var metadataAttribute = toolType.GetCustomAttributes(typeof(RefactoringToolMetadataAttribute), false)
            .FirstOrDefault() as RefactoringToolMetadataAttribute;

        return new RefactoringToolMetadata
        {
            Name = toolAttribute?.Name ?? method.Name,
            Description = descriptionAttribute?.Description ?? string.Empty,
            Category = metadataAttribute?.Category ?? "General",
            Parameters = GetParameterMetadata(method),
            Examples = metadataAttribute?.Examples?.ToList() ?? new List<string>(),
            MinimumVersion = metadataAttribute?.MinimumVersion,
            Tags = metadataAttribute?.Tags?.ToList() ?? new List<string>(),
            IsExperimental = metadataAttribute?.IsExperimental ?? false,
            DocumentationUrl = metadataAttribute?.DocumentationUrl
        };
    }

    private static List<ParameterMetadata> GetParameterMetadata(MethodInfo method)
    {
        return method.GetParameters()
            .Select(p => new ParameterMetadata
            {
                Name = p.Name ?? string.Empty,
                Type = GetParameterType(p),
                Description = GetParameterDescription(p),
                IsRequired = GetParameterRequired(p),
                DefaultValue = GetParameterDefaultValue(p),
                AllowedValues = GetParameterAllowedValues(p),
                ValidationPattern = GetParameterValidationPattern(p),
                Example = GetParameterExample(p)
            })
            .ToList();
    }

    private static string GetParameterType(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttributes(typeof(RefactoringParameterAttribute), false)
            .FirstOrDefault() as RefactoringParameterAttribute;
        return attribute?.Type ?? parameter.ParameterType.Name.ToLowerInvariant();
    }

    private static string GetParameterDescription(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .FirstOrDefault() as DescriptionAttribute;
        return attribute?.Description ?? string.Empty;
    }

    private static bool GetParameterRequired(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttributes(typeof(RefactoringParameterAttribute), false)
            .FirstOrDefault() as RefactoringParameterAttribute;
        return attribute?.IsRequired ?? true;
    }

    private static string? GetParameterDefaultValue(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttributes(typeof(RefactoringParameterAttribute), false)
            .FirstOrDefault() as RefactoringParameterAttribute;
        return string.IsNullOrEmpty(attribute?.DefaultValue) ? null : attribute.DefaultValue;
    }

    private static List<string> GetParameterAllowedValues(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttributes(typeof(RefactoringParameterAttribute), false)
            .FirstOrDefault() as RefactoringParameterAttribute;
        return attribute?.AllowedValues?.ToList() ?? new List<string>();
    }

    private static string? GetParameterValidationPattern(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttributes(typeof(RefactoringParameterAttribute), false)
            .FirstOrDefault() as RefactoringParameterAttribute;
        return string.IsNullOrEmpty(attribute?.ValidationPattern) ? null : attribute.ValidationPattern;
    }

    private static string? GetParameterExample(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttributes(typeof(RefactoringParameterAttribute), false)
            .FirstOrDefault() as RefactoringParameterAttribute;
        return string.IsNullOrEmpty(attribute?.Example) ? null : attribute.Example;
    }
}
