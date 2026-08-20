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

    Reply in the language the user writes in, in plain prose, briefly, the way a
    colleague who knows the system would explain it to someone who does not.
    """;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = configuration["DeepSeek:ApiKey"]
    ?? configuration["DEEPSEEK_API_KEY"]
    ?? throw new InvalidOperationException(
        "No DeepSeek API key. Set it with:\n"
        + "  dotnet user-secrets set \"DeepSeek:ApiKey\" \"<key>\" --project src/ErpAgent\n"
        + "or export DEEPSEEK_API_KEY in the environment.");

var connectionString =
    configuration["ERPPRD01_CONNECTION"]
    ?? "Server=localhost,1433;Database=ERPPRD01;User Id=sa;Password=LegacyLab!2026;"
       + "TrustServerCertificate=True;Encrypt=False";

var builder = Kernel.CreateBuilder();

// DeepSeek speaks the OpenAI wire protocol, so the OpenAI connector works
// unchanged against its endpoint. deepseek-chat, not deepseek-reasoner: this
// agent is nothing without function calling, and the reasoner does not do it.
builder.AddOpenAIChatCompletion(
    modelId: "deepseek-chat",
    endpoint: new Uri("https://api.deepseek.com/v1"),
    apiKey: apiKey);

builder.Services.AddSingleton<IFunctionInvocationFilter>(new ConsoleAuditFilter());

var kernel = builder.Build();
kernel.Plugins.AddFromObject(new OrderTools(connectionString), "Orders");

var chat = kernel.GetRequiredService<IChatCompletionService>();
var settings = new OpenAIPromptExecutionSettings
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

var history = new ChatHistory(SystemPrompt);
var traceEnabled = configuration["ERPAGENT_TRACE"] == "1";

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
/// </summary>
internal sealed class ConsoleAuditFilter : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var arguments = string.Join(", ",
            context.Arguments.Select(a => $"{a.Key}: {a.Value}"));

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [tool] {context.Function.Name}({arguments})");

        await next(context);

        var result = context.Result.GetValue<object>();
        Console.WriteLine($"  [tool] -> {JsonSerializer.Serialize(result)}");
        Console.ResetColor();
    }
}
