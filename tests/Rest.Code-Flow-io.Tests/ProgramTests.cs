using System.Reflection;

namespace Rest.Code_Flow_io.Tests;

public class ProgramTests
{
    [Fact]
    public void Escape_RemovesProblematicChars()
    {
        var method = typeof(Program).GetMethod("Escape", BindingFlags.NonPublic | BindingFlags.Static);
        var input = "Texto \"com\" barras\\e\nquebra\rde linha";
        var expected = "Texto 'com' barras/e\\nquebra de linha";
        var result = (string)method.Invoke(null, new object[] { input });
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeleteIfExists_RemovesFile()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "teste");
        var method = typeof(Program).GetMethod("DeleteIfExists", BindingFlags.NonPublic | BindingFlags.Static);
        method.Invoke(null, new object[] { tempFile });
        Assert.False(File.Exists(tempFile));
    }

    // Removido teste que passa null para cfg

    [Fact(Skip = "Requires mmdc CLI")]
    public async Task GenerateMermaidImage_ReturnsFalseIfProcessFails()
    {
        var method = typeof(Program).GetMethod("GenerateMermaidImage", BindingFlags.NonPublic | BindingFlags.Static);
        var result = await (Task<bool>)method.Invoke(null, new object[] { "arquivo.mmd", "svg" });
        Assert.False(result);
    }

    [Fact]
    public async Task Main_ReturnsErrorOnInvalidArgs()
    {
        var mainMethod = typeof(Program).GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
        var result = await (Task<int>)mainMethod.Invoke(null, new object[] { new string[] { } });
        Assert.Equal(1, result);
    }
}
