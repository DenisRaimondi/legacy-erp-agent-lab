using Xunit;

namespace ErpAgent.Tools.Tests;

public class GetOrderStatusTests
{
    [Fact]
    public async Task Reports_order_1042_as_held_for_credit()
    {
        var tools = new OrderTools(TestDatabase.ConnectionString);

        var status = await tools.GetOrderStatusAsync(1042);

        Assert.Equal("H", status.Status);
        Assert.Equal("CR", status.HoldReason);
        Assert.Equal(100, status.CustomerId);
        Assert.Equal("Rossi Impianti S.p.A.", status.CustomerName);
        Assert.Equal(2600.00m, status.OrderTotal);
    }

    /// <summary>
    /// The hold on 1042 was set by a trigger, and the triggers in this database
    /// predate FND_AUDIT_TRL and never write to it. An empty audit list is
    /// therefore ambiguous on its own, so the tool must say why it is empty —
    /// otherwise the model is free to read "no audit" as "nothing happened".
    /// </summary>
    [Fact]
    public async Task Explains_why_the_1042_hold_left_no_audit_trail()
    {
        var tools = new OrderTools(TestDatabase.ConnectionString);

        var status = await tools.GetOrderStatusAsync(1042);

        Assert.Empty(status.AuditRows);
        Assert.Contains("trigger", status.AuditNote, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counter-example: order 1051 was cancelled through SP_CANC_ORD, and
    /// stored procedures do write the audit trail. Same tool, same order shape,
    /// different history.
    /// </summary>
    [Fact]
    public async Task Returns_the_cancellation_audit_row_of_order_1051()
    {
        var tools = new OrderTools(TestDatabase.ConnectionString);

        var status = await tools.GetOrderStatusAsync(1051);

        var entry = Assert.Single(status.AuditRows);
        Assert.Equal("CANC", entry.Action);
        Assert.Equal("LBIANCHI", entry.By);
    }

    /// <summary>
    /// Order ids run 1001 to 1078 with only 31 orders in between, because rows
    /// were lost in the 2011 migration. A missing id is therefore an ordinary
    /// outcome carrying real information, not a malfunction — and it must not
    /// surface as a null reference, which reads to the model as a broken system
    /// and invites it to speculate about corruption.
    /// </summary>
    [Fact]
    public async Task Reports_a_missing_order_as_absent_and_says_gaps_are_normal()
    {
        var tools = new OrderTools(TestDatabase.ConnectionString);

        var error = await Assert.ThrowsAsync<OrderNotFoundException>(
            () => tools.GetOrderStatusAsync(1041));

        Assert.Contains("1041", error.Message);
        Assert.Contains("2011", error.Message);
    }
}
