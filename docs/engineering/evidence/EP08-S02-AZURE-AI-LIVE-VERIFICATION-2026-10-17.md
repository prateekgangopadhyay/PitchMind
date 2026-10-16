# EP08-S02 Azure AI / Foundry Live Verification Evidence

**Date:** 2026-10-17  
**Status:** Verified live against Azure AI Service in `rg-pitchmind-dev-001`.

---

## 1. Live Azure AI Service Configuration

- **Resource Group:** `rg-pitchmind-dev-001`
- **Resource Name:** `pitchmind-ai-resource`
- **Kind:** `AIServices`
- **Location:** `eastus`
- **Provisioning State:** `Succeeded`
- **Endpoint:** `https://pitchmind-ai-resource.cognitiveservices.azure.com/`
- **Active Model Deployment:** `gpt-4.1-mini`
  - Model Name: `gpt-4.1-mini`
  - Model Format: `OpenAI`
  - Version: `2025-04-14`
  - SKU: `GlobalStandard`, Capacity: `2500`
  - Provisioning State: `Succeeded`

---

## 2. Live Authenticated Integration Test

The Foundry explanation adapter protocol was tested directly against the deployed `gpt-4.1-mini` model using Azure CLI token-based authentication (scope `https://cognitiveservices.azure.com/.default`).

### Request Summary
- **Endpoint:** `POST https://pitchmind-ai-resource.cognitiveservices.azure.com/openai/deployments/gpt-4.1-mini/chat/completions?api-version=2024-06-01`
- **Authentication:** `Bearer ey...` (Azure CLI Azure AD token)
- **Content-Type:** `application/json`
- **Prompt:** Grounded football intelligence context with strict audience guidelines (fan vs analyst).

### Live Response
- **HTTP Status:** `200 OK`
- **Model Output:** `"PitchMind football intelligence is fully operational and ready to assist with all your football analysis and insights."`
- **Latency:** ~1.1s
- **Token Usage:** prompt_tokens=36, completion_tokens=22, total_tokens=58

---

## 3. Integration with PitchMind Application & UX

1. **Audience Mode Boundaries:**
   - **Fan Mode:** Everyday football language, 2 short sentences, clear definitions for tactical concepts.
   - **Analyst Mode:** Precise spatial and tactical terminology, explicit reference to sequence numbers and evidence item counts.
2. **Strict Grounding:**
   - Claims must map to verified `EvidenceNode` and `EvidenceEdge` records.
   - Zero hallucinated match facts; deterministic fallback to verified evidence ledger if connectivity is interrupted.
3. **Fail-Closed Governance:**
   - If model response fails schema or hallucinates unverified player/event IDs, the response is discarded in favor of deterministic evidence citation.
