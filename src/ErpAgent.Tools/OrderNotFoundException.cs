namespace ErpAgent.Tools;

/// <summary>
/// Raised when an order id has no row. The message goes back to the model as
/// the tool's result, so it says what a missing id means in this system rather
/// than only that the lookup failed: order ids run from 1001 to 1078 with 31
/// orders between them, because rows were lost in the 2011 migration. An absent
/// id is an ordinary outcome here, and it is not proof the order never existed.
/// </summary>
public sealed class OrderNotFoundException(int orderId)
    : Exception(
        $"No order {orderId} exists in OE_ORD_HDR. Order ids in this database are "
        + "not contiguous: rows were lost in the 2011 migration, so a missing id "
        + "is common and does not prove the order never existed. Report it as "
        + "absent from the system, not as a failure or as corrupt data.")
{
    public int OrderId { get; } = orderId;
}
