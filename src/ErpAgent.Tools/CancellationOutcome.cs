namespace ErpAgent.Tools;

/// <summary>
/// What actually happened when an order was cancelled — which is not what the
/// word suggests. Nothing is removed: the order and its lines are marked X, the
/// stock they had reserved goes back, and a row is written to the audit trail.
///
/// The stock is the part worth reporting. A deletion would have left those
/// quantities reserved for an order that no longer existed, nothing would have
/// complained, and every promise made afterwards would have been short.
/// </summary>
public sealed record CancellationOutcome
{
    public required int OrderId { get; init; }
    public required bool Cancelled { get; init; }

    /// <summary>Null when the order was cancelled.</summary>
    public string? RefusedBecause { get; init; }

    /// <summary>Says plainly that the order still exists, so the answer does not imply deletion.</summary>
    public required string WhatWasDone { get; init; }

    public int LinesCancelled { get; init; }

    /// <summary>
    /// Quantities handed back to the warehouse, measured before and after rather
    /// than assumed from the order lines. Lines for items that carry no stock —
    /// a freight charge, an item nobody stocks — are absent, because nothing was
    /// released for them.
    /// </summary>
    public IReadOnlyList<ReleasedStock> StockReleased { get; init; } = [];

    public required string ActedAs { get; init; }

    /// <summary>True when the audit row was written, null when nothing was changed.</summary>
    public bool? Audited { get; init; }

    public required string AuditNote { get; init; }
}

public sealed record ReleasedStock
{
    public required string ItemCode { get; init; }
    public required string WarehouseCode { get; init; }
    public required decimal Quantity { get; init; }
}
