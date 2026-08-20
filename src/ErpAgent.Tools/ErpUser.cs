namespace ErpAgent.Tools;

/// <summary>
/// Who the agent is acting for. Supplied by the host from the real session,
/// never by the model: identity passed as a tool argument is identity the model
/// can invent, and it would be the name written to the audit trail.
/// </summary>
/// <param name="Name">The ERP account name recorded in FND_AUDIT_TRL.</param>
/// <param name="Role">Decides which tools may run at all. See RoleAuthorizationFilter.</param>
public sealed record ErpUser(string Name, string Role);
