# PitchMind infrastructure (Bicep)

M03 adds a DEV-only network and RBAC Key Vault baseline and tightens storage security. It does **not** deploy resources automatically.

Run `az bicep build --file infra/main.bicep`, then `az bicep build-params --file infra/environments/dev.bicepparam`. Use `./scripts/azure-dev-whatif.sh --resource-group <existing-dev-resource-group>` to preview changes. See [M03 runbook](../docs/engineering/M03-AZURE-DEV.md) for security exceptions, environment isolation, acceptance gates, and deployment prerequisites.
