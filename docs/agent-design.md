# Agent design

The database in `db/` is the test bench. This document describes the agent that
runs on top of it: what it is allowed to do, where the knowledge lives, and why
the boundaries sit where they do.

It describes the design in full. The code implements it in slices — the first
slice is stated at the end.

---

## The problem the agent solves

A clerk looks at order 1042 and sees three fields: *on hold*, *reason CR*,
*total 2,600.00*. Everything that explains those fields lives somewhere the
clerk cannot reach — in a trigger, in a stored procedure, in a threshold agreed
verbally in 2015, in the difference between two `WHERE` clauses.

So the clerk asks a colleague, and the colleague answers from memory. The agent
is an attempt to answer from the system instead.

Note what the agent is *not* for: repairing the database. The defects in
`ERPPRD01` are the kind that outlive the people who introduced them, because
fixing them means auditing every caller, re-calibrating thresholds that were
tuned against the broken figures, and getting a sign-off nobody wants to give.
The order is stuck today. The agent makes the system legible while the defects
are still there — and, incidentally, is the tool that makes the impact analysis
for a future fix possible at all.

## Three layers, divided by read versus write

The dividing line is not "tools versus knowledge". It is what happens when the
answer is wrong.

**1. Curated tools** — `[KernelFunction]` methods in C#. Deterministic
retrieval. The code *encapsulates* the scattered rules: it does not bypass them
and it does not silently correct them. Reasoning above stays probabilistic —
the model chooses which tool to call and writes the prose — so tool
descriptions, audit and tests carry the weight.

Curated tools are expensive: a human who knows the system has to write each
rule down. That cost only repays on the questions the office asks every day,
and on every write. It does not scale to a whole ERP, and it is not meant to.

**2. Flexible read path** — RAG over a knowledge base of the tribal rules, plus
generated SQL on a read-only connection, for questions no tool anticipated.
This is where the long tail lives: knowledge written once in prose instead of
once in C#. Cheap to produce, probabilistic to retrieve. Answers from this path
are marked **inferred**, never certified.

**3. Writes** — curated tools only, role filters, every call written to
`FND_AUDIT_TRL`. No delete tool exists at all, so soft delete holds by
construction: the agent cannot violate the convention even when instructed to.

Guiding principle: *the model decides what, the code decides how.* Rules stated
in a prompt are suggestions; filters in code are enforcement.

## Tool contracts: every variable, or none

A tool must not leave the model an inference it cannot reliably make.

Customer 100 has two exposure figures. The hold check computed 5,182.50
(statuses `N`,`H`); the release check computes 7,410.50 (`N`,`H`,`X`, counting
an order everyone believes cancelled). A tool that returns one number lets the
model produce an answer that is fluent, plausible and wrong — and lets it do so
intermittently, which is worse.

So the rule is: **a tool returns every variable that bears on the decision, and
names the disagreements it found.** The model's job is to write the sentence,
not to reconstruct the system.

Two consequences worth stating explicitly.

*The tool reports the defect; it does not apply it and it does not correct it.*
Returning only the defensible figure — 5,182.50 — would make the agent promise
a release that the database will refuse. The user's problem is caused by the
system as it is. The tool describes that, and labels which reading is
defensible.

*Naming the disagreement is not endorsing it.* Encoding "exposure has two
readings" in C# does not add a rule to the world. That rule already runs every
day, in someone's head, unversioned and unreviewable. Writing it down moves it
somewhere it can be tested and read.

## The first two tools

Scoped to the walkthrough's first question — *why is order 1042 blocked?* — and
its follow-up, *so release it, then*.

### `GetOrderStatus(orderId)`

What the order is and what state it is in.

```jsonc
{
  "orderId": 1042,
  "status": "H", "statusMeaning": "on hold",
  "holdReason": "CR", "holdReasonMeaning": "credit limit exceeded at insert",
  "customer": { "id": 100, "name": "Rossi Impianti S.p.A." },
  "orderTotal": 2600.00,
  "orderDate": "2026-04-18",
  "auditRows": [],
  "auditNote": "none: holds are set by a trigger that never writes FND_AUDIT_TRL"
}
```

