# Secret scanning and source SBOM

## Local and PR checks

`bash scripts/secret-scan.sh` scans the checked-out source tree with Gitleaks.
The runner uses [Gitleaks v8.29.1](https://github.com/gitleaks/gitleaks/releases/tag/v8.29.1), pinned by manifest digest
(`sha256:aa036a2f4bdfe3cc3c55fa4326308efabb4a6be498c883c864fd1d0d5585438a`). It
mounts source read-only, disables container networking, and redacts any finding
from console output. It fails when the scanner reports a finding or cannot run.
The pinned version is exercised by a clean fixture and an ephemeral, synthetic
token-shaped canary in `scripts/test_secret_scan.py`.

`bash scripts/generate-sbom.sh <source-directory> <output-spdx-json>` generates
an SPDX 2.3 source SBOM with [Syft](https://github.com/anchore/syft) `v1.54.1`, pinned by digest
(`sha256:3eb5379ba7b409c3f4069b686110527af0c47df993fa5c10d13e7cf34f49b1aa`).
The source is mounted read-only, container networking is disabled, update checks
are disabled, and output must be outside the source tree. Local validation
removes its temporary SBOM after checking the SPDX JSON structure. Azure DevOps
publishes its source SBOM as the `security-evidence` build artifact.

`scripts/validate-all.sh` runs both fixture tests and the actual repository scan
and source SBOM generation. The Azure DevOps PR validation job runs the same
checks and publishes the SBOM artifact. These are source-tree PR checks, not a
secret-history scan or an image SBOM. The release path still needs an SBOM tied
to the immutable image digest before any release approval. This change does not
enable, provision, or deploy any Azure stage.

We pin and regression-test Gitleaks `v8.29.1`. An [upstream v8.30.1 report](https://github.com/gitleaks/gitleaks/issues/2170)
used an example token containing a default stopword, so it does not establish
an engine regression. Our synthetic canary avoids those stopwords.
