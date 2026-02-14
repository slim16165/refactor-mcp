using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RefactorMCP.Tests.Tools;

public class SummaryResourceTests : TestBase
{
    [Fact]
    public async Task GetSummary_OmitsMethodBodies()
    {
        var result = await SummaryResources.GetSummary(ExampleFilePath, CancellationToken.None);
        var normalizedResult = result.Replace("\r\n", "\n");
        Assert.Contains("public int Calculate(int a, int b)", normalizedResult);
        Assert.Contains("{}", normalizedResult);
        Assert.DoesNotContain("throw new ArgumentException", normalizedResult);
    }

    [Fact]
    public async Task GetSummary_FileNotFound_ReturnsMessage()
    {
        var result = await SummaryResources.GetSummary("does_not_exist.cs", CancellationToken.None);
        Assert.Contains("// File not found:", result);
    }
}
