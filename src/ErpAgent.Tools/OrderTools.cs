using System.ComponentModel;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel;

namespace ErpAgent.Tools;

public sealed class OrderTools(string connectionString, ErpUser user)
{
    /// <summary>
    /// The 10% release tolerance, agreed verbally with the CFO in 2015. There is
    /// no document; there is only the WHERE clause in SP_REL_ORD_HLD.
    /// </summary>
    private const decimal ReleaseTolerance = 1.10m;

    /// <summary>
    /// FND_AUDIT_TRL is written by stored procedures only. The triggers that set
    /// credit holds and recalculate totals predate it and were never retrofitted,
    /// so the trail is partial by construction and its silence proves nothing.
    /// The tool says so rather than letting an empty list speak for itself.
    /// </summary>
    private const string AuditCaveat =
        "FND_AUDIT_TRL is written by stored procedures only; the triggers that set "
        + "credit holds and recalculate totals never write to it. An empty or short "
        + "trail is not evidence that nothing happened.";

    /// <summary>
    /// The order status codes, as documented in the schema. 'X' is in the list
    /// because it is a legal code, not because anyone agrees what it means.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> StatusMeanings =
        new Dictionary<string, string>
        {
            ["N"] = "new / open",
            ["H"] = "on hold",
            ["P"] = "posted",
            ["S"] = "shipped",
            ["X"] = "cancelled — contested, see the status note"
        };

    private const string StatusNote =
        "Status codes: N new/open, H on hold, P posted, S shipped, X cancelled. "
        + "'X' is read two ways in this system: SP_CANC_ORD and the credit trigger "
        + "treat it as cancelled and stop counting the order, while "
        + "SP_GET_CUST_EXPO counts it as still live. There is therefore no single "
        + "definition of an 'open' order — say which codes you mean.";

