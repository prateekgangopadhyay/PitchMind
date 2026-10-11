# CodeGraph — optional engineering accelerator

**Status:** Repository configuration and operator guidance committed; engine installation, indexing, hosted execution and performance improvements are **not verified** by this document.

**Implementation:** [codegraph-ai/CodeGraph](https://github.com/codegraph-ai/CodeGraph) using `@astudioplus/codegraph-mcp` and its local `codegraph-server` analysis engine. CodeGraph supports C#, TypeScript/TSX, Python, shell and configuration languages used by PitchMind. It is an **advisory graph**, never an authority for acceptance, source approval, security or release decisions.

## Opt-in installation

In a developer's trusted local machine/worktree (macOS ARM64 and Linux supported):

```sh
npm install -g @astudioplus/codegraph-mcp
# Package installation downloads a native engine and verifies its published checksum.
# If engine download fails, follow the vendor's documented fetch-engine command.
```

A checked-in `/.vscode/mcp.json` config registers `codegraph-mcp` in the **all** tool profile for VS Code-compatible MCP clients (one server exposes both `get_edit_context` and graph/impact tools; the narrower `graph` profile omits edit-context). Reload the editor and explicitly enable/trust the server. Other MCP clients can configure the command `codegraph-mcp` with `--profile all --workspace <absolute-checkout-path>`; client configuration is local to each agent host. The repository does not install or execute a downloaded binary merely because it was cloned.

Use `CODEGRAPH_TELEMETRY=off` in the agent host environment if the operator wishes to disable CodeGraph's anonymous usage telemetry. Never index secrets, unapproved source materials, a `docker/.env` file, token stores, or retained production data; its built-in exclusions are defense-in-depth, not permission to ingest those materials. Only enable CodeGraph in an appropriately trusted worktree.

## Agent workflow: fast, bounded, evidence-led

1. Pick an existing EPxx-Sxx story and smallest responsible Council role group. Find the exact **head commit** and **base branch** of the proposed PR. For stacked PRs, review against their *actual parent PR*, not automatically against `main`.
2. On a fresh head/worktree, use `codegraph_get_ai_context` or `codegraph_get_edit_context` on relevant symbols; inspect `codegraph_analyze_impact`, `codegraph_get_dependency_graph` and `codegraph_find_related_tests` for high-impact changes. Query the current worktree: an index of `main` cannot prove a child PR's behavior.
3. Keep the graph output **short and advisory** in the story's existing execution evidence: changed symbols, caller/consumer impact, tests to run and unresolved uncertainties. Share that output with the affected sibling agents and the integrating Principal Engineer via story handoff/PR comments. Do not generate a new parallel backlog or mandatory review ceremony.
4. Write code and execute the actual unit, contract, PostgreSQL, frontend and local Docker integration tests required by the changed surface. CodeGraph's guesses cannot replace runtime proof or independent review.
5. Reindex or reopen the server after changing branch/worktree; repeat impact checks after restacking onto an updated parent.

### One-shot PR impact check

In a checked-out PR worktree where the *actual parent* ref is present and an ancestor of HEAD:

```sh
git fetch origin <parent-branch>
bash scripts/codegraph-pr-context.sh origin/<parent-branch>
# Or use an exact ancestor commit SHA as the argument.
```

The script resolves a locally installed engine (or an explicitly configured `CODEGRAPH_SERVER_PATH`), runs the upstream `codegraph_pr_context` tool in fast `--graph-only` mode and prints Markdown to stdout. It is **read-only** and fails if the baseline is missing or stale. It does not silently compare stacked PRs to `main`. Record a missing/failed CodeGraph analysis as *advisory unavailable*, then proceed with manual impact review and all existing tests—do not stall development.

## GitHub/Azure policy

- GitHub is source control. **Azure DevOps is the sole CI/CD orchestrator.** Do not copy CodeGraph's vendor-provided GitHub Actions workflow into this repository: `pipelines/azure-pipelines.yml` explicitly rejects `.github/workflows`.
- CodeGraph is opt-in, **not** a new required Azure DevOps gate. No third-party install or external model invocation is added to CI; adding CodeGraph to Azure DevOps later requires a separately reviewed, supply-chain-pinned, nonblocking validation change.
- Never let automated PR context output grant approval, merge a draft, mark any story accepted, publish football facts or authorize release.
- LOCAL uses Docker/locally installed tooling. DEV/STAGE/PROD use approved Azure services only. **No Azure DEV deployment or resource mutation without exact, change-specific owner approval**; protected release gates remain in force.

## Pilot / operational checks

Pilot on a single dependency chain (`#81 -> #88 -> #90 -> #89 -> #91` or the currently active parent/child equivalents). Compare time-to-context, actual review turnaround, rework after restack, targeted-test usefulness and merges into `main` against the prior baseline. Prefer the narrowest tool calls rather than dumping the entire graph. If the full MCP tool surface materially slows an agent, switch that agent to `--profile core` (edit/search context) and use the separate graph-only PR script for impact analysis, without weakening tests.

**Known limitations:** graph resolution is imperfect across dynamic dispatch, reflection, DI, SQL and runtime configurations; graph output does not prove live SignalR, PostgreSQL, Azure or Foundry behavior, real browser accessibility, football mapping or data rights.
