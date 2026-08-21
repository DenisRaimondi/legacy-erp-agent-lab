using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.Text.Json;
using ErpAgent.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

const string SystemPrompt = """
    You answer questions about an ERP system by calling the tools you are given.

    You have no knowledge of this database beyond what the tools return, and the
    schema does not mean what it appears to mean, so never infer, estimate or
    reconstruct a figure yourself: call a tool, or say you cannot answer.

    Tool responses deliberately include the system's own disagreements and
    caveats. Report them. An answer that states only the obvious half is worse
    than no answer, because it sounds right.

    The tools have already applied the rules and reached the decisions. Read
    their fields and relay them; do not recompute a figure, re-derive a verdict
    a boolean already states, or turn a null into a claim.

    Reply in the language the user writes in, in plain prose, briefly, the way a
    colleague who knows the system would explain it to someone who does not.
    """;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

// Provider selection: Azure OpenAI when configured, DeepSeek otherwise. The
// agent does not care which model answers - the tools, the role filter and the
// audit trail are identical either way. That is the point of the trust layer:
// swapping the model must not change what the agent is allowed to do.
var azureEndpoint = configuration["AzureOpenAI:Endpoint"]
    ?? configuration["AZURE_OPENAI_ENDPOINT"];
var azureApiKey = configuration["AzureOpenAI:ApiKey"]
    ?? configuration["AZURE_OPENAI_API_KEY"];
var azureDeployment = configuration["AzureOpenAI:Deployment"]
    ?? configuration["AZURE_OPENAI_DEPLOYMENT"]
    ?? "gpt-5-mini";

var useAzure = !string.IsNullOrWhiteSpace(azureEndpoint)
    && !string.IsNullOrWhiteSpace(azureApiKey);

var apiKey = useAzure
    ? null
    : configuration["DeepSeek:ApiKey"]
        ?? configuration["DEEPSEEK_API_KEY"]
        ?? throw new InvalidOperationException(
            "No model provider configured. Set Azure OpenAI:\n"
            + "  dotnet user-secrets set \"AzureOpenAI:Endpoint\" \"<url>\" --project src/ErpAgent\n"
            + "  dotnet user-secrets set \"AzureOpenAI:ApiKey\" \"<key>\" --project src/ErpAgent\n"
            + "or DeepSeek:\n"
            + "  dotnet user-secrets set \"DeepSeek:ApiKey\" \"<key>\" --project src/ErpAgent");

var connectionString =
    configuration["ERPPRD01_CONNECTION"]
    ?? "Server=localhost,1433;Database=ERPPRD01;User Id=sa;Password=LegacyLab!2026;"
       + "TrustServerCertificate=True;Encrypt=False";

// Identity comes from the host, standing in for whatever a real deployment
// would authenticate against. It is never a tool parameter: a name the model
// can choose is a name the model can invent, and it is what lands in the audit
// trail.
var user = new ErpUser(
    configuration["ERPAGENT_USER"] ?? "DEMO",
    configuration["ERPAGENT_ROLE"] ?? "sales");

var traceEnabled = configuration["ERPAGENT_TRACE"] == "1";

