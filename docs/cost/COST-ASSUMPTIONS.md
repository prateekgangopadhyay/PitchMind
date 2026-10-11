# Azure cost model — unverified planning assumptions

The earlier architecture discussion proposed DEV $290–1,140/month, STAGE $600–2,200/month, PROD $1,500–7,000/month and optional advanced analytics $500–4,500+/month. These are illustrative allowances, **not live quotes**, and cannot be used for purchasing decisions.

## Before provisioning
- Price each SKU in the intended Azure region using Azure Pricing Calculator.
- Confirm Azure Managed Redis minimum SKU and regional availability.
- Confirm Foundry model deployment quota and per-token pricing.
- Budget monitoring ingestion, Front Door, private endpoints, data egress and fixed-cost analytics.
- Keep optional advanced resources disabled until workload and budget approval.

## EP08-S05 local code limits (2026-10-10)
- The current Application policy caps each request by serialized context/question bytes, requested output tokens and provider-reported input/output tokens; defaults are 40,000 request bytes, 16,384 input tokens, 512 output tokens, 4 concurrent calls, and 12 provider attempts per sliding 60-second window per process. Before provider I/O, the adapter bounds serialized UTF-8 request bytes by the configured input-token ceiling as a conservative tokenizer-independent proxy; reported provider token counts are validated after the call.
- A request attempt consumes call budget even when the provider fails. No automatic retry occurs. A circuit breaker, timeout, and bounded cache of verified results reduce repeat calls; cache hits are exact-scope/revision/time/audience/question keyed and expire after 30 seconds.
- These are code-level per-process limits, not an aggregate multi-replica allowance or a dollar budget. Current Foundry model/SKU prices, Azure region, exporter/dashboard, and a shared budget ledger are unverified. Do not use them as a purchase estimate or as DEV deployment authorization.
