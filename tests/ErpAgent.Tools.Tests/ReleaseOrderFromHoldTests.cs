using Xunit;

namespace ErpAgent.Tools.Tests;

public class ReleaseOrderFromHoldTests
{
    /// <summary>
    /// A refusal is an expected outcome, not an exception: the model has to
    /// explain it to the user, so it comes back as data with the diagnosis
    /// attached. Nothing in the database changes — SP_REL_ORD_HLD raises before
    /// it updates anything.
    /// </summary>
    [Fact]
    public async Task Refuses_to_release_1042_and_explains_why()
    {
        var tools = TestDatabase.ToolsFor("MROSSI", "credit");

        var outcome = await tools.ReleaseOrderFromHoldAsync(1042);

        Assert.False(outcome.Released);
        Assert.Contains("110%", outcome.RefusedBecause);
        Assert.NotNull(outcome.Diagnosis);
        Assert.Equal(1051, Assert.Single(outcome.Diagnosis.OrdersCountedOnlyByReleaseCheck).OrderId);
    }

    /// <summary>
    /// The order stays held. Worth asserting on its own: a release path that
    /// half-applied its effects before refusing would be far worse than one
    /// that refuses.
    /// </summary>
    [Fact]
    public async Task Leaves_1042_on_hold_after_a_refusal()
    {
        var tools = TestDatabase.ToolsFor("MROSSI", "credit");

        await tools.ReleaseOrderFromHoldAsync(1042);

        var status = await tools.GetOrderStatusAsync(1042);
        Assert.Equal("H", status.Status);
        Assert.Equal("CR", status.HoldReason);
    }
}
