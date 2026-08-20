namespace ErpAgent.Tools;

/// <summary>
/// How much of an item can be promised, and by when.
///
/// There is no single right number here, and that is the point. The official
/// procedure, a raw sum over the stock table, and the answer the warehouse
/// manager would give are all defensible — they answer different questions. A
/// tool that returned one figure would be choosing which question was asked.
/// </summary>
public sealed record ItemAvailability
{
    public required string ItemCode { get; init; }
    public required string Description { get; init; }

    /// <summary>On a shelf, not already promised to someone else. What can be shipped today.</summary>
    public required decimal AvailableNow { get; init; }

    /// <summary>Paid for and moving, but not anywhere it can be picked from yet.</summary>
    public required decimal ArrivingLater { get; init; }

    /// <summary><see cref="AvailableNow"/> plus <see cref="ArrivingLater"/>. Quarantined stock is in neither.</summary>
    public required decimal TotalNet { get; init; }

    /// <summary>What SP_GET_ITM_AVL returns.</summary>
    public required decimal AccordingToTheOfficialProcedure { get; init; }

    /// <summary>Why that figure is lower, so it reads as a narrower question rather than a contradiction.</summary>
    public required string OfficialProcedureCaveat { get; init; }

    /// <summary>
    /// SUM(QTY_OH) across every warehouse, ignoring commitments and location.
    /// Included because it is what a generated query returns and what appears on
    /// spreadsheets around the office — so the agent can reconcile with it when
    /// somebody quotes it, not because it answers anything.
    /// </summary>
    public required decimal RawSumOfOnHand { get; init; }

    public required IReadOnlyList<WarehouseStock> ByWarehouse { get; init; }

    /// <summary>Set when the stock itself is in an impossible state. Null otherwise.</summary>
    public string? Warning { get; init; }
}

public sealed record WarehouseStock
{
    public required string WarehouseCode { get; init; }
    public required string Meaning { get; init; }
    public required decimal OnHand { get; init; }
    public required decimal Committed { get; init; }

    /// <summary>On hand minus committed. Negative when more has been promised than exists.</summary>
    public required decimal Net { get; init; }

    /// <summary>Whether this stock can be promised for today.</summary>
    public required bool CountsAsAvailableNow { get; init; }
}
