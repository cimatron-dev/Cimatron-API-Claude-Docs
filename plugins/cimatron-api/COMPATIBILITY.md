# Compatibility

The `cimatron-claude` marketplace ships two Claude Code plugins plus a hosted
MCP server. Each component is versioned independently. This page is the
best-effort compatibility matrix between them; the canonical source of truth
is always the version reported by the installed plugin (`plugin.json`) and the
`/version` endpoint of the deployed server.

Repository: https://github.com/Cimatron-Post/Cimatron-API-Claude-Docs

## Current versions

| Component            | Version | Last bump   | Source of truth                                                |
| -------------------- | ------- | ----------- | -------------------------------------------------------------- |
| `cimatron-api`       | 2.0.1   | 2026-05-18  | `plugins/cimatron-api/.claude-plugin/plugin.json`              |
| `cimatron-api-admin` | 1.0.1   | 2026-05-18  | `plugins/cimatron-api-admin/.claude-plugin/plugin.json`        |
| MCP server           | 1.1.7   | 2026-05-18  | `api-docs/package.json` (deployed at the hosted server URL)    |

Hosted MCP server URL:
`https://cimatron.digitalexample.com/Cimatron-API-Claude-Docs-MCP/mcp`

## Compatibility matrix

The plugins talk to the MCP server, not to each other. They can be upgraded
independently as long as the server exposes every MCP tool a given plugin
version requires.

| `cimatron-api` | `cimatron-api-admin` | MCP server | Status     |
| -------------- | -------------------- | ---------- | ---------- |
| 2.x            | 1.x                  | 1.1.x      | Supported  |
| 2.x            | (not installed)      | 1.1.x      | Supported  |
| (not installed)| 1.x                  | 1.1.x      | Supported  |

Today the MCP server is unversioned at the tool-surface level: there is one
production deployment and both plugins target whatever tools it currently
exposes. The version in `api-docs/package.json` tracks server build/deploy
revisions, not a tool-surface contract.

## Breaking-change policy

To keep upgrades painless across the three independently-shipped components:

- **MCP server tools are append-only.** New tools can be added at any time;
  existing tool names and required parameter names must not be removed or
  renamed in a server release that the plugins still target.
- **Renaming or removing a tool requires a major plugin bump.** If a server
  release drops or renames an existing tool, every plugin that calls it must
  ship a new major version (`cimatron-api` 2.x to 3.x, `cimatron-api-admin`
  1.x to 2.x) pinned to the new server contract. Document the break here.
- **Adding optional parameters is non-breaking** on either side. Plugins
  should treat unknown response fields as forward-compatible.
- **Server deploys roll forward.** There is one production hosted server;
  rolling back the plugins is supported but rolling back the server is not.
  This is why removals are gated behind a major plugin bump.

## Diagnosing a version-skew issue

If a slash command from either plugin fails with a "tool not found" or "unknown
parameter" error, the most likely cause is that the deployed MCP server is
older than the plugin expects (or, less commonly, newer with a removed tool).

1. **Confirm the server is connected.** Run `/mcp` in Claude Code and verify
   the `cimatron-api` (or `cimatron-deploy`) server shows as connected. If it
   shows disconnected or errored, the plugin can't reach the hosted server at
   all — check network/auth before chasing a version mismatch.
2. **List the tools the server advertises.** `/mcp` shows the tool list for
   each connected server. Compare it against the tool the failing slash
   command calls (each command in `plugins/*/commands/*.md` names the MCP
   tool it expects).
3. **If the expected tool is missing, the server is too old.** Redeploy the
   server from `main` (see the `/deploy` skill) so it picks up the latest
   `api-docs/` build, then retry.
4. **If the expected tool is present but rejects parameters, the plugin is
   too old.** Re-install the plugin from the marketplace to pull the latest
   version, then retry.
5. **Confirm versions match this matrix.** Read the installed plugin's
   `plugin.json` and fetch the server's `/version` endpoint; both should
   line up with a row in the "Compatibility matrix" above.
