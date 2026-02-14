using System.Text;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;

namespace RefactorMCP.ConsoleApp.Infrastructure;

internal static class TextService
{
    public static async Task<(string Text, Encoding Encoding)> ReadFileWithEncodingAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var encoding = DetectEncoding(bytes);
        var text = encoding.GetString(bytes);
        return (text, encoding);
    }

    public static async Task<Encoding> GetFileEncodingAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return DetectEncoding(bytes);
    }

    public static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                return new UTF32Encoding(true, true);
            if (bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                return new UTF32Encoding(false, true);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;
        }
        return Encoding.UTF8;
    }

    public static async Task WriteFileWithEncodingAsync(string filePath, string text, Encoding encoding, CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(filePath, text, encoding, cancellationToken);
    }
}
