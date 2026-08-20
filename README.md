# Legacy ERP Agent Lab

**A deliberately messy, realistically legacy ERP database — built as the test
bench for LLM agents that must operate safely on enterprise systems.**

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

This repo is that database: every quirk is deliberate, hand-crafted onto
specific rows, verified by script, and documented as a **question a real
office user would ask** — see **[docs/tour-of-the-mess.md](docs/tour-of-the-mess.md)**.

## Quick start

Requires Docker. SQL Server 2022 (linux/amd64), lab-only credentials.

```bash
docker compose up -d --wait
for f in 01_schema 02_constraints 03_triggers 04_procs 05_seed 99_verify; do
  docker exec erpprd01 /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P 'LegacyLab!2026' -C -b -i /db/$f.sql
done
```

The last script, `99_verify.sql`, proves every trap fires: expect a column of
`PASS` lines. Connect with any client at `localhost:1433`, user `sa`,
password `LegacyLab!2026`, database `ERPPRD01`.

## The five questions

The heart of the project. Each maps to hand-crafted rows and to a failure mode
of naive agents (full walkthrough in the [tour](docs/tour-of-the-mess.md)):

| # | An office user asks… | The trap underneath |
|---|---|---|
| 1 | "Why is order 1042 blocked?" | Hold logic split across a CHECK, a trigger, and a proc — plus an undocumented 110% tolerance rule |
| 2 | "How many BRK-204 can I promise for Friday?" | Three different answers: the 2011-vintage proc says 30, naive SQL says 70, the truth is "35 now, 55 next week" |
| 3 | "What does order status 'X' mean?" | Two stored procedures disagree — and a cancelled order is what keeps a customer blocked |
| 4 | "Why does this total keep changing?" | Two competing discount implementations fight over the same denormalized column |
| 5 | "Delete order 1058." | Nothing here ever DELETEs. Soft delete is an unwritten convention — with two orphan rows showing what happens when deletion goes wrong |

## Why build the mess on purpose?

Because the mess **is** the benchmark. An agent that can answer the five
questions correctly — with the caveats a resident expert would add — has to
deal with exactly what makes legacy systems hard: knowledge that lives outside
the schema. That's the skill this lab is designed to exercise and demonstrate.

Provenance note: this is an invented **category archetype**, built from scratch
to smell like two decades of real ERPs. It is not derived from any actual
system, structurally or otherwise.

## Roadmap — the agent layer

A .NET / Semantic Kernel application on top of this database, built on a
three-layer trust model:

1. **Curated tools** (`[KernelFunction]` plugins): C# code that *encapsulates*
   the scattered rules — `ExplainOrderHold`, `GetRealAvailability`,
   `ReleaseOrderFromHold`. The model decides *what* to do; the code decides
   *how*. Deterministic retrieval, probabilistic orchestration.
2. **Flexible read path**: RAG over a documented knowledge base of the tribal
   rules + generated SQL on a read-only connection, for questions no tool
   anticipated — answers marked as *inferred*, never *certified*.
3. **Writes**: curated tools only, role-based authorization filters, every
   call written to `FND_AUDIT_TRL`. Prompt-level rules are suggestions;
   code-level filters are enforcement.

Planned deliverable: a benchmark running the five questions (and variants)
against a raw text-to-SQL agent, a RAG-informed agent, and the curated-tool
agent — measuring where each one breaks. The messy rows above are the test
fixtures.

## Layout

```
db/        numbered SQL scripts: schema → constraints → triggers → procs → seed → verify
docs/      tour-of-the-mess.md (the guided tour) · knowledge-base/ (planned, for the agent)
src/       planned: the Semantic Kernel agent
benchmark/ planned: the three-approaches comparison
```

## License

MIT
