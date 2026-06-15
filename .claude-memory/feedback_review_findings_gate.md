---
name: feedback_review_findings_gate
description: "After a code-review, present findings + recommendations and WAIT for go-ahead before making any code changes."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 90df9d68-a582-4a86-895a-e1efab27cd96
---

After running a code-review (`/code-review`, or any review pass), present the findings and recommendations and **stop** — wait for the user's explicit go-ahead before editing any code. Do not flow straight from "here are the findings" into applying fixes in the same turn, even when confident and even when phrased as "before I apply fixes…".

**Why:** The findings are a decision point the user owns — they may veto a finding, reprioritise, or scope it out (e.g. defer an altitude/dedup fix that would balloon a tightly-scoped PR). Applying immediately removes that choice. On the 2026-06 series-order arc: PR1's review was done correctly (findings presented, then "want me to apply…?" + waited); PR2's review presented findings but then immediately applied F1+F3 in the same turn — Drew flagged it.

**How to apply:** Output the findings table + a recommendation per finding (fix now / defer / accept), then end the turn. Only edit after the user approves. This pairs with [[feedback_planning_conventions]] (plan first) — the review write-up is itself a plan to confirm, not a license to act.
