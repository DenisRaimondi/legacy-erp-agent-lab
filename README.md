# Legacy ERP Agent Lab

**A deliberately messy ERP database, and an LLM agent that answers questions
about it correctly — including the parts the database gets wrong.**

Most "AI agent + database" demos run on clean schemas where text-to-SQL just
works. Real enterprise systems don't look like that. They look like this:

- cryptic table names (`AR_CUST_ACCT`, `OE_ORD_HDR`, `INV_ONHAND_QTY`)
- business logic **scattered** across CHECK constraints, triggers and stored
  procedures — no single place answers "why is this order blocked?"
- undocumented status flags that two stored procedures interpret differently
- denormalized totals maintained by two competing implementations that disagree
- duplicate master data, orphan rows, soft deletes, audit columns that lie

On a schema like this, a generated SQL query can be **syntactically perfect and
semantically wrong** — the schema doesn't tell you that real availability is
on-hand minus committed, or that status `'X'` means two different things
depending on who you ask. That knowledge lives in code and in people's heads.

This repository is both halves: the database, with every quirk hand-crafted onto
specific rows and verified by script, and a .NET / Semantic Kernel agent that
operates on it through curated tools rather than generated SQL.

## What it does

Three transcripts, taken from actual runs.

**"Why is order 1042 blocked?"** — the answer no query can reach:

> It is on hold because the credit exposure exceeded the limit when it was
> entered. The release ceiling would normally allow it — the hold-side exposure
> is €5,182.50 against a €5,500 ceiling. But the release check measures a
> different exposure, €7,410.50, because it also counts **order 1051**, which was
> cancelled in July. That cancelled order is what keeps 1042 blocked, and it is
> the only thing here anyone can act on.

**"How many BRK-204 can I promise for Friday?"** — three defensible answers, and
the tool keeps them apart instead of choosing:

> **35 today**, from MAIN and SEC1. **20 more** are in transit and cannot be
> promised for a date before they land.
>
> The ERP screen shows **30**, because the procedure behind it only counts one
> warehouse — it is not wrong, it is answering a narrower question. If someone
> quotes **70** from a spreadsheet, that is the raw on-hand sum including stock
> already promised to others.

**"Delete order 1058."** — the request the system cannot grant literally:

> Cancelled. In this system that does not mean deleted: the order and its three
> lines are marked cancelled and stay in the database, visible in listings and
> reports. No function exists that removes them.
>
> **10 units of BRK-204** that were reserved for it went back to the warehouse
> and can now be promised to other customers.

Ask for availability again and it is 45. A `DELETE` would have left those ten
reserved forever, nothing would have objected, and every promise made afterwards
would have been short by ten.

## Quick start

Requires Docker and the .NET 9 SDK. Lab-only credentials, safe to publish —
nothing real lives here.

```bash
docker compose up -d --wait
for f in 01_schema 02_constraints 03_triggers 04_procs 05_seed 99_verify; do
  docker exec erpprd01 /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P 'LegacyLab!2026' -C -b -i /db/$f.sql
done
```

`99_verify.sql` proves every trap still fires: expect a column of `PASS` lines.
The database alone is usable — connect any client at `localhost:1433`.

To run the agent you need an API key for an OpenAI-compatible chat model with
function calling. The default is DeepSeek:

```bash
dotnet user-secrets set "DeepSeek:ApiKey" "<key>" --project src/ErpAgent
dotnet run --project src/ErpAgent
```

`dotnet test` runs the suite against the container — 33 integration tests, no
API key needed, because the tools are ordinary code.

| Variable | Effect |
|---|---|
| `ERPAGENT_USER` / `ERPAGENT_ROLE` | who the agent acts for. `sales` may cancel, `credit` may release holds |
| `ERPAGENT_TRACE=1` | print the conversation the kernel builds, including the messages it appends itself |

## The five questions

The database was built around five questions a real office user would ask, each
mapping to hand-crafted rows and to a failure mode of naive agents. Full
walkthrough in the [tour](docs/tour-of-the-mess.md).

| # | An office user asks… | The trap underneath | Agent |
|---|---|---|---|
| 1 | "Why is order 1042 blocked?" | Hold logic split across a CHECK, a trigger and a proc; the human override is calibrated against one exposure and applied to another | ✅ |
| 2 | "How many BRK-204 can I promise for Friday?" | Three answers: the 2011-vintage proc says 30, naive SQL says 70, the truth is "35 now, 55 next week" | ✅ |
| 3 | "What does order status 'X' mean?" | Two stored procedures disagree — and a cancelled order is what keeps a customer blocked | ✅ |
| 4 | "Why does this total keep changing?" | Two competing discount implementations fight over the same denormalized column | planned |
| 5 | "Delete order 1058." | Nothing here ever DELETEs. Soft delete is an unwritten convention | ✅ |