`auditNote` exists because an empty array is ambiguous here. Absence of audit in
this database is not evidence that nothing happened — it is evidence that a
trigger did it.

### `CheckReleaseEligibility(orderId)`

Whether the order can be released. Read-only: it reproduces the release
procedure's reasoning in C# without executing it, so it can answer *why not*
instead of merely failing.

```jsonc
{
  "orderId": 1042,
  "canBeReleased": false,
  "blockedBy": "release ceiling (110% of credit limit)",
  "creditLimit": 5000.00,
  "releaseCeiling": 5500.00,
  "exposureUsedByReleaseCheck": 7410.50,
  "exposureUsedByHoldCheck": 5182.50,
  "exposuresDisagree": true,
  "wouldPassWithHoldExposure": true,
  "ordersCountedOnlyByReleaseCheck": [
    { "orderId": 1051, "status": "X", "amount": 2228.00 }
  ],
  "sourceObjects": ["SP_REL_ORD_HLD", "SP_GET_CUST_EXPO"]
}
```

`canBeReleased` is a decision the code makes, not one the model derives.
`ordersCountedOnlyByReleaseCheck` is the only actionable field in the whole
response: without it the clerk knows they are stuck, with it they know what to
ask for.

The two thresholds are deliberate — the system holds above 100% of the limit, a
human may override up to 110%, past 110% nobody can. That control is sound. What
breaks it is that its two halves measure different exposures, so the tolerance
was calibrated against one figure and is applied to another. Both thresholds and
both figures appear in the response for exactly that reason.

Releasing an order for real is a different tool, `ReleaseOrderFromHold`. It
writes, so it belongs to layer 3 and arrives with the role filters and the audit
trail.

## Structure

```
src/ErpAgent/            console host: kernel, chat loop, configuration
src/ErpAgent.Tools/      [KernelFunction] tools and data access
tests/ErpAgent.Tools.Tests/  integration tests against the container
```

**Data access: Dapper** over `Microsoft.Data.SqlClient`. The SQL stays explicit
and readable in the source, which matters here — the queries are part of what
this repo is showing. An ORM would hide them, and on a schema of this vintage it
would fight the stored procedures rather than help.

**Provider-agnostic by configuration.** The agent talks to an `IChatClient` and
never learns which service answered: Azure OpenAI when an endpoint and key are
set, DeepSeek otherwise. Function calling is a hard requirement either way,
which on DeepSeek rules out `deepseek-reasoner`. DeepSeek has no embeddings
endpoint, so the RAG layer will need a separate embedding source; that decision
belongs to layer 2 and is deferred.

**Configuration**: `DeepSeek:ApiKey` resolved from .NET user secrets first, then
the `DEEPSEEK_API_KEY` environment variable, then a clear error naming both. No
key is ever stored in the repository.

**Tests run against the real container**, not mocks. These tools exist to
encapsulate the behaviour of *this* database; a mock would only prove that the
mock matches the assumption. The container starts in two minutes from `db/`, so
there is no reason to fake it.

## Acceptance

The first slice is done when the agent, asked *"why is order 1042 blocked?"*,
answers with all three of:

1. it is on hold because the credit exposure exceeded the limit at insert,
2. the release ceiling would normally allow it — the hold-side exposure is
   inside the tolerance,
3. release fails anyway, because a cancelled order is still counted, **and that
   order is 1051**.

An answer containing only point 1 is fluent, professional and useless. That is
the failure mode the whole design exists to prevent.

## Deferred, deliberately

Role-based authorization filters · the audit interceptor · `ReleaseOrderFromHold`
and the other writes · the RAG read path and its knowledge base · the web UI with
the live audit panel · the three-way benchmark. Each is designed above; none is
needed to make one real question work end to end, and building them first would
produce three half-finished layers instead of one working answer.
