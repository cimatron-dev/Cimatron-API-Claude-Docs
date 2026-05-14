# Cimatron API — Claude Code Plugin

A Claude Code plugin that gives Claude first-class knowledge of the
Cimatron API and the workflows that surround building plugins against it.

## Install

In Claude Code:

```
/plugin marketplace add cimatron-dev/Cimatron-API-Claude-Docs
/plugin install cimatron-api@cimatron-claude
```

Then restart Claude Code. Verify with `/mcp` — you should see a
`cimatron-api` server connected with `search`, `read_file`, and
`list_index` tools.

## What you get

| Capability | Where |
|---|---|
| Search the Cimatron SDK docs | `cimatron-api-docs` agent + `cimatron-api` skill, backed by a hosted MCP server |
| Scaffold a new Cimatron 2026 API plugin | `/cimatron-api:new-cimatron-api` |
| Add a toolbar command to an existing plugin | `/cimatron-api:add-command` |
| Register / unregister a plugin in `ExternalCommands.ini` | `/cimatron-api:register-command`, `/cimatron-api:unregister-command` |
| Audit a plugin against the project standard | `api-reviewer` agent |
| Generate command icons | `icon-creator` agent |
| Add a feature-guide UI to an existing command | `feature-guide-scaffold` agent |
| One-off scaffolding outside the standard commands | `api-scaffold` agent |

The doc-search side talks to a hosted MCP server at
`https://cimatron.digitalexample.com/Cimatron-API-Claude-Docs-MCP/mcp` —
no local install of the doc corpus is required.

## License

MIT — see [`LICENSE`](./LICENSE).