## How the agent is built

Three layers, divided by what happens when the answer is wrong. Full rationale
in [docs/agent-design.md](docs/agent-design.md).

1. **Curated tools** — `[KernelFunction]` methods in C#. The code *encapsulates*
   the scattered rules: it does not bypass them, and it does not silently
   correct them. Six tools today, four reads and two writes.
2. **Flexible read path** — RAG over the tribal knowledge plus generated SQL on
   a read-only connection, for questions no tool anticipated, marked *inferred*
   rather than certified. Planned.
3. **Writes** — curated tools only, role filters, everything audited. There is
   no delete tool, so soft delete holds by construction.

Two rules do most of the work.

**A tool returns every variable bearing on the decision, and names the
disagreements it found.** Customer 100 has two exposure figures that contradict
each other; a tool returning one of them lets the model produce an answer that
is fluent, plausible and wrong — intermittently, which is worse. So both come
back, along with which one the release path uses and which orders make them
differ.

**Rules in a prompt are suggestions; filters in code are enforcement.**
Authorization runs on the invocation, not the conversation, so the function body
does not execute. And the policy is a table where neither role contains the
other: order entry cancels, credit control releases. A hierarchy of permission
levels would have had to invent a rank the business does not have.

## Designing around what the model gets wrong

Every design decision here came from watching a specific failure, not from a
principle. Each row below is something that actually happened during
development.

| The model… | did this | the design answer |
|---|---|---|
| cannot count | reported 12 shipped orders where there were 14 | counts come back with the list, computed in code |
| fills ambiguity with plausibility | read `Audited: false` on a refused write as "it was done and not recorded" | three states, and a note saying which — `null` when nothing happened |
| reads an exception as a broken system | turned a null reference into "the record may be corrupt" | missing rows are an outcome, and the message says a gap here is normal |
| wants to be helpful | would have found the `UPDATE` that skips the credit rules | that route is not in the callable surface |
| re-derives what it already has | recomputed a verdict a boolean already stated, correcting itself mid-sentence | relay, do not recompute — and the decision is already a field |
| does not know what is not in the schema | would say "€5,182 over a €5,000 limit, shall I release it?" | the tribal rules are encapsulated in the tools |
| will use whatever it is handed | would have supplied its own user name for the audit trail | identity is a constructor argument, never a tool parameter |

The mirror image matters too, or there would be no reason to use a model at all.
In the same sessions it resolved "try it" against an action named two turns
earlier, chained tools nobody told it to chain, held three caveats from three
sources together in one readable paragraph, and — asked which orders were open —
**refused to answer** and asked which status codes were meant, because the
system does not define the word.

There is a symmetry worth stating. The database's central defect is a flag that
two procedures read differently, and it has cost a customer their credit line
for ten years. `Audited: false` was the same defect, in code written this week:
a field admitting two readings, and something downstream picking the wrong one.
Designing tools for a model turns out to be the same discipline as designing
schemas for people.

## Why build the mess on purpose

Because the mess **is** the benchmark. An agent that answers the five questions
the way a resident expert would — caveats included — has to deal with exactly
what makes legacy systems hard: knowledge that lives outside the schema.

The agent is not here to repair the database. Defects like these outlive the
people who introduce them, because fixing them means auditing every caller and
re-calibrating thresholds that were tuned against the broken figures. The order
is stuck today. The agent makes the system legible while the defects are still
there — and is the tool that makes the impact analysis for a future fix possible
at all.

Provenance note: this is an invented **category archetype**, built from scratch
to smell like two decades of real ERPs. It is not derived from any actual
system, structurally or otherwise.

## Layout

```
db/         numbered SQL scripts: schema → constraints → triggers → procs → seed → verify
            98_reset_demo.sql restores order 1058 so the cancellation demo can be repeated
docs/       tour-of-the-mess.md (the guided tour) · agent-design.md (why the agent is shaped this way)
src/        ErpAgent.Tools — the tools and data access, no API key needed to test
            ErpAgent      — kernel, chat loop, authorization and audit filters
tests/      33 integration tests against the container
benchmark/  planned: the same questions against text-to-SQL, RAG and curated tools
```

## Planned

A benchmark running the five questions against a raw text-to-SQL agent, a
RAG-informed agent and the curated-tool agent, measuring where each breaks. A
web front end with a live audit panel — the point being not that the answer is
right, but that you can see what the agent did to get there.

## License

MIT
