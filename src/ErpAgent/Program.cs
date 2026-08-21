using System.Text.Json;
using ErpAgent.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

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

var builder = Kernel.CreateBuilder();

if (useAzure)
{
    // On Azure the model is reached through a deployment we create and name
    // ourselves, so the identifier here is our deployment name - not the name
    // of the underlying model.
    builder.AddAzureOpenAIChatCompletion(
        deploymentName: azureDeployment,
        endpoint: azureEndpoint!,
        apiKey: azureApiKey!);
}
else
{
    // DeepSeek speaks the OpenAI wire protocol, so the OpenAI connector works
    // unchanged against its endpoint. deepseek-chat, not deepseek-reasoner: this
    // agent is nothing without function calling, and the reasoner does not do it.
    builder.AddOpenAIChatCompletion(
        modelId: "deepseek-chat",
        endpoint: new Uri("https://api.deepseek.com/v1"),
        apiKey: apiKey!);
}

builder.Services.AddSingleton<IFunctionInvocationFilter>(new ConsoleAuditFilter(traceEnabled));
builder.Services.AddSingleton<IFunctionInvocationFilter>(
    new RoleAuthorizationFilter(user, RoleAuthorizationFilter.DefaultPolicy));

var kernel = builder.Build();
kernel.Plugins.AddFromObject(new OrderTools(connectionString, user), "Orders");
kernel.Plugins.AddFromObject(new InventoryTools(connectionString), "Inventory");

var chat = kernel.GetRequiredService<IChatCompletionService>();
var settings = new OpenAIPromptExecutionSettings
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

var history = new ChatHistory(SystemPrompt);

Console.WriteLine("ERPPRD01 assistant. Ask about an order. Empty line to quit.");
if (!traceEnabled) Console.WriteLine("Set ERPAGENT_TRACE=1 to see the conversation the kernel builds.");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question)) break;

    history.AddUserMessage(question);

    var depthBefore = history.Count;
    var answer = await chat.GetChatMessageContentAsync(history, settings, kernel);
    var depthAfterCall = history.Count;

    history.Add(answer);

    Console.WriteLine();
    Console.WriteLine(answer.Content);
    Console.WriteLine();

    if (traceEnabled) WriteConversationTrace(history, depthBefore, depthAfterCall);
}

// The kernel appends the tool traffic to the history it was handed: one
// assistant message carrying the call requests, then one message per result.
// Printing the roles makes that visible, which is the whole point of the demo —
// the loop happens inside a single await and is otherwise invisible.
static void WriteConversationTrace(ChatHistory history, int before, int afterCall)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  history: {before} messages before the call, {afterCall} after "
                      + $"— {afterCall - before} appended by the kernel");

    foreach (var message in history)
    {
        var preview = message.Content is { Length: > 0 } text
            ? text.ReplaceLineEndings(" ")[..Math.Min(60, text.Length)]
            : $"[{message.Items.Count} non-text blocks]";
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
internal sealed class ConsoleAuditFilter(bool verbose) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var arguments = string.Join(", ",
            context.Arguments.Select(a => $"{a.Key}: {a.Value}"));

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [tool] {context.Function.Name}({arguments})");

        await next(context);

        var json = JsonSerializer.Serialize(context.Result.GetValue<object>());
        Console.WriteLine($"  [tool] -> {(verbose ? json : Summarise(json))}");
        Console.ResetColor();
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
