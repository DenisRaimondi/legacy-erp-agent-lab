namespace ErpAgent.Tools;

/// <summary>
/// Whether an order can be released from hold, and every figure the decision
/// rests on. Both exposures appear because the two halves of the credit control
/// measure different things: a report carrying one of them cannot explain a
/// refusal, and would let a fluent, confident, wrong answer through.
/// </summary>
public sealed record ReleaseEligibility
{
    public required int OrderId { get; init; }
    public required bool CanBeReleased { get; init; }

    /// <summary>What refuses the release, or null when it would go through.</summary>
    public string? BlockedBy { get; init; }

    public decimal? CreditLimit { get; init; }

    /// <summary>The limit plus the 10% tolerance agreed verbally with the CFO in 2015.</summary>
    public decimal? ReleaseCeiling { get; init; }

    /// <summary>Exposure as the credit trigger counts it: statuses N and H.</summary>
    public required decimal ExposureUsedByHoldCheck { get; init; }

    /// <summary>Exposure as SP_GET_CUST_EXPO counts it: statuses N, H and X.</summary>
    public required decimal ExposureUsedByReleaseCheck { get; init; }

    public required bool ExposuresDisagree { get; init; }

    /// <summary>
    /// True when the release would go through if it used the same exposure the
    /// hold used. When this is true and <see cref="CanBeReleased"/> is false,
    /// the refusal is caused by the disagreement rather than by the customer.
    /// </summary>
    public required bool WouldPassWithHoldExposure { get; init; }

    /// <summary>
    /// The cancelled orders that only the release-side exposure counts. This is
    /// the actionable part: without it the clerk knows they are stuck, with it
    /// they know what to ask for.
    /// </summary>
    public IReadOnlyList<CountedOrder> OrdersCountedOnlyByReleaseCheck { get; init; } = [];

    public IReadOnlyList<string> SourceObjects { get; init; } = [];
}

public sealed record CountedOrder
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
}