// One IChatClient, whichever provider is configured. Everything downstream —
// tools, middleware, the loop — is written against the abstraction and never
// learns which service answered.
IChatClient chatClient = useAzure
    ? new AzureOpenAIClient(new Uri(azureEndpoint!), new ApiKeyCredential(azureApiKey!))
        .GetChatClient(azureDeployment)
        .AsIChatClient()
    // DeepSeek speaks the OpenAI wire protocol, so the OpenAI client works
    // unchanged against its endpoint. deepseek-chat, not deepseek-reasoner: this
    // agent is nothing without function calling, and the reasoner does not do it.
    : new OpenAIClient(new ApiKeyCredential(apiKey!),
            new OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com/v1") })
        .GetChatClient("deepseek-chat")
        .AsIChatClient();

var orderTools = new OrderTools(connectionString, user);
var inventoryTools = new InventoryTools(connectionString);

// The registered name is stated explicitly rather than inferred from the method
// name. The authorization policy is keyed on these strings, and a policy that
// silently stops matching is a policy that silently stops enforcing.
AITool[] tools =
[
    AIFunctionFactory.Create(orderTools.ListOrdersAsync, name: "ListOrders"),
    AIFunctionFactory.Create(orderTools.GetOrderStatusAsync, name: "GetOrderStatus"),
    AIFunctionFactory.Create(orderTools.CheckReleaseEligibilityAsync, name: "CheckReleaseEligibility"),
    AIFunctionFactory.Create(orderTools.ReleaseOrderFromHoldAsync, name: "ReleaseOrderFromHold"),
    AIFunctionFactory.Create(orderTools.CancelOrderAsync, name: "CancelOrder"),
    AIFunctionFactory.Create(inventoryTools.GetItemAvailabilityAsync, name: "GetItemAvailability"),
];

var audit = new ConsoleAuditMiddleware(traceEnabled);
var authorization = new RoleAuthorizationFilter(user, RoleAuthorizationFilter.DefaultPolicy);

// Order matters: audit wraps authorization, so a refused call is still recorded.
// The reverse order would make refusals invisible — the one event you most want
// in the trail is the one that never ran.
AIAgent agent = chatClient
    .AsAIAgent(instructions: SystemPrompt, tools: tools)
    .AsBuilder()
        .Use(audit.InvokeAsync)
        .Use(authorization.InvokeAsync)
    .Build();

AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine("ERPPRD01 assistant. Ask about an order. Empty line to quit.");
if (!traceEnabled) Console.WriteLine("Set ERPAGENT_TRACE=1 to see the messages the agent builds.");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question)) break;

    AgentResponse response = await agent.RunAsync(question, session);

    Console.WriteLine();
    Console.WriteLine(response.Text);
    Console.WriteLine();

    if (traceEnabled) WriteConversationTrace(response);
}

// A single RunAsync hides a loop: the model asks for tools, the framework runs
// them, the results go back, and only then does the answer arrive. Printing the
// messages the run produced makes that visible — the whole point of the demo is
// that the loop is otherwise invisible.
static void WriteConversationTrace(AgentResponse response)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  the run produced {response.Messages.Count} messages");

    foreach (var message in response.Messages)
    {
        var text = message.Text;
        var preview = text is { Length: > 0 }
            ? text.ReplaceLineEndings(" ")[..Math.Min(60, text.Length)]
            : $"[{message.Contents.Count} non-text blocks]";
        Console.WriteLine($"    {message.Role,-9} {preview}");
    }

    Console.ResetColor();
    Console.WriteLine();
}

/// <summary>
/// Prints every tool call as it happens. The console stand-in for the audit
/// panel: the point of the demo is not that the answer is right, it is that you
/// can see what the agent did to get there.
///
/// Which is why the result is summarised rather than dumped. A tool returns
/// everything that bears on the decision because the model reads all of it; a
/// person watching the screen does not, and thirty rows of JSON hide the call
/// they were meant to reveal. Same data, opposite audience, opposite treatment.
/// The full payload is one environment variable away.
/// </summary>
internal sealed class ConsoleAuditMiddleware(bool verbose)
{
    public async ValueTask<object?> InvokeAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        var arguments = string.Join(", ",
            context.Arguments.Select(a => $"{a.Key}: {a.Value}"));

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [tool] {context.Function.Name}({arguments})");

        var result = await next(context, cancellationToken);

        var json = JsonSerializer.Serialize(result);
        Console.WriteLine($"  [tool] -> {(verbose ? json : Summarise(json))}");
        Console.ResetColor();

        return result;
    }

    /// <summary>
    /// One line per result: scalars as they are, long strings clipped, arrays as
    /// a count. Deliberately generic — it reads the serialised shape rather than
    /// the tool types, so a new tool needs no change here.
    /// </summary>
    private static string Summarise(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind is not JsonValueKind.Object) return Clip(json, 120);

        var fields = document.RootElement.EnumerateObject().Select(p => p.Value.ValueKind switch
        {
            JsonValueKind.Array => $"{p.Name}=[{p.Value.GetArrayLength()} items]",
            JsonValueKind.Object => $"{p.Name}={{{string.Join(" ",
                p.Value.EnumerateObject().Select(i => $"{i.Name}={i.Value}"))}}}",
            JsonValueKind.String => $"{p.Name}=\"{Clip(p.Value.GetString() ?? "", 60)}\"",
            JsonValueKind.Null => $"{p.Name}=null",
            _ => $"{p.Name}={p.Value}"
        });

        return string.Join(", ", fields);
    }

    private static string Clip(string text, int limit) =>
        text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit), "…");
}
