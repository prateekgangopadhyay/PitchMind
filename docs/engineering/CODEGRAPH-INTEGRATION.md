# Optional CodeGraph analysis

CodeGraph is an optional advisory tool for repository navigation and change-impact analysis. It does not replace source inspection, tests, code review, CI checks, security review, or product acceptance. It must not block implementation when unavailable.

## Available workflows

- In an environment with the CodeGraph MCP server, use `codegraph_get_edit_context`, `codegraph_analyze_impact`, and `codegraph_find_related_tests` for symbol context, callers, dependencies, and test discovery.
- For a stacked pull request, run `bash scripts/codegraph-pr-context.sh <exact-parent-ref>`. The base must resolve to a local commit and be an ancestor of `HEAD`; compare against the actual parent, not automatically against `main`.
- The script invokes `codegraph-server` when installed, or the platform binary from the globally installed `@astudioplus/codegraph-mcp` package. Set `CODEGRAPH_SERVER_PATH` to an executable binary when it is installed elsewhere.

The PR-context script is read-only and does not contact Azure, modify repository files, create pull requests, approve changes, or merge. If the engine, MCP server, or package is unavailable, continue with ordinary repository inspection and the relevant test suites.
