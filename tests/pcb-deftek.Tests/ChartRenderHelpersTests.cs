using FluentAssertions;
using pcb_deftek.Presentation.Controls;
using Xunit;

namespace pcb_deftek.Tests;

public class ChartRenderHelpersTests
{
    [Theory]
    [InlineData(0, 300, 1)]
    [InlineData(1, 300, 1)]
    [InlineData(4, 300, 1)]
    [InlineData(12, 300, 3)]
    [InlineData(60, 300, 15)]
    public void CalculateLabelStep_ThinsOnlyWhenPointsExceedWidthBudget(int count, double width, int expected)
    {
        ChartRenderHelpers.CalculateLabelStep(count, width).Should().Be(expected);
    }
}
