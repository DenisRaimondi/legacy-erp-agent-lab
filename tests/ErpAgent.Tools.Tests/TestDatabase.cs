using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

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

    public static InventoryTools Inventory() => new(ConnectionString);

    public static OrderTools ToolsFor(string userName, string role) =>
        new(ConnectionString, new ErpUser(userName, role));

    /// <summary>
    /// Runs db/98_reset_demo.sql, putting order 1058 and the stock it commits
    /// back the way the seed left them. Cancelling is the one behaviour under
    /// test that really changes the database, so the tests that exercise it
    /// restore the fixture rather than leaving the next run to guess.
    ///
    /// The script is located from this file's compile-time path, so the tests do
    /// not depend on where the runner happens to put the binaries.
    /// </summary>
    public static async Task ResetDemoStateAsync([CallerFilePath] string thisFile = "")
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", ".."));
        var script = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "db", "98_reset_demo.sql"));

        // GO is a sqlcmd batch separator, not T-SQL: the driver rejects it.
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline)
            .Where(b => !string.IsNullOrWhiteSpace(b));

        await using var db = new SqlConnection(ConnectionString);
        foreach (var batch in batches) await db.ExecuteAsync(batch);
    }
}
