using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace ErpAgent.Tools.Tests;

/// <summary>
/// "Delete order 1058" is the request the system is least able to grant
/// literally. Nothing here ever issues DELETE: cancelling means status X, the
/// release of the stock the order had reserved, and an audit row. These tests
/// change the database and put it back afterwards.
/// </summary>
public class CancelOrderTests : IAsyncLifetime
{
    public Task InitializeAsync() => TestDatabase.ResetDemoStateAsync();
    public Task DisposeAsync() => TestDatabase.ResetDemoStateAsync();

    [Fact]
    public async Task Cancels_1058_without_removing_it()
    {
        var tools = TestDatabase.ToolsFor("LBIANCHI", "sales");

        var outcome = await tools.CancelOrderAsync(1058);

        Assert.True(outcome.Cancelled);

        // The row is still there, which is the whole difference from a delete.
        var status = await tools.GetOrderStatusAsync(1058);
        Assert.Equal("X", status.Status);
    }

    /// <summary>
    /// The consequence a DELETE would have missed. Ten brackets were reserved
    /// for this order; cancelling hands them back, and availability moves from
    /// 35 to 45. Nothing would have complained had they stayed reserved forever
    /// — they would simply have been missing from every promise made afterwards.
    /// </summary>
    [Fact]
    public async Task Hands_back_the_stock_the_order_had_reserved()
    {
        var orders = TestDatabase.ToolsFor("LBIANCHI", "sales");
        var inventory = TestDatabase.Inventory();

        var before = await inventory.GetItemAvailabilityAsync("BRK-204");
        var outcome = await orders.CancelOrderAsync(1058);
        var after = await inventory.GetItemAvailabilityAsync("BRK-204");

        Assert.Equal(35m, before.AvailableNow);
        Assert.Equal(45m, after.AvailableNow);

        var released = Assert.Single(outcome.StockReleased);
        Assert.Equal("BRK-204", released.ItemCode);
        Assert.Equal(10m, released.Quantity);
    }

    /// <summary>
    /// Two of the three lines carry no stock at all — one item is not stocked
    /// and the other is a freight charge that is not a product. Reporting them
    /// as released would be a lie of tidiness.
    /// </summary>
    [Fact]
    public async Task Reports_only_the_lines_that_actually_held_stock()
    {
        var tools = TestDatabase.ToolsFor("LBIANCHI", "sales");

        var outcome = await tools.CancelOrderAsync(1058);

        Assert.Equal(3, outcome.LinesCancelled);
        Assert.Single(outcome.StockReleased);
    }

    [Fact]
    public async Task Records_the_cancellation_in_the_audit_trail()
    {
        var tools = TestDatabase.ToolsFor("LBIANCHI", "sales");

        var outcome = await tools.CancelOrderAsync(1058);

        Assert.True(outcome.Audited);
        var status = await tools.GetOrderStatusAsync(1058);
        var entry = Assert.Single(status.AuditRows);
        Assert.Equal("CANC", entry.Action);
        Assert.Equal("LBIANCHI", entry.By);
    }

    /// <summary>
    /// Order 1001 shipped in 2023. SP_CANC_ORD only accepts N and H, and a
    /// refusal is data here for the same reason it is on release: the user needs
    /// telling, and the model must not go looking for another route.
    /// </summary>
    [Fact]
    public async Task Refuses_to_cancel_an_order_that_has_already_shipped()
    {
        var tools = TestDatabase.ToolsFor("LBIANCHI", "sales");

        var outcome = await tools.CancelOrderAsync(1001);

        Assert.False(outcome.Cancelled);
        Assert.NotNull(outcome.RefusedBecause);
        Assert.Null(outcome.Audited);

        var status = await tools.GetOrderStatusAsync(1001);
        Assert.Equal("S", status.Status);
    }

    /// <summary>
    /// Cancelling and releasing are different powers held by different people:
    /// order entry cancels, credit control releases. Neither role is a superset
    /// of the other, which is what a policy table gives you and a hierarchy of
    /// permissions does not.
    /// </summary>
    [Fact]
    public async Task Is_closed_to_the_role_that_may_release_holds()
    {
        var user = new ErpUser("MGRECU", "credit");
        var orders = new OrderTools(TestDatabase.ConnectionString, user);
        var middleware = new RoleAuthorizationFilter(user, RoleAuthorizationFilter.DefaultPolicy);

        var context = new FunctionInvocationContext
        {
            Function = AIFunctionFactory.Create(orders.CancelOrderAsync, name: "CancelOrder"),
            Arguments = new AIFunctionArguments { ["orderId"] = 1058 }
        };

        var result = await middleware.InvokeAsync(
            agent: null!,
            context,
            next: static async (ctx, ct) => await ctx.Function.InvokeAsync(ctx.Arguments, ct),
            CancellationToken.None);

        Assert.Contains("denied", Assert.IsType<string>(result), StringComparison.OrdinalIgnoreCase);

        var status = await TestDatabase.Tools().GetOrderStatusAsync(1058);
        Assert.Equal("N", status.Status);
    }

    /// <summary>
    /// The rule that cannot be argued with, asserted directly: there is no tool
    /// that deletes. Not discouraged in a prompt, not gated by a role — absent
    /// from the surface the model can call.
    /// </summary>
    [Fact]
    public void Offers_no_tool_that_deletes_anything()
    {
        var orders = new OrderTools(TestDatabase.ConnectionString, new ErpUser("X", "sales"));
        var inventory = new InventoryTools(TestDatabase.ConnectionString);

        // The surface the model can call, assembled as the host assembles it.
        AIFunction[] surface =
        [
            AIFunctionFactory.Create(orders.ListOrdersAsync, name: "ListOrders"),
            AIFunctionFactory.Create(orders.GetOrderStatusAsync, name: "GetOrderStatus"),
            AIFunctionFactory.Create(orders.CheckReleaseEligibilityAsync, name: "CheckReleaseEligibility"),
            AIFunctionFactory.Create(orders.ReleaseOrderFromHoldAsync, name: "ReleaseOrderFromHold"),
            AIFunctionFactory.Create(orders.CancelOrderAsync, name: "CancelOrder"),
            AIFunctionFactory.Create(inventory.GetItemAvailabilityAsync, name: "GetItemAvailability"),
        ];

        Assert.DoesNotContain(surface,
            f => f.Name.Contains("delete", StringComparison.OrdinalIgnoreCase)
              || f.Name.Contains("remove", StringComparison.OrdinalIgnoreCase)
              || f.Name.Contains("sql", StringComparison.OrdinalIgnoreCase));
    }
}
