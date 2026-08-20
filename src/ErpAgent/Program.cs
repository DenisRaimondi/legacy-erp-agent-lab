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

Console.WriteLine("ERPPRD01 assistant. Ask about an order. Empty line to quit.");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question)) break;

    history.AddUserMessage(question);
    var answer = await chat.GetChatMessageContentAsync(history, settings, kernel);
    history.Add(answer);

    Console.WriteLine();
    Console.WriteLine(answer.Content);
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
