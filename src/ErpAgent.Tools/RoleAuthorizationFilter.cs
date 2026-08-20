using Microsoft.SemanticKernel;

namespace ErpAgent.Tools;

/// <summary>
/// Decides which tools a role may run, and refuses the rest before they execute.
///
/// This is enforcement rather than instruction. The same rule could be written
/// into the system prompt, where the model would usually respect it — but a
/// prompt is a request, and a request can be argued with, misread, or buried
/// under a long conversation. A filter runs on the invocation, so there is
/// nothing to argue with: the function does not execute.
/// </summary>
public sealed class RoleAuthorizationFilter(
    ErpUser user,
    IReadOnlyDictionary<string, string> requiredRoles) : IFunctionInvocationFilter
{
    /// <summary>
    /// The whole write policy, in one readable place. Anything absent is open;
    /// every entry here is a tool that changes the database.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultPolicy =
        new Dictionary<string, string>
        {
            ["ReleaseOrderFromHold"] = "credit"
        };

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        if (requiredRoles.TryGetValue(context.Function.Name, out var required)
            && !string.Equals(user.Role, required, StringComparison.OrdinalIgnoreCase))
        {
            // Short-circuit: `next` is never called, so the function body never
            // runs. The model is told plainly, so it reports the refusal instead
            // of hunting for another route.
            context.Result = new FunctionResult(context.Function,
                $"Denied: {user.Name} holds the role '{user.Role}', and "
                + $"{context.Function.Name} requires '{required}'. Nothing was "
                + "changed. Tell the user which role is needed; do not attempt "
                + "another way to perform this action.");
            return;
        }

        await next(context);
    }
}
