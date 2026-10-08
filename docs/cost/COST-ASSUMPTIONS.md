# Azure cost model — unverified planning assumptions

The earlier architecture discussion proposed DEV $290–1,140/month, STAGE $600–2,200/month, PROD $1,500–7,000/month and optional advanced analytics $500–4,500+/month. These are illustrative allowances, **not live quotes**, and cannot be used for purchasing decisions.

## Before provisioning
- Price each SKU in the intended Azure region using Azure Pricing Calculator.
- Confirm Azure Managed Redis minimum SKU and regional availability.
- Confirm Foundry model deployment quota and per-token pricing.
- Budget monitoring ingestion, Front Door, private endpoints, data egress and fixed-cost analytics.
- Keep optional advanced resources disabled until workload and budget approval.
