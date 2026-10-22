namespace Etherprof.Core.Tests;

using Etherprof.Core;
using Xunit;

public class QrCodeGeneratorTests
{
    [Theory]
    [InlineData("WIFI:S:MyNetwork;T:WPA;P:Secret123;;")]
    [InlineData("WIFI:S:TestNet;T:nopass;;")]
    [InlineData("WIFI:S:A;T:WPA;P:B;;")]
    [InlineData("WIFI:S:A Very Long Network Name That Spans Multiple Words;T:WPA;P:SuperLongComplexPasswordWithSpecialCharacters!@#$%^&*();;")]
    public void GenerateMatrix_ValidWifiStrings_GeneratesValidMatrix(string text)
    {
        bool[,] matrix = QrCodeGenerator.GenerateMatrix(text);
        Assert.NotNull(matrix);
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        Assert.True(rows > 0);
        Assert.Equal(rows, cols);

        // Finder patterns should have black top-left module
        Assert.True(matrix[0, 0]);
        Assert.True(matrix[0, cols - 1]);
        Assert.True(matrix[rows - 1, 0]);
    }
}
