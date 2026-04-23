---
name: Retro — infra security arc
description: 5-PR sequence taking SQL/KV/AI from public-with-firewall to Private-Endpoint architecture
type: project
originSessionId: 8c3462ff-d4fd-4094-b532-e213e55887db
---
**Shipped** — VNet (10.0.0.0/16) + App Service VNet integration; Key Vault with RBAC; SQL PE; Azure OpenAI in eastus2 with peered secondary VNet + PE; docs rewrite. Every Azure resource the App Service touches is now reachable only through Private Endpoints.

**Surprise** — Microsoft Foundry / Claude on Azure was the planned backbone for the AI provider story, but Drew's subscription is `Sponsored_2016-01-01` and Microsoft hard-blocks Claude on Foundry for sponsored subs. We hit `DeploymentModelNotSupported` mid-deploy. Whole Foundry side of the architecture got dropped from PR 4 and parked in TODO.md for "if you ever move to EA/MCA-E". The eastus2 VNet + peering survived because Azure OpenAI still lives there (australiaeast retires gpt-4o June 2026).

**Lesson** — verify subscription eligibility BEFORE spending an afternoon on Bicep templates for a service you can't actually deploy. The provider docs mention it in passing; we missed it. Also: cross-region resources via VNet peering is a real pattern in Azure, not an exotic one — once you accept the second VNet, the rest is straightforward.

**Quotable** — the moment of "wait, the model deployment error mentions Anthropic compliance, but actually it's just the subscription type". Two layers of red herring before the real answer surfaced.
