# Tour of the mess

Every quirk in ERPPRD01 is deliberate, lives on specific rows, and is phrased
here as a **question a real office user would ask**. These questions double as
the acceptance tests for the planned agent layer: an agent operating on this
database is correct when it answers all of them the way the resident expert
would — including the caveats.

> Legend of the recurring characters:
> `CONV` = the 2011 migration user · `SA` = someone used the sysadmin account
> · `TRG_*` = a trigger touched the row · status `X` = it's complicated.

---

## Q1 — "Why is order 1042 (Rossi) blocked?"

**The trap:** no single object answers this. The order-hold logic is split
across three places:

| Piece | Object | What it knows |
|---|---|---|
| Valid status codes | `CK_OE_HDR_STS` (CHECK) | `'H'` is a legal status. Nothing more. |
| Who goes ON hold | `TRG_OE_ORD_HDR_AI` (trigger) | Credit check at insert: exposure (statuses `N`,`H`) vs `CR_LMT_AMT`, **no tolerance** |
| Who comes OFF hold | `SP_REL_ORD_HLD` (proc) | The release rules, including the **10% tolerance** ("agreed verbally with the CFO in 2015") |

**The rows:** customer 100 (Rossi Impianti, limit **5,000**). Order 1030
arrived in March (2,582.50) — under the limit, no hold. Order 1042 arrived in
April (2,600.00), taking exposure to **5,182.50**, and the trigger held it on
the spot with reason `CR`. The order was *born* blocked; nobody decided
anything. Note also that the trigger only holds the row it just inserted —
1030 stays `N` even though it accounts for half the overage. Last one in pays.

**The two thresholds are deliberate, and sane.** The system stops on its own
above 100% of the limit; a human may override up to 110%; past 110% nobody
can. Automatic flag, human judgement, hard ceiling — a four-eyes control of
the kind a CFO actually asks for. Rossi's 5,182.50 sits comfortably inside
that override band, so releasing 1042 should be a formality.

**Q1b — "Fine, override it then."** `SP_REL_ORD_HLD` refuses:

```
SP_REL_ORD_HLD: credit release refused, exposure exceeds 110% of limit.
```

Its check runs on `SP_GET_CUST_EXPO`, which **also counts order 1051 — an
order everyone believes is cancelled** (status `X`, 2,228.00). Exposure
becomes 7,410.50, past the 5,500 ceiling. See Q3.

**The real defect is not the asymmetry — it is the mismatch.** The two halves
of one credit control measure two different things: the hold looks at
5,182.50, the override looks at 7,410.50. The 10% tolerance was calibrated
against the first number and is applied to the second. Management's escape
hatch is unusable, and nothing in the schema says so.

The clerk sees none of this. The screen says *order 1042 — on hold — reason
CR*, and the error message claims an exposure that contradicts the figures in
front of them. That is where the office legend comes from: "if release fails,
call IT, they have a script."

**Audit note:** there is **no audit row** for the 1042 hold. The trigger
predates `FND_AUDIT_TRL` and never writes it. Absence of audit proves nothing
in this system.

---

## Q2 — "How many BRK-204 can I promise a customer for Friday?"

**The trap:** three different answers depending on how you compute it.

| Who answers | Method | Result |
|---|---|---|
| The official proc `SP_GET_ITM_AVL` | on hand − committed, **warehouse `MAIN` only** (hardcoded in 2011, when MAIN was the only warehouse) | **30** |
| A naive `SUM(QTY_OH)` over the raw table | counts transit and ignores commitments | **70** |
| The resident expert | MAIN net (30) + SEC1 (5) + TRNS arriving (20) | **"55, but 20 of those are still in transit — promise 35 for Friday, 55 for next week"** |

**The rows:** item 5000 (`BRK-204`) in `INV_ONHAND_QTY`:
`MAIN` 45/15 committed · `SEC1` 5/0 · `TRNS` 20/0.
The 15 committed = order 1030 (qty 5, Rossi) + order 1058 (qty 10, Van Dijk).

**Bonus smell:** `BLT-M8-40` is oversold — 120 committed vs 100 on hand.
No constraint objects. Everybody in the office knows about the bolts.

