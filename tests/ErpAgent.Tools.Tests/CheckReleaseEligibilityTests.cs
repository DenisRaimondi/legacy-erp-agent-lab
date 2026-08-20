using Xunit;

namespace ErpAgent.Tools.Tests;

/// <summary>
/// Order 1042 is the walkthrough's first question. Its hold is ordinary; what
/// is not ordinary is that the sanctioned release refuses it, for a reason
/// that appears nowhere in the schema.
/// </summary>
public class CheckReleaseEligibilityTests
{
    [Fact]
    public async Task Order_1042_cannot_be_released()
    {
        var tools = new OrderTools(TestDatabase.ConnectionString);

        var check = await tools.CheckReleaseEligibilityAsync(1042);

        Assert.False(check.CanBeReleased);
    }

    /// <summary>
    /// Both halves of the credit control appear in the answer because they
    /// measure different things: the hold compared 5,182.50 against the limit,
    /// the release compares 7,410.50 against the ceiling. A report carrying
    /// only one figure cannot explain the refusal.
    /// </summary>
    [Fact]
    public async Task Reports_both_thresholds_and_both_exposures()
    {
        var tools = new OrderTools(TestDatabase.ConnectionString);

        var check = await tools.CheckReleaseEligibilityAsync(1042);

        Assert.Equal(5000.00m, check.CreditLimit);
        Assert.Equal(5500.00m, check.ReleaseCeiling);
        Assert.Equal(5182.50m, check.ExposureUsedByHoldCheck);
        Assert.Equal(7410.50m, check.ExposureUsedByReleaseCheck);
        Assert.True(check.ExposuresDisagree);
    }

    /// <summary>
    /// The only actionable field in the whole response. Without it the clerk
    /// knows they are stuck; with it they know what to ask IT to fix.
    /// </summary>
    [Fact]
    public async Task Names_the_cancelled_order_that_keeps_1042_blocked()
    {
        var tools = new OrderTools(TestDatabase.ConnectionString);

        var check = await tools.CheckReleaseEligibilityAsync(1042);

        Assert.True(check.WouldPassWithHoldExposure);
        var culprit = Assert.Single(check.OrdersCountedOnlyByReleaseCheck);
        Assert.Equal(1051, culprit.OrderId);
        Assert.Equal("X", culprit.Status);
        Assert.Equal(2228.00m, culprit.Amount);
    }

    [Fact]
    public async Task Reports_a_missing_order_as_absent_and_says_gaps_are_normal()
    {
        var tools = new OrderTools(TestDatabase.ConnectionString);

        var error = await Assert.ThrowsAsync<OrderNotFoundException>(
            () => tools.CheckReleaseEligibilityAsync(1041));

        Assert.Contains("1041", error.Message);
        Assert.Contains("2011", error.Message);
    }
}
