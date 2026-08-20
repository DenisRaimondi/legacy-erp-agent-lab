namespace ErpAgent.Tools;

/// <summary>
/// Raised when an item code has no row in INV_ITEM_MST. Unlike a missing order
/// id, a missing item code usually means the code was mistyped or belongs to
/// another system — items were not lost in the migration the way orders were.
/// </summary>
public sealed class ItemNotFoundException(string itemCode)
    : Exception(
        $"No item with code '{itemCode}' exists in INV_ITEM_MST. Item codes are "
        + "case-sensitive here and follow shapes like BRK-204 or BLT-M8-40. Ask the "
        + "user to check the code rather than guessing at a similar one.")
{
    public string ItemCode { get; } = itemCode;
}
