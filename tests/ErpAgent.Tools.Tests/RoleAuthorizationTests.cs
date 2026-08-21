using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace ErpAgent.Tools.Tests;

/// <summary>
/// Authorization is tested by invoking the tool directly through the middleware.
/// No model is involved and none is needed: the middleware runs on the
/// invocation, not on the conversation — which is the point. A rule the model
/// could talk its way past would not be a rule.
///
/// Both write cases use order 1042, which SP_REL_ORD_HLD refuses on its own
/// merits. If the middleware ever stopped working these tests would fail
/// without altering the fixture.
/// </summary>
public class RoleAuthorizationTests
{
    /// <summary>Agent Framework serialises tool results in camelCase.</summary>
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Runs one tool call through the authorization middleware, exactly as the
    /// agent would. The registered names are stated here for the same reason
    /// they are stated in the host: the policy is keyed on them.
    /// </summary>
    private static async Task<object?> InvokeAsync(
        ErpUser user, string toolName, AIFunctionArguments arguments)
    {
        var orders = new OrderTools(TestDatabase.ConnectionString, user);

        AIFunction function = toolName switch
        {
            "ReleaseOrderFromHold" =>
                AIFunctionFactory.Create(orders.ReleaseOrderFromHoldAsync, name: "ReleaseOrderFromHold"),
            "CancelOrder" =>
                AIFunctionFactory.Create(orders.CancelOrderAsync, name: "CancelOrder"),
            "GetOrderStatus" =>
                AIFunctionFactory.Create(orders.GetOrderStatusAsync, name: "GetOrderStatus"),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unknown tool")
        };

        var middleware = new RoleAuthorizationFilter(user, RoleAuthorizationFilter.DefaultPolicy);

        var context = new FunctionInvocationContext
        {
            Function = function,
            Arguments = arguments
        };

        return await middleware.InvokeAsync(
            agent: null!,
            context,
            next: static async (ctx, ct) => await ctx.Function.InvokeAsync(ctx.Arguments, ct),
            CancellationToken.None);
    }

    [Fact]
    public async Task Refuses_a_release_to_a_role_that_may_not_release()
    {
        var result = await InvokeAsync(
            new ErpUser("GVERDI", "sales"),
            "ReleaseOrderFromHold",
            new AIFunctionArguments { ["orderId"] = 1042 });

        var text = Assert.IsType<string>(result);
        Assert.Contains("denied", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("credit", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counter-case, which is what makes the one above mean something: the
    /// authorised role gets through the middleware and reaches the procedure,
    /// whose refusal is about credit rather than about permission.
    /// </summary>
    [Fact]
    public async Task Lets_an_authorised_role_reach_the_procedure()
    {
        var result = await InvokeAsync(
            new ErpUser("MGRECU", "credit"),
            "ReleaseOrderFromHold",
            new AIFunctionArguments { ["orderId"] = 1042 });

        // Agent Framework hands back the serialised payload rather than the
        // object: what the middleware sees is what the model will see. Worth
        // pinning, because it is the shape every middleware has to work with.
        var outcome = Assert.IsType<JsonElement>(result).Deserialize<ReleaseOutcome>(Json)!;
        Assert.False(outcome.Released);
        Assert.Contains("110%", outcome.RefusedBecause);
    }

    /// <summary>
    /// Reads are not gated. Worth pinning: middleware that quietly blocked
    /// everything would pass the denial test above for the wrong reason.
    /// </summary>
    [Fact]
    public async Task Leaves_read_tools_open_to_every_role()
    {
        var result = await InvokeAsync(
            new ErpUser("GVERDI", "sales"),
            "GetOrderStatus",
            new AIFunctionArguments { ["orderId"] = 1042 });

        var status = Assert.IsType<JsonElement>(result).Deserialize<OrderStatus>(Json)!;
        Assert.Equal("H", status.Status);
    }
}
