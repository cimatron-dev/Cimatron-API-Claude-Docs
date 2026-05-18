---
name: cimatron-api
description: Reference card for the cimatron-api MCP doc-search workflow — loaded into the main thread when Claude answers a Cimatron API question directly. Documents the search/read_file/list_index tools, ranking weights, and index schema. To DELEGATE a lookup to a subagent, use the cimatron-api-docs agent instead.
---

# Cimatron API Documentation Lookup

This skill is a **reference card** loaded into the main conversation when Claude
is answering a Cimatron API question directly — it documents the MCP-server
doc-search workflow, the parameter shapes, the ranking weights, and the index
schema so the answer can come from official Cimatron docs rather than guesswork.

If the lookup would be better handled out-of-band (to parallelize work or to
keep a long doc body out of the main context), delegate to the
`cimatron-api-docs` agent instead — it runs the same workflow described below.

## How to find a doc

Do **not** try to enumerate files. Call the `mcp__cimatron-api__search` tool —
it returns ranked entries with the metadata that caused each match, so you
can pick the right doc before reading it.

`mcp__cimatron-api__search` parameters:

| Param      | Notes                                                                       |
|------------|-----------------------------------------------------------------------------|
| `terms`    | Array of search terms. Multi-term queries are AND-combined and ranked.     |
| `service`  | Usually `Cimatron`. Optional.                                              |
| `method`   | `Method`, `Property`, `Enum`, or `Procedure`. Optional.                    |
| `category` | `interfaces`, `enums`, `procedures`, `geometry`, `filters`, `tools`, `sketcher`, `tips`, `setup`, `attributes`, `release-notes`. Optional. |
| `limit`    | Default 10.                                                                |

Ranking weights: `topics > endpoint > path == service > description > content`.
If metadata search yields fewer than `limit` results, the server falls back to a
content grep. One call to `search` should replace any urge to list directories
or grep across the tree by hand.

After getting hits, call `mcp__cimatron-api__read_file` with the `path` from a
hit to read the doc body (200KB cap, server-side path-safety enforced).

For broader browsing, `mcp__cimatron-api__list_index` returns paginated entry
summaries (`offset`, `limit` — default 100).

## How the index is shaped for Cimatron

One entry per Markdown doc:

| Field         | Cimatron meaning                                                                  |
|---------------|-----------------------------------------------------------------------------------|
| `path`        | Relative MD path under `api-docs/` on the server                                 |
| `description` | One-line summary                                                                  |
| `topics`      | Free-form tags + auto-detected (parameter names, enum values, etc.)              |
| `service`     | Always `Cimatron` (left in for cross-product reuse)                              |
| `endpoint`    | Interface/enum/procedure name (e.g. `IApplication`, `MdExtrude`)                 |
| `method`      | COM call kind when relevant: `Property`, `Method`, `Enum`, `Procedure`           |
| `category`    | One of the curated areas listed in the `category` row above                      |
| `api_version` | Cimatron version the entry applies to (e.g. `2024`)                              |

## Workflow for a user question

1. Pick 1–3 keywords the user is asking about (interface name, operation,
   parameter, enum value).
2. Call `mcp__cimatron-api__search` with those `terms` and whichever filter
   narrows the result the most (`category: "interfaces"` for class-style
   lookups, `category: "enums"` for enum names, etc.).
3. Call `mcp__cimatron-api__read_file` only on the top-ranked path(s). Do not
   pre-load the whole index — the server already consulted it on your behalf.
4. Answer using:
   - Interface/enum/procedure name and the `endpoint` field
   - Description
   - Properties / methods / parameters from the doc body
   - The doc path so the user can locate it on the server

## When the index has no hit

If `mcp__cimatron-api__search` returns zero results for a reasonable query,
tell the user clearly that the topic isn't covered in the current index and
suggest they ask a maintainer to add it (via the `cimatron-api-admin` plugin
or the repo's CLI). Do not invent answers.
