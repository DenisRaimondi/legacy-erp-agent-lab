using System.ComponentModel;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel;

namespace ErpAgent.Tools;

public sealed class InventoryTools(string connectionString)
{
    /// <summary>
    /// What each warehouse code means and whether its stock can be promised
    /// today. None of this is in the database — there is no warehouse table.
    /// It lives in the schema comments and in the heads of the people who work
    /// there, which is precisely the knowledge a generated query cannot have.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Meaning, bool Available)> Warehouses =
        new Dictionary<string, (string, bool)>
        {
            ["MAIN"] = ("central warehouse", true),
            ["SEC1"] = ("secondary warehouse", true),
            ["TRNS"] = ("goods in transit — bought and moving, not yet on any shelf", false),
            ["QC01"] = ("quarantine, awaiting quality inspection — may never be released", false)
        };

    [KernelFunction]
    [Description("""
        Reports how much of an item can be promised and by when. Use it whenever
        someone asks what is in stock, what is available, or how many they can
        commit to a customer.

        There is no single stock figure in this system, so do not reduce the
        answer to one. AvailableNow is what can ship today; ArrivingLater is in
        transit and cannot be promised for a date before it lands. Give both,
        with the timing.

        AccordingToTheOfficialProcedure is what the ERP screen shows, and it is
        lower because it only looks at one warehouse. Mention it when it differs,
        so the user is not surprised when the screen disagrees with you.

        RawSumOfOnHand is the figure that turns up on spreadsheets. Use it only
        to reconcile if someone quotes it; never present it as the answer.
        """)]
    public async Task<ItemAvailability> GetItemAvailabilityAsync(
        [Description("The item code, for example BRK-204.")] string itemCode)
    {
        await using var db = new SqlConnection(connectionString);

        var item = await db.QuerySingleOrDefaultAsync<ItemRow>(
            """
            SELECT ITEM_ID AS ItemId, ITEM_CD AS ItemCode, ITEM_DESC AS Description
              FROM dbo.INV_ITEM_MST WHERE ITEM_CD = @itemCode;
            """,
            new { itemCode }) ?? throw new ItemNotFoundException(itemCode);

        var stock = (await db.QueryAsync<StockRow>(
            """
            SELECT WHSE_CD AS WarehouseCode, QTY_OH AS OnHand, QTY_COMM AS Committed
              FROM dbo.INV_ONHAND_QTY WHERE ITEM_ID = @itemId ORDER BY WHSE_CD;
            """,
            new { itemId = item.ItemId })).ToArray();

        // Call the procedure rather than reproducing its WHERE clause: the tool
        // reports what the ERP screen shows, and follows it if it is ever fixed.
        var officialParams = new DynamicParameters();
        officialParams.Add("@ITEM_CD", itemCode);
        officialParams.Add("@AVL_QTY", dbType: DbType.Decimal,
            direction: ParameterDirection.Output, precision: 15, scale: 3);
        await db.ExecuteAsync("dbo.SP_GET_ITM_AVL", officialParams,
            commandType: CommandType.StoredProcedure);

        var byWarehouse = stock.Select(s =>
        {
            var known = Warehouses.TryGetValue(s.WarehouseCode, out var w)
                ? w
                : ($"unknown warehouse code '{s.WarehouseCode}' — no reference table exists", false);

            return new WarehouseStock
            {
                WarehouseCode = s.WarehouseCode,
                Meaning = known.Item1,
                OnHand = s.OnHand,
                Committed = s.Committed,
                Net = s.OnHand - s.Committed,
                CountsAsAvailableNow = known.Item2
            };
        }).ToArray();

        var availableNow = byWarehouse.Where(w => w.CountsAsAvailableNow).Sum(w => w.Net);
        var arrivingLater = byWarehouse.Where(w => w.WarehouseCode == "TRNS").Sum(w => w.Net);

        return new ItemAvailability
        {
            ItemCode = item.ItemCode,
            Description = item.Description,
            AvailableNow = availableNow,
            ArrivingLater = arrivingLater,
            TotalNet = availableNow + arrivingLater,
            AccordingToTheOfficialProcedure = officialParams.Get<decimal>("@AVL_QTY"),
            OfficialProcedureCaveat =
                "SP_GET_ITM_AVL counts warehouse MAIN only. The clause dates from 2011, "
                + "when MAIN was the only warehouse; SEC1, TRNS and QC01 were added later "
                + "and nobody revisited it. It is not wrong about MAIN — it is answering a "
                + "narrower question.",
            RawSumOfOnHand = stock.Sum(s => s.OnHand),
            ByWarehouse = byWarehouse,
            Warning = DescribeOversell(byWarehouse)
        };
    }

    /// <summary>
    /// Nothing prevents committing more than exists, and something already has.
    /// A bare negative number would be arithmetically correct and say nothing
    /// about what it costs — some order is not going to be filled.
    /// </summary>
    private static string? DescribeOversell(IReadOnlyList<WarehouseStock> stock)
    {
        var oversold = stock.Where(w => w.Net < 0).ToArray();
        if (oversold.Length == 0) return null;

        var places = string.Join("; ", oversold.Select(w =>
            $"{w.WarehouseCode} has {w.Committed:0.###} committed against {w.OnHand:0.###} on hand"));

        return $"{places}. More has been promised to customers than exists, and nothing in "
               + "the database prevents that. Some order will not be filled — say so rather "
               + "than reporting the negative figure on its own.";
    }

    private sealed record ItemRow
    {
        public required int ItemId { get; init; }
        public required string ItemCode { get; init; }
        public required string Description { get; init; }
    }

    private sealed record StockRow
    {
        public required string WarehouseCode { get; init; }
        public required decimal OnHand { get; init; }
        public required decimal Committed { get; init; }
    }
}
