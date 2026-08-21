using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ErpAgent.Tools;

/// <summary>
/// Decides which tools a role may run, and refuses the rest before they execute.
///
/// This is enforcement rather than instruction. The same rule could be written
/// into the system prompt, where the model would usually respect it — but a
/// prompt is a request, and a request can be argued with, misread, or buried
/// under a long conversation. Middleware runs on the invocation, so there is
/// nothing to argue with: the function does not execute.
/// </summary>
public sealed class RoleAuthorizationFilter(
    ErpUser user,
    IReadOnlyDictionary<string, string> requiredRoles)
{
    /// <summary>
    /// The whole write policy, in one readable place. Anything absent is open;
    /// every entry here is a tool that changes the database.
    ///
    /// Note that neither role contains the other. Order entry cancels orders and
    /// credit control releases holds, and a table says so directly — a hierarchy
    /// of permission levels could not express it without inventing a rank that
    /// the business does not have.
    ///
    /// The keys must match the names the tools are registered under. They are
    /// set explicitly at registration rather than inferred from the method name,
    /// because a policy that silently stops matching is a policy that silently
    /// stops enforcing.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultPolicy =
        new Dictionary<string, string>
        {
            ["ReleaseOrderFromHold"] = "credit",
            ["CancelOrder"] = "sales"
        };

    /// <summary>
    /// Function-calling middleware: it wraps every tool invocation the agent
    /// makes. Returning without awaiting <paramref name="next"/> short-circuits
    /// the call — the function body never runs, and the string returned here is
    /// what the model receives as the result.
    /// </summary>
    public async ValueTask<object?> InvokeAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        if (requiredRoles.TryGetValue(context.Function.Name, out var required)
            && !string.Equals(user.Role, required, StringComparison.OrdinalIgnoreCase))
        {
            // The model is told plainly, so it reports the refusal instead of
            // hunting for another route.
            return $"Denied: {user.Name} holds the role '{user.Role}', and "
                + $"{context.Function.Name} requires '{required}'. Nothing was "
                + "changed. Tell the user which role is needed; do not attempt "
                + "another way to perform this action.";
        }

        return await next(context, cancellationToken);
    }
}
