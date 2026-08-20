using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace ErpAgent.Tools.Tests;

/// <summary>
/// Authorization is tested by invoking the kernel function directly. No model is
/// involved and none is needed: the filter runs on the invocation, not on the
/// conversation — which is the point. A rule the model could talk its way past
/// would not be a rule.
///
/// Both cases use order 1042, which SP_REL_ORD_HLD refuses on its own merits.
/// If the filter ever stopped working these tests would fail without altering
/// the fixture.
/// </summary>
public class RoleAuthorizationTests
{
    private static Kernel KernelFor(ErpUser user)
    {
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IFunctionInvocationFilter>(
            new RoleAuthorizationFilter(user, RoleAuthorizationFilter.DefaultPolicy));

        var kernel = builder.Build();
        kernel.Plugins.AddFromObject(new OrderTools(TestDatabase.ConnectionString, user), "Orders");
        return kernel;
    }

    [Fact]
    public async Task Refuses_a_release_to_a_role_that_may_not_release()
    {
        var kernel = KernelFor(new ErpUser("GVERDI", "sales"));

        var result = await kernel.InvokeAsync("Orders", "ReleaseOrderFromHold",
            new KernelArguments { ["orderId"] = 1042 });

        var text = result.GetValue<string>();
        Assert.Contains("denied", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("credit", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counter-case, which is what makes the one above mean something: the
    /// authorised role gets through the filter and reaches the procedure, whose
    /// refusal is about credit rather than about permission.
    /// </summary>
    [Fact]
    public async Task Lets_an_authorised_role_reach_the_procedure()
    {
        var kernel = KernelFor(new ErpUser("MGRECU", "credit"));

        var result = await kernel.InvokeAsync("Orders", "ReleaseOrderFromHold",
            new KernelArguments { ["orderId"] = 1042 });

        var outcome = result.GetValue<ReleaseOutcome>();
        Assert.NotNull(outcome);
        Assert.False(outcome.Released);
        Assert.Contains("110%", outcome.RefusedBecause);
    }

    /// <summary>
    /// Reads are not gated. Worth pinning: a filter that quietly blocked
    /// everything would pass the denial test above for the wrong reason.
    /// </summary>
    [Fact]
    public async Task Leaves_read_tools_open_to_every_role()
    {
        var kernel = KernelFor(new ErpUser("GVERDI", "sales"));

        var result = await kernel.InvokeAsync("Orders", "GetOrderStatus",
            new KernelArguments { ["orderId"] = 1042 });

        Assert.Equal("H", result.GetValue<OrderStatus>()!.Status);
    }
}
