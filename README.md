# Cimatron API — Claude Code Plugin

A Claude Code plugin that gives Claude first-class knowledge of the
Cimatron API and the workflows that surround building plugins against it.

## Prerequisites

This plugin is for **Windows 10/11**. Install these once before you start —
the plugin's `/cimatron-api:setup-env` check will re-verify them for you and
offer to install anything missing.

| What | Why | How to install |
|---|---|---|
| **Git** | `/plugin marketplace add` clones this repo over git | `winget install -e --id Git.Git` &nbsp;·&nbsp; or [git-scm.com/download/win](https://git-scm.com/download/win) |
| **Claude Code** | The host for this plugin | `winget install -e --id Anthropic.ClaudeCode` |
| **VS Code** | F5 build → deploy → debug into Cimatron | `winget install -e --id Microsoft.VisualStudioCode --scope user` &nbsp;·&nbsp; or [code.visualstudio.com](https://code.visualstudio.com/download) |
| **C# Dev Kit** (VS Code extension) | Managed debugger that attaches to Cimatron (.NET Framework 4.8 / x64) | `code --install-extension ms-dotnettools.csdevkit` |
| **.NET Framework 4.8 Developer Pack** | Plugins compile against `net48` (the runtime alone is not enough) | `winget install -e --id Microsoft.DotNet.Framework.DeveloperPack_4` &nbsp;·&nbsp; or [dotnet.microsoft.com](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) |
| **Cimatron 2024.0 or newer** (2026 recommended) | The plugin DLL loads into this | Use your Cimatron distributor's installer — not auto-installable |

After installing Git/VS Code via `winget`, **open a fresh terminal** so the
new `git` / `code` commands land on your `PATH` before continuing.

> **Already set up?** If you have Claude Code, Git, VS Code + C# Dev Kit, and
> the .NET 4.8 Developer Pack, skip straight to **Install** below.

## Install

In Claude Code:

```
/plugin marketplace add cimatron-dev/Cimatron-API-Claude-Docs
/plugin install cimatron-api@cimatron-claude
```

Then restart Claude Code. Verify with `/mcp` — you should see a
`cimatron-api` server connected with `search`, `read_file`, and
`list_index` tools.

Once installed, sanity-check the rest of your toolchain from inside Claude
Code with:

```
/cimatron-api:setup-env
```

It reports a pass/fail table for Git, VS Code, the C# Dev Kit, the .NET 4.8
Developer Pack, and your installed Cimatron versions — and offers to install
anything that's missing.

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
