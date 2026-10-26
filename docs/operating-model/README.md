# Engineering Council — operating model

This is the canonical collaboration contract for 28 PitchMind engineering-role skills. Read [visual operating model](WORKFLOW-DIAGRAMS.md), [E2E operating model](ENGINEERING-OPERATING-MODEL.md), [delivery artifact templates](DELIVERY-ARTIFACT-TEMPLATES.md), [architecture review](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md) and [architecture/ADRs](../architecture/README.md) before assigning implementation work.

The 28 portable GitHub Copilot Agent Skills live in [../../.github/skills](../../.github/skills/README.md). Each is a role-specific capability with triggers, required references, executable review questions, deliverables, stop conditions and handoffs. Skills are on-demand; the always-applicable minimal policy is in [../../.github/copilot-instructions.md](../../.github/copilot-instructions.md).

## Starting an epic or story

1. Quote the exact EPxx-Sxx acceptance criteria from the authoritative hackathon backlog; link the execution register and current code.
2. Ask Product Owner to choose one story DRI, desired user result, P0 boundaries and dependencies.
3. Ask Principal Architect to run the architecture delta/risk route; select only necessary specialist skills.
4. Staff + story DRI produce HLD/LLD, contracts, tests and a partitioned implementation plan.
5. Implement locally on a feature branch; independent reviewers inspect code/tests and collect *observed* evidence.
6. Create a reviewable PR. Merge/release only after the applicable gates and named decision authorities.
7. **Ask the owner before every DEV story deployment.** No deployment/approval inference from earlier prototype activity.

Treat architecture documents as target intent, integration smoke as environment-scoped proof and clean pipeline output as build-scoped proof.
## Executable routing and validation (added)
- [28-role machine-readable skills registry](COUNCIL-SKILL-REGISTRY.json)
- [All 53 Hackathon story routes, owners by skill and dependency DAG](STORY-EXECUTION-ROUTING.json)
- [Readable per-story execution roadmap](ALL-STORIES-EXECUTION-ROADMAP.md)
- [Current-vs-target C4](../architecture/README.md)

Run `node scripts/validate_operating_model.mjs` at repository root (or `node scripts/validate_operating_model.mjs --self-test`). The Node validation checks catalog/frontmatter, 53-story hackathon coverage, no missing workpackets, DAG cycles, current-vs-target architecture and two manual DEV gates. It is static validation: **not** .NET/React integration, Azure deployment or rights/football certification. Azure DevOps validates this in the frontend job after installing Node.

**Verification status:** [Current static skills, 53-story routing and deployment-control audit](STATIC-VERIFICATION-2026-10-08.md). This does not certify CI, the full feed, or DEV runtime.

## Cross-cutting UI specialists
The 28 accountable Council role skills remain unchanged in number. Five cross-cutting design specialists (33 discoverable skill directories total) are available under `.github/`: Taste, Impeccable, Emil design engineering, Vercel Web Design Guidelines and Awesome Design audit. For materially changed UI, use them in that order as documented in the [frontend review protocol](../design/FRONTEND-DESIGN-DELIVERY.md). These are design-time review resources; the Product Owner remains the story acceptance owner, and their presence does not mean any visual story or UX audit has passed. See the [design registry](../design/DESIGN-SKILL-REGISTRY.json), [premium brand system](../design/PITCHMIND-DESIGN-SYSTEM.md) and [vendor/license notes](../design/VENDOR-SOURCES.md).

## Direct design-skill and audit URLs

Use these links to open each specialist's **PitchMind design instructions** and upstream source directly, instead of relying on the registry alone. All five skills are design-time review resources, not story acceptance owners or proof that a particular UI has passed an audit. Follow Taste → Impeccable → Emil → Vercel → Awesome for UI work; Impeccable remains a local advisory adaptation, with no upstream CLI, binaries or hooks installed.

| Specialist | PitchMind audit instructions | Upstream audit/source URL |
| --- | --- | --- |
| **Taste** | [Taste frontend skill](../../.github/skills/design-taste-frontend/SKILL.md) | [Pinned Taste Skill source](https://github.com/girishlade111/taste-skill/blob/8399cf57f6faad8c943700cce0dd9873ff2ae296/skills/taste-skill/SKILL.md) |
| **Impeccable** | [Impeccable local advisory skill](../../.github/skills/impeccable/SKILL.md) | [Impeccable project](https://github.com/pbakaus/impeccable) (upstream CLI/binaries/hooks not installed) |
| **Emil design engineering** | [Emil design engineering skill](../../.github/skills/emil-design-eng/SKILL.md) | [Pinned Emil skill source](https://github.com/emilkowalski/skills/blob/2d228630ba1446365f614a108cb8252f1feda202/skills/emil-design-eng/SKILL.md) |
| **Vercel Web Design Guidelines** | [Web Design Guidelines audit skill](../../.github/skills/web-design-guidelines/SKILL.md) | [Current Web Interface Guidelines (audit rules)](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md) · [Pinned Vercel skill source](https://github.com/vercel-labs/agent-skills/blob/063bee94c3f4df8453406c830b0a7df0f2860278/skills/web-design-guidelines/SKILL.md) |
| **Awesome Design audit** | [Awesome Design audit skill](../../.github/skills/awesome-design-audit/SKILL.md) | [Pinned Awesome Design rubric collection](https://github.com/frankxai/awesome-design-agent-skills/tree/9a6b8fe9f0ccf8b851863485b76143c54f33df7e/rubrics) |

The **Awesome Design audit rubrics** are also available directly in this repository: [anti-slop rubric](../design/vendor/awesome-anti-slop-rubric.md), [AI UI quality rubric](../design/vendor/awesome-ai-ui-quality-rubric.md), and [premium generation gate](../design/vendor/awesome-premium-generation-gate.md).

For audit records and execution, use the [source-only baseline UI audit](../design/BASELINE-UI-AUDIT.md) and [per-screen frontend audit workflow](../design/FRONTEND-DESIGN-DELIVERY.md). The baseline audit is explicitly partial; final visual/accessibility acceptance requires rendered-screen evidence, relevant tests and independent review. [Source attribution and licenses](../design/VENDOR-SOURCES.md) document the pinned versions.

## Accelerated delivery and execution directive

The active [AI execution directive](AI-EXECUTION-DIRECTIVE.md) governs hackathon story delivery while prioritizing parallel P0 development **and** dependency-safe incremental integration into `main`. Cross-agent context sharing and strict automated testing ensure high code quality. All code must pass independent review, unit/integration test suites, and Product Owner acceptance before release.
