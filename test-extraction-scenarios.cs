using System;
using System.Threading.Tasks;

public class TestExtractMethodScenarios
{
    public void TestVariableOutput()
    {
        int x = 10;
        int y = 20;
        
        // Selection start: extract this block
        int result = x + y;
        Console.WriteLine(result);
        // Selection end
    }

    public async Task TestAsyncExtraction()
    {
        var data = await GetDataAsync();
        
        // Selection start: extract this block
        var processed = ProcessData(data);
        Console.WriteLine(processed);
        // Selection end
    }

    public void TestReturnStatement()
    {
        int x = 10;
        
        // Selection start: this should fail
        if (x > 5)
        {
            return; // This should cause extraction to fail
        }
        Console.WriteLine(x);
        // Selection end
    }

    private async Task<string> GetDataAsync()
    {
        await Task.Delay(100);
        return "test data";
    }

    private string ProcessData(string input)
    {
        return input.ToUpper();
    }
}