    [KernelFunction]
    [Description("""
        Lists orders, newest first, with the filter that was applied stated back.
        Call it with no arguments to see everything, or narrow by customer or by
        status code. Use it when the user has no order number in hand, or asks
        what exists.

        Status must be given as a single code — N, H, P, S or X. Do not translate
        a phrase like "open orders" into codes yourself: the system disagrees with
        itself about which codes count as open, so ask the user which they mean.

        Rows whose customer was deleted or never existed carry a DataWarning.
        Repeat it; those rows look ordinary otherwise.
        """)]
    public async Task<OrderList> ListOrdersAsync(
        [Description("Restrict to one customer account id, for example 100.")]
        int? customerId = null,
        [Description("Restrict to one status code: N, H, P, S or X.")]
        string? status = null)
    {
        if (status is not null && !StatusMeanings.ContainsKey(status.ToUpperInvariant()))
        {
            throw new ArgumentException(
                $"'{status}' is not an order status code. Valid codes are N (new/open), "
                + "H (on hold), P (posted), S (shipped) and X (cancelled). Words like "
                + "'open' or 'active' are ambiguous in this system — the credit trigger "
                + "counts N and H as live, SP_GET_CUST_EXPO also counts X — so ask which "
                + "codes are meant instead of choosing one.",
                nameof(status));
        }

        var code = status?.ToUpperInvariant();

        await using var db = new SqlConnection(connectionString);

        // LEFT JOIN for the same reason as elsewhere: orders outlive their
        // customers here, and there is no foreign key to object.
        var rows = await db.QueryAsync<OrderRow>(
            """
            SELECT h.ORD_HDR_ID   AS OrderId,
                   h.STS_FLG      AS Status,
                   h.HLD_RSN_CD   AS HoldReason,
                   h.CUST_ACCT_ID AS CustomerId,
                   c.PARTY_NAME   AS CustomerName,
                   c.STS_FLG      AS CustomerStatus,
                   h.ORD_TOT_AMT  AS OrderTotal,
                   h.ORD_DT       AS OrderDate
              FROM dbo.OE_ORD_HDR h
              LEFT JOIN dbo.AR_CUST_ACCT c ON c.CUST_ACCT_ID = h.CUST_ACCT_ID
             WHERE (@customerId IS NULL OR h.CUST_ACCT_ID = @customerId)
               AND (@code       IS NULL OR h.STS_FLG      = @code)
             ORDER BY h.ORD_HDR_ID;
            """,
            new { customerId, code });

        var orders = rows.Select(r => new OrderSummary
        {
            OrderId = r.OrderId,
            Status = r.Status,
            StatusMeaning = StatusMeanings.GetValueOrDefault(r.Status, "unknown code"),
            HoldReason = r.HoldReason,
            CustomerId = r.CustomerId,
            CustomerName = r.CustomerName,
            OrderTotal = r.OrderTotal,
            OrderDate = r.OrderDate,
            DataWarning = DescribeCustomerProblem(r)
        }).ToArray();

        return new OrderList
        {
            Orders = orders,
            Count = orders.Length,
            FilterApplied = (customerId, code) switch
            {
                (null, null) => "all orders",
                (int id, null) => $"customer {id}",
                (null, string s) => $"status {s}",
                (int id, string s) => $"customer {id}, status {s}"
            },
            StatusNote = StatusNote,
            CountsByStatus = orders
                .GroupBy(o => o.Status)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    /// <summary>
    /// Written for the person who ends up reading it. The model is told to relay
    /// these notes, so any jargon in them reaches a clerk who does not write SQL:
    /// "soft delete" and "foreign key" are facts about the implementation, not
    /// about the customer's situation, and saying them explains nothing to the
    /// only person who can act.
    /// </summary>
    private static string? DescribeCustomerProblem(OrderRow row) => row switch
    {
        { CustomerName: null } =>
            $"The customer account {row.CustomerId} this order belongs to is gone from "
            + "the system — the record was removed outright, most likely in the 2011 "
            + "migration, and nothing stopped it. There is no way to tell who the "
            + "customer was from this order alone.",
        { CustomerStatus: "X" } =>
            $"The customer account {row.CustomerId} ({row.CustomerName}) is marked as "
            + $"closed, yet this order is still in status {row.Status}. Closing an "
            + "account does not touch its orders in this system, so neither row is "
            + "wrong on its own — but somebody has to decide which one is out of date.",
        _ => null
    };

    private sealed record OrderRow
    {
        public required int OrderId { get; init; }
        public required string Status { get; init; }
        public string? HoldReason { get; init; }
        public required int CustomerId { get; init; }
        public string? CustomerName { get; init; }
        public string? CustomerStatus { get; init; }
        public decimal? OrderTotal { get; init; }
        public required DateTime OrderDate { get; init; }
    }

    [KernelFunction]
    [Description("""
        Returns what a sales order is and what state it is in: status code and
        its meaning, hold reason, customer, total, and the order's audit trail.
        Use it whenever the user names an order number. The audit trail in this
        system is written by stored procedures only and never by triggers, so an
        empty trail is not evidence that nothing happened; report that caveat
        rather than concluding the order was never touched.
        """)]
    public async Task<OrderStatus> GetOrderStatusAsync(
        [Description("The order header id, for example 1042.")] int orderId)
    {
        await using var db = new SqlConnection(connectionString);

        // LEFT JOIN, not INNER: order 1013 points at a customer physically
        // deleted in the 2011 migration and no foreign key ever objected.
        // An inner join would make that order disappear instead of reporting it.
        var order = await db.QuerySingleOrDefaultAsync<OrderStatus>(
            """
            SELECT h.ORD_HDR_ID   AS OrderId,
                   h.STS_FLG      AS Status,
                   h.HLD_RSN_CD   AS HoldReason,
                   h.CUST_ACCT_ID AS CustomerId,
                   c.PARTY_NAME   AS CustomerName,
                   h.ORD_TOT_AMT  AS OrderTotal
              FROM dbo.OE_ORD_HDR h
              LEFT JOIN dbo.AR_CUST_ACCT c ON c.CUST_ACCT_ID = h.CUST_ACCT_ID
             WHERE h.ORD_HDR_ID = @orderId;
            """,
            new { orderId }) ?? throw new OrderNotFoundException(orderId);

        var audit = await db.QueryAsync<AuditEntry>(
            """
            SELECT ACTN_CD AS Action,
                   ACTN_BY AS [By],
                   ACTN_DT AS [At],
                   RMK_TXT AS Remark
              FROM dbo.FND_AUDIT_TRL
             WHERE OBJ_NM = 'OE_ORD_HDR'
               AND OBJ_ID = @orderId
             ORDER BY ACTN_DT;
            """,
            new { orderId });

        return order with
        {
            AuditRows = audit.ToArray(),
            AuditNote = AuditCaveat
        };
    }

    [KernelFunction]
    [Description("""
        Determines whether an order on hold can be released, and returns every
        figure the decision rests on. Read-only: it decides nothing in the
        database and releases nothing. Use it whenever the user asks why an
        order is blocked, whether it can be released, or asks you to release it.

        The credit control has two halves that measure different things: the
        hold compares one exposure against the credit limit, the release
        compares a different exposure against a ceiling 10% above it. Both
        figures and both thresholds are in the response. When CanBeReleased is
        false while WouldPassWithHoldExposure is true, the refusal is caused by
        that disagreement rather than by the customer's real position, and the
        orders responsible are listed in OrdersCountedOnlyByReleaseCheck. Always
        name them: they are the only actionable part of the answer.
        """)]
    public async Task<ReleaseEligibility> CheckReleaseEligibilityAsync(
        [Description("The order header id, for example 1042.")] int orderId)
    {
        await using var db = new SqlConnection(connectionString);

        var order = await db.QuerySingleOrDefaultAsync<HoldRow>(
            """
            SELECT h.CUST_ACCT_ID AS CustomerId,
                   h.STS_FLG      AS Status,
                   h.HLD_RSN_CD   AS HoldReason,
                   c.CR_LMT_AMT   AS CreditLimit
              FROM dbo.OE_ORD_HDR h
              LEFT JOIN dbo.AR_CUST_ACCT c ON c.CUST_ACCT_ID = h.CUST_ACCT_ID
             WHERE h.ORD_HDR_ID = @orderId;
            """,
            new { orderId }) ?? throw new OrderNotFoundException(orderId);

        // Exposure as the credit trigger counts it: statuses N and H.
        var exposureUsedByHoldCheck = await db.ExecuteScalarAsync<decimal>(
            """
            SELECT ISNULL(SUM(ORD_TOT_AMT), 0)
              FROM dbo.OE_ORD_HDR
             WHERE CUST_ACCT_ID = @customerId
               AND STS_FLG IN ('N','H');
            """,
            new { customerId = order.CustomerId });

        // Exposure as the release path counts it. Call the procedure rather than
        // reimplementing it: the tool encapsulates the rule, it does not fork it.
        // If SP_GET_CUST_EXPO is ever corrected, this figure follows on its own.
        var expoParams = new DynamicParameters();
        expoParams.Add("@CUST_ACCT_ID", order.CustomerId);
        expoParams.Add("@EXPO_AMT", dbType: DbType.Decimal,
            direction: ParameterDirection.Output, precision: 15, scale: 2);
        await db.ExecuteAsync("dbo.SP_GET_CUST_EXPO", expoParams,
            commandType: CommandType.StoredProcedure);
        var exposureUsedByReleaseCheck = expoParams.Get<decimal>("@EXPO_AMT");

        var cancelledButCounted = await db.QueryAsync<CountedOrder>(
            """
            SELECT ORD_HDR_ID  AS OrderId,
                   STS_FLG     AS Status,
                   ORD_TOT_AMT AS Amount
              FROM dbo.OE_ORD_HDR
             WHERE CUST_ACCT_ID = @customerId
               AND STS_FLG = 'X'
             ORDER BY ORD_HDR_ID;
            """,
            new { customerId = order.CustomerId });

        var ceiling = order.CreditLimit * ReleaseTolerance;

        var (canBeReleased, blockedBy) = Adjudicate(order, exposureUsedByReleaseCheck, ceiling);

        return new ReleaseEligibility
        {
            OrderId = orderId,
            CanBeReleased = canBeReleased,
            BlockedBy = blockedBy,
            CreditLimit = order.CreditLimit,
            ReleaseCeiling = ceiling,
            ExposureUsedByHoldCheck = exposureUsedByHoldCheck,
            ExposureUsedByReleaseCheck = exposureUsedByReleaseCheck,
            ExposuresDisagree = exposureUsedByHoldCheck != exposureUsedByReleaseCheck,
            WouldPassWithHoldExposure = ceiling is null || exposureUsedByHoldCheck <= ceiling,
            OrdersCountedOnlyByReleaseCheck = cancelledButCounted.ToArray(),
            SourceObjects = ["TRG_OE_ORD_HDR_AI", "SP_REL_ORD_HLD", "SP_GET_CUST_EXPO"]
        };
    }

    [KernelFunction]
    [Description("""
        Releases an order from hold through SP_REL_ORD_HLD, the only sanctioned
        path, and reports what happened. Use it when the user asks to release,
        unblock or approve an order.

        A refusal is a normal result, not a failure: it comes back with
        Released=false and the figures behind it. Report the refusal and what
        would have to change. Do not look for another way to release the order —
        there is no other tool, and there is not meant to be: an update that
        skipped this procedure would skip the credit rules and leave no audit
        trail.

        The acting user is taken from the session. There is no parameter for it,
        and you must not ask the user to supply one.
        """)]
    public async Task<ReleaseOutcome> ReleaseOrderFromHoldAsync(
        [Description("The order header id, for example 1042.")] int orderId)
    {
        var diagnosis = await CheckReleaseEligibilityAsync(orderId);

        await using var db = new SqlConnection(connectionString);

        try
        {
            await db.ExecuteAsync("dbo.SP_REL_ORD_HLD",
                new { ORD_HDR_ID = orderId, USR_NM = user.Name },
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException refusal)
        {
            // The procedure raises before it updates anything, so the order is
            // untouched. Surface the reason it gave rather than a stack trace.
            return new ReleaseOutcome
            {
                OrderId = orderId,
                Released = false,
                RefusedBecause = refusal.Message,
                Diagnosis = diagnosis,
                ActedAs = user.Name,
                Audited = null,
                AuditNote = "Nothing was changed, so there is nothing to audit. "
                            + "Do not report this as an untracked change."
            };
        }

        var audited = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.FND_AUDIT_TRL
             WHERE OBJ_NM = 'OE_ORD_HDR' AND OBJ_ID = @orderId
               AND ACTN_CD = 'REL_HLD' AND ACTN_BY = @actedAs;
            """,
            new { orderId, actedAs = user.Name });

        return new ReleaseOutcome
        {
            OrderId = orderId,
            Released = true,
            ActedAs = user.Name,
            Audited = audited > 0,
            AuditNote = audited > 0
                ? $"Recorded in FND_AUDIT_TRL as REL_HLD by {user.Name}."
                : "The order was released but no audit row was written. Report "
                  + "this: it is not normal for this procedure."
        };
    }

    /// <summary>
    /// The release rules, as SP_REL_ORD_HLD applies them — reproduced here
    /// read-only so the tool can answer "why not" instead of merely failing.
    /// A NULL credit limit skips the check entirely, which is how account 102
    /// ended up with effectively unlimited credit that nobody granted.
    /// </summary>
    private static (bool CanBeReleased, string? BlockedBy) Adjudicate(
        HoldRow order, decimal exposure, decimal? ceiling) => order switch
    {
        { Status: not "H" } => (false, $"order is not on hold (status {order.Status})"),
        { HoldReason: not "CR" } => (true, null),
        _ when ceiling is null => (true, null),
        _ when exposure > ceiling => (false, "release ceiling (110% of credit limit)"),
        _ => (true, null)
    };

    private sealed record HoldRow
    {
        public required int CustomerId { get; init; }
        public required string Status { get; init; }
        public string? HoldReason { get; init; }
        public decimal? CreditLimit { get; init; }
    }
}

public sealed record OrderStatus
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public string? HoldReason { get; init; }
    public required int CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal OrderTotal { get; init; }
    public IReadOnlyList<AuditEntry> AuditRows { get; init; } = [];
    public string AuditNote { get; init; } = "";
}

public sealed record AuditEntry
{
    public required string Action { get; init; }
    public required string By { get; init; }
    public required DateTime At { get; init; }
    public string? Remark { get; init; }
}
