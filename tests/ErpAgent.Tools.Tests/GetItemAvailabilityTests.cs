using Xunit;

namespace ErpAgent.Tools.Tests;

/// <summary>
/// "How many BRK-204 can I promise for Friday?" has three defensible answers in
/// this database and no wrong one — they answer different questions. The tool's
/// job is to keep them apart instead of picking one.
/// </summary>
public class GetItemAvailabilityTests
{
    [Fact]
    public async Task Separates_what_is_on_the_shelf_from_what_is_still_coming()
    {
        var tools = TestDatabase.Inventory();

        var stock = await tools.GetItemAvailabilityAsync("BRK-204");

        Assert.Equal(35m, stock.AvailableNow);      // MAIN 45-15, plus SEC1 5
        Assert.Equal(20m, stock.ArrivingLater);     // TRNS, not on any shelf yet
        Assert.Equal(55m, stock.TotalNet);
    }

    /// <summary>
    /// SP_GET_ITM_AVL was written in 2011, when MAIN was the only warehouse, and
    /// still filters on it. It is not wrong about MAIN — it is answering a
    /// narrower question than the one being asked, and says so.
    /// </summary>
    [Fact]
    public async Task Reports_what_the_official_procedure_says_and_why_it_is_lower()
    {
        var tools = TestDatabase.Inventory();

        var stock = await tools.GetItemAvailabilityAsync("BRK-204");

        Assert.Equal(30m, stock.AccordingToTheOfficialProcedure);
        Assert.Contains("MAIN", stock.OfficialProcedureCaveat);
        Assert.Contains("2011", stock.OfficialProcedureCaveat);
    }

    /// <summary>
    /// The figure that appears on spreadsheets around the office, because it is
    /// what SUM(QTY_OH) returns. Reported so the agent can reconcile with it
    /// when somebody quotes it, not because it is a good answer.
    /// </summary>
    [Fact]
    public async Task Reports_the_raw_total_that_ignores_commitments()
    {
        var tools = TestDatabase.Inventory();

        var stock = await tools.GetItemAvailabilityAsync("BRK-204");

        Assert.Equal(70m, stock.RawSumOfOnHand);
    }

    /// <summary>
    /// The resident expert answers "35 for Friday, 55 the week after". A single
    /// number cannot carry that, so the breakdown says which warehouses are on a
    /// shelf and which are not, and why.
    /// </summary>
    [Fact]
    public async Task Says_which_warehouses_can_be_promised_and_which_cannot()
    {
        var tools = TestDatabase.Inventory();

        var stock = await tools.GetItemAvailabilityAsync("BRK-204");

        var main = stock.ByWarehouse.Single(w => w.WarehouseCode == "MAIN");
        Assert.True(main.CountsAsAvailableNow);
        Assert.Equal(30m, main.Net);

        var secondary = stock.ByWarehouse.Single(w => w.WarehouseCode == "SEC1");
        Assert.True(secondary.CountsAsAvailableNow);

        var transit = stock.ByWarehouse.Single(w => w.WarehouseCode == "TRNS");
        Assert.False(transit.CountsAsAvailableNow);
        Assert.Contains("transit", transit.Meaning, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// BLT-M8-40 has 120 committed against 100 on hand. No constraint objects,
    /// and everybody in the office knows about the bolts. A tool that reported
    /// -20 without a word would be arithmetically right and useless.
    /// </summary>
    [Fact]
    public async Task Flags_an_item_committed_beyond_what_exists()
    {
        var tools = TestDatabase.Inventory();

        var stock = await tools.GetItemAvailabilityAsync("BLT-M8-40");

        Assert.Equal(-20m, stock.AvailableNow);
        Assert.Contains("more", stock.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reports_an_unknown_item_code_as_absent()
    {
        var tools = TestDatabase.Inventory();

        var error = await Assert.ThrowsAsync<ItemNotFoundException>(
            () => tools.GetItemAvailabilityAsync("NOPE-1"));

        Assert.Contains("NOPE-1", error.Message);
    }
}