---

## Q3 — "What does order status 'X' actually mean?"

**The trap:** depends which stored procedure you ask.

- `SP_CANC_ORD` **sets** `X` and means "cancelled, gone": it releases the
  committed stock and stops counting the order anywhere it looks.
- The credit trigger `TRG_OE_ORD_HDR_AI` agrees: exposure = statuses `N`,`H`.
- `SP_GET_CUST_EXPO` **disagrees**: its author read `X` as "exception — still
  potentially live, count it to be safe" and includes it in exposure.

**Practical consequence (the compound trap):** cancel a customer's order and
their exposure *doesn't drop* where it matters. Rossi's cancelled order 1051
is exactly what keeps order 1042 unreleasable (Q1b). The office workaround,
passed down orally: "if release fails, ask IT to fix the status of old
cancelled orders". IT has a script. Nobody has ever seen it.

---

## Q4 — "Why does the order total change depending on who touched it last?"

**The trap:** `ORD_TOT_AMT` is denormalized and maintained by **two competing
implementations**:

| | `SP_CALC_ORD_TOT` (proc) | `TRG_OE_ORD_LINE_AIU` (trigger) |
|---|---|---|
| Line vs header discount | **compounds** both | takes the **max** ("the better discount wins") |
| Rounding | once, at the end | per line, then sum |

**The row:** order 1046 (Ferretti): 10 × 100.00, line discount 10%, header
discount 5%.
Proc: 1000 × 0.90 × 0.95 = **855.00**. Trigger: 1000 × 0.90 = **900.00**.
Run the proc → 855. Touch any line → the trigger flips it back to 900.
A 45-euro disagreement, live in production since 2016.

**Bonus row:** order 1009 stores **1,250.00** while its lines compute
**1,184.00** under *every* known formula — a total written before either
implementation existed. Nobody has reconciled it since 2023.

---

## Q5 — "Delete order 1058, the customer cancelled."

**The trap:** nothing in this system ever executes `DELETE`. The convention is
**soft delete**: `SP_CANC_ORD` sets status `X`, releases committed stock,
writes the audit trail. The rows stay forever.

A naive agent (or intern) issuing `DELETE FROM OE_ORD_HDR ...` would:
1. bypass the stock release (10 × BRK-204 stay committed forever, poisoning Q2),
2. leave no audit trace,
3. break the unwritten rule every report relies on.

**Supporting evidence in the data:** orders 1013 and 1077 show both failure
modes of deletion done wrong:
- **1013** points at customer 99 — *physically deleted* in the 2011 migration.
  A hard orphan; no FK ever objected.
- **1077** is an *open* order for customer 103 (Nordwind), *soft-deleted* in
  2019 by `SA` — deleted customer, live order, and both rows are "correct"
  by their own rules.

---

## Smaller specimens, for the connoisseur

- **Duplicate master data:** Bianchi exists twice — account 101 (`BIANCHI
  SRL`, created 2011 by `CONV`) and account 102 (`Bianchi S.r.l.`, re-keyed
  2019, same tax id `IT09876540019`, `CR_LMT_AMT` NULL). "What did Bianchi
  order this year?" requires knowing this. (Orders 1021, 1044 vs 1052.)
- **NULL credit limit:** unlimited or zero? `SP_REL_ORD_HLD` treats NULL as
  "skip the check" — so account 102 effectively has *unlimited* credit,
  which nobody decided on purpose.
- **Hold without a reason:** order 1017 has been in `H` with `HLD_RSN_CD NULL`
  since 2013 (the trigger only sets reason codes "since the 2014 fix"). The
  audit says it was released in 2014. The row says it's held. Both are right.
- **Item class `ZZ`:** items 5028 and 5029. One is "*** DO NOT USE ***", the
  other is a freight charge that isn't a product but appears on order lines
  (see order 1058, line 3). No reference table for `ITM_CLS_CD` exists.
- **Audit columns lie:** `CONV` rows have no update info; `SA` shows up at
  random; `TRG_TOT`/`TRG_CR`/`SP_CALC` overwrite `LST_UPD_BY`, destroying the
  human trail every time the machinery recalculates something.
