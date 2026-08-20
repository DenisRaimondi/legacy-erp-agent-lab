namespace ErpAgent.Tools;

/// <summary>
/// A listing of orders, with the filter it actually applied stated back. The
/// filter is reported because the obvious ones are ambiguous here: "open" has
/// no single definition in this system, and a listing that silently chose one
/// would be answering a different question than the one asked.
/// </summary>
public sealed record OrderList
{
    public required IReadOnlyList<OrderSummary> Orders { get; init; }
    public required int Count { get; init; }

    /// <summary>What was selected, in words, e.g. "all orders" or "customer 100".</summary>
    public required string FilterApplied { get; init; }

    /// <summary>What the status codes mean, including the one nobody agrees on.</summary>
    public required string StatusNote { get; init; }

    /// <summary>
    /// How many orders carry each status, within the filter applied.
    ///
    /// Tallying a list is the one arithmetic a language model reliably gets
    /// wrong, and a wrong count reads exactly like a right one — asked which
    /// orders were open, the agent counted the rows itself and reported 12
    /// shipped where there were 14.
    ///
    /// Counted in memory over the rows in <see cref="Orders"/> rather than by a
    /// second query. A separate SELECT COUNT would be equally exact but taken at
    /// a different instant, so a concurrent insert could return 31 rows next to
    /// counts summing to 32 — figures that contradict each other inside one
    /// answer, which is the disease this whole system exists to illustrate.
    /// Counting the rows in hand makes agreement structural.
    /// </summary>
    public required IReadOnlyDictionary<string, int> CountsByStatus { get; init; }
}

public sealed record OrderSummary
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required string StatusMeaning { get; init; }
    public string? HoldReason { get; init; }
    public required int CustomerId { get; init; }

    /// <summary>Null when the order points at a customer that no longer exists.</summary>
    public string? CustomerName { get; init; }

    public decimal? OrderTotal { get; init; }
    public required DateTime OrderDate { get; init; }

    /// <summary>
    /// Set when the row is structurally broken — a customer that was deleted,
    /// or one that was never there. Null on an ordinary row. Without it these
    /// look like any other line in a list, which is how they last for a decade.
    /// </summary>
    public string? DataWarning { get; init; }
}
