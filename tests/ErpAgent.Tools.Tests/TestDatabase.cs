namespace ErpAgent.Tools.Tests;

/// <summary>
/// Connection to the lab database. These tests run against the real container
/// from <c>db/</c> — the tools exist to encapsulate the behaviour of that
/// specific database, so a mock would only prove the mock matches the guess.
/// </summary>
internal static class TestDatabase
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("ERPPRD01_CONNECTION")
        ?? "Server=localhost,1433;Database=ERPPRD01;User Id=sa;Password=LegacyLab!2026;"
           + "TrustServerCertificate=True;Encrypt=False";

    public static OrderTools Tools() => ToolsFor("VERIFY", "credit");

    public static OrderTools ToolsFor(string userName, string role) =>
        new(ConnectionString, new ErpUser(userName, role));
}
