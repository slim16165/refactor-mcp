using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol;

namespace RefactorMCP.ConsoleApp.Infrastructure;

internal static class RangeService
{
    public static bool TryParseRange(string range, out int startLine, out int startColumn, out int endLine, out int endColumn)
    {
        startLine = startColumn = endLine = endColumn = 0;
        var parts = range.Split('-');
        if (parts.Length != 2) return false;
        var startParts = parts[0].Split(':');
        var endParts = parts[1].Split(':');
        if (startParts.Length != 2 || endParts.Length != 2) return false;
        return int.TryParse(startParts[0], out startLine) &&
               int.TryParse(startParts[1], out startColumn) &&
               int.TryParse(endParts[0], out endLine) &&
               int.TryParse(endParts[1], out endColumn);
    }

    public static bool ValidateRange(SourceText text, int startLine, int startColumn, int endLine, int endColumn, out string error)
    {
        error = string.Empty;
        if (startLine <= 0 || startColumn <= 0 || endLine <= 0 || endColumn <= 0)
        {
            error = "Error: Range values must be positive";
            return false;
        }
        if (startLine > endLine || (startLine == endLine && startColumn >= endColumn))
        {
            error = "Error: Range start must precede end";
            return false;
        }
        if (startLine > text.Lines.Count || endLine > text.Lines.Count)
        {
            error = "Error: Range exceeds file length";
            return false;
        }
        return true;
    }

    public static TextSpan ParseSelectionRange(SourceText sourceText, string selectionRange)
    {
        if (!TryParseRange(selectionRange, out var startLine, out var startColumn, out var endLine, out var endColumn))
            throw new McpException("Error: Invalid selection range format. Use 'startLine:startColumn-endLine:endColumn'");

        if (!ValidateRange(sourceText, startLine, startColumn, endLine, endColumn, out var error))
            throw new McpException(error);

        var startPosition = sourceText.Lines[startLine - 1].Start + startColumn - 1;
        var endPosition = sourceText.Lines[endLine - 1].Start + endColumn - 1;
        return TextSpan.FromBounds(startPosition, endPosition);
    }
}
