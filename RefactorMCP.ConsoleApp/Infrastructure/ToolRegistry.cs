using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Server;

namespace RefactorMCP.ConsoleApp.Infrastructure;

public static class ToolRegistry
{
    private static readonly Dictionary<string, MethodInfo> _tools = new(StringComparer.OrdinalIgnoreCase);
    
    // Canonical names (what we list / suggest)
    private static readonly HashSet<string> _canonicalToolNames = new(StringComparer.OrdinalIgnoreCase);

    static ToolRegistry()
    {
        Initialize();
    }

    public static void Initialize()
    {
        if (_tools.Count > 0) return;

        var methods = typeof(ToolRegistry).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(McpServerToolTypeAttribute), false).Length > 0)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0);

        foreach (var method in methods)
        {
            // Legacy kebab-case: keeps "-command" suffix if present
            var legacyKebab = ToKebabCase(method.Name); // e.g. list-tools-command
            _tools[legacyKebab] = method;

            // Original CLR name for max backwards compatibility
            if (!_tools.ContainsKey(method.Name))
                _tools[method.Name] = method;

            // Canonical kebab-case: if method ends with "Command", also register without it
            var canonical = legacyKebab;
            if (method.Name.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = method.Name.Substring(0, method.Name.Length - "Command".Length);
                canonical = ToKebabCase(baseName); // e.g. list-tools
                _tools[canonical] = method;
            }

            _canonicalToolNames.Add(canonical);
        }
    }

    public static MethodInfo? GetTool(string name)
    {
        var sanitized = name.Trim();
        
        // 1. Try exact match (case-insensitive due to dictionary)
        if (_tools.TryGetValue(sanitized, out var method))
            return method;
            
        // 2. Normalize kebab/pascal/snake/spaces
        var normalized = NormalizeToolName(sanitized);
        if (_tools.TryGetValue(normalized, out method))
            return method;
            
        // 3. If user typed legacy "-command", also try canonical, and vice-versa
        if (normalized.EndsWith("-command", StringComparison.OrdinalIgnoreCase))
        {
            var canon = normalized.Substring(0, normalized.Length - "-command".Length);
            if (_tools.TryGetValue(canon, out method))
                return method;
        }
        else
        {
            var legacy = normalized + "-command";
            if (_tools.TryGetValue(legacy, out method))
                return method;
        }
        
        return null;
    }

    public static List<string> SuggestTools(string typo, int maxCount = 3)
    {
        var typoNorm = NormalizeToolName(typo);
        var available = GetAvailableToolNames().ToList();
        
        return available
            // cheap pre-score: startsWith/contains then distance
            .Select(t => new { Name = t, Score = Score(typoNorm, t) })
            .OrderBy(x => x.Score)
            .Take(maxCount)
            .Select(x => x.Name)
            .ToList();
    }

    public static IEnumerable<string> GetAvailableToolNames()
    {
        // List only canonical names (no legacy "-command" noise)
        // Ensure we only list items that look like kebab-case (have dashes) or represent valid tools
        return _canonicalToolNames
            .Where(n => n.Contains('-')) 
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        
        // Pure conversion - does NOT strip "Command" suffix automatically
        
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            
            if (char.IsUpper(c))
            {
                // avoid "HTTPServer" => "h-t-t-p-server" (simple heuristic)
                var prevIsUpper = i > 0 && char.IsUpper(name[i - 1]);
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                
                if (i > 0 && name[i-1] != '-' && (!prevIsUpper || nextIsLower))
                {
                    sb.Append('-');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c == '_' || c == ' ')
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString().Trim('-');
    }

    private static string NormalizeToolName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        // If already kebab-ish, keep it but normalize separators/case.
        if (name.Contains('-') && !name.Any(char.IsUpper))
            return name.Trim().Replace('_', '-').Replace(' ', '-').ToLowerInvariant();
        // Otherwise convert to kebab
        return ToKebabCase(name.Trim().Replace('_', '-').Replace(' ', '-'));
    }

    private static int Score(string req, string cand)
    {
        if (string.IsNullOrEmpty(req)) return 1000;
        if (cand.Equals(req, StringComparison.OrdinalIgnoreCase)) return 0;
        if (cand.StartsWith(req, StringComparison.OrdinalIgnoreCase)) return 1;
        if (cand.Contains(req, StringComparison.OrdinalIgnoreCase)) return 2;
        // clamp Levenshtein cost a range "not too expensive"
        return 100 + ComputeLevenshteinClamp(req, cand, 12);
    }

    private static int ComputeLevenshteinClamp(string s, string t, int maxDistance)
    {
        int n = s.Length;
        int m = t.Length;
        
        if (Math.Abs(n - m) > maxDistance) return maxDistance + 1;
        
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        
        var res = d[n, m];
        return res > maxDistance ? maxDistance + 1 : res;
    }
}
