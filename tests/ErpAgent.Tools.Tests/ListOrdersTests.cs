using Xunit;

namespace ErpAgent.Tools.Tests;

public class ListOrdersTests
{
    [Fact]
    public async Task Lists_every_order_when_no_filter_is_given()
    {
        var tools = TestDatabase.Tools();

        var list = await tools.ListOrdersAsync();

        Assert.Equal(31, list.Orders.Count);
        Assert.Contains("all", list.FilterApplied, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lists_only_one_customers_orders_when_asked()
    {
        var tools = TestDatabase.Tools();

        var list = await tools.ListOrdersAsync(customerId: 100);

        Assert.Equal([1004, 1030, 1042, 1051], list.Orders.Select(o => o.OrderId));
        Assert.All(list.Orders, o => Assert.Equal("Rossi Impianti S.p.A.", o.CustomerName));
    }

    /// <summary>
    /// Order 1013 points at customer 99, physically removed in the 2011
    /// migration; order 1077 is open against a customer soft-deleted in 2019.
    /// A listing that renders both as ordinary rows with a blank name is how
    /// these survive for a decade — the row looks fine at a glance.
    /// </summary>
    [Fact]
    public async Task Flags_orders_whose_customer_is_missing_or_deleted()
    {
        var tools = TestDatabase.Tools();

        var list = await tools.ListOrdersAsync();

        var hardOrphan = list.Orders.Single(o => o.OrderId == 1013);
        Assert.Contains("99", hardOrphan.DataWarning);
        Assert.Contains("gone", hardOrphan.DataWarning);

        var softOrphan = list.Orders.Single(o => o.OrderId == 1077);
        Assert.Contains("closed", softOrphan.DataWarning);
        Assert.Contains("103", softOrphan.DataWarning);

        // These strings are read by the model, so an interpolation that silently
        // failed to interpolate would be shipped to the user verbatim.
        Assert.All(list.Orders.Where(o => o.DataWarning is not null),
            o => Assert.DoesNotContain("{", o.DataWarning));

        // And so is the vocabulary. The model was told to relay these notes, so
        // whatever jargon they contain reaches a clerk who does not write SQL.
        Assert.All(list.Orders.Where(o => o.DataWarning is not null),
            o => Assert.DoesNotContain("soft", o.DataWarning, StringComparison.OrdinalIgnoreCase));

        Assert.All(list.Orders.Where(o => o.OrderId is not (1013 or 1077)),
            o => Assert.Null(o.DataWarning));
    }

    /// <summary>
    /// "Open orders" has no single answer here: the credit trigger counts N and
    /// H, SP_GET_CUST_EXPO counts N, H and X. A tool that quietly picked one
    /// would be inventing a definition the system does not have, so it takes
    /// explicit codes and says so when it is handed a word instead.
    /// </summary>
    [Fact]
    public async Task Refuses_to_guess_what_an_unrecognised_status_means()
    {
        var tools = TestDatabase.Tools();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => tools.ListOrdersAsync(status: "open"));

        Assert.Contains("N", error.Message);
        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asked which orders are open, the agent listed all 31 rows and counted
    /// them by hand, reporting 12 shipped where there are 14. Counting a list is
    /// something the model does badly and the database does exactly, so the
    /// tally comes back with the list.
    /// </summary>
    [Fact]
    public async Task Counts_the_orders_by_status_so_nobody_has_to_tally_them()
    {
        var tools = TestDatabase.Tools();

        var list = await tools.ListOrdersAsync();

        Assert.Equal(
            new Dictionary<string, int> { ["N"] = 9, ["H"] = 3, ["P"] = 4, ["S"] = 14, ["X"] = 1 },
            list.CountsByStatus.OrderBy(kv => kv.Key).ToDictionary());
    }

    [Fact]
    public async Task Explains_the_status_codes_and_that_X_is_contested()
    {
        var tools = TestDatabase.Tools();

        var list = await tools.ListOrdersAsync(status: "H");

        Assert.Equal(3, list.Orders.Count);
        Assert.All(list.Orders, o => Assert.Equal("on hold", o.StatusMeaning));
        Assert.Contains("X", list.StatusNote);
        Assert.Contains("cancelled", list.StatusNote, StringComparison.OrdinalIgnoreCase);
    }
}
