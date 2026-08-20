namespace ErpAgent.Tools;

/// <summary>
/// The result of attempting the sanctioned release. A refusal is an expected
/// outcome rather than an error: the user has to be told why, so the diagnosis
/// travels with it. Reporting a refusal as a failure would invite the model to
/// look for another way — and on this database there is one, which is exactly
/// what must not happen.
/// </summary>
public sealed record ReleaseOutcome
{
    public required int OrderId { get; init; }
    public required bool Released { get; init; }

    /// <summary>Null when the order was released.</summary>
    public string? RefusedBecause { get; init; }

    /// <summary>
    /// The figures behind the refusal, so the answer can name what would have
    /// to change. Null when the release succeeded.
    /// </summary>
    public ReleaseEligibility? Diagnosis { get; init; }

    /// <summary>Who the release was recorded against. Comes from the session, not the model.</summary>
    public required string ActedAs { get; init; }

    /// <summary>
    /// True when SP_REL_ORD_HLD wrote the audit row, false when a change was
    /// made without one, and <b>null when nothing was changed at all</b>.
    ///
    /// The three states matter. A plain false on a refusal reads as "it was done
    /// and not recorded" — a specific and alarming claim in a database whose own
    /// documented caveat is that a missing audit row proves nothing. Null keeps
    /// the model from making it.
    /// </summary>
    public bool? Audited { get; init; }

    /// <summary>Says in words what <see cref="Audited"/> means here, so the model can pass it through.</summary>
    public required string AuditNote { get; init; }
}
