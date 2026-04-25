---
name: cimatron-api
description: Search Cimatron SDK/API documentation — enums, interfaces, procedures, tips. Use when you need to look up any Cimatron API detail.
disable-model-invocation: true
allowed-tools: Read Grep Glob
---

# Cimatron API Documentation Lookup

You are a specialized assistant for searching and explaining Cimatron API documentation stored in local files.

## Documentation Structure

All docs live under `CimatronDocs/` (relative to repo root).

### Navigation Hierarchy

1. **`mapping.md`** — Top-level index listing all topic areas and their entry counts
2. **`topics/*.md`** — Per-topic tables with columns: Title | Local File | Source URL
3. **`assets/docs/**/*.htm`** — The actual API documentation pages (HTML format)

### Topic Files (in `topics/`)

| File | What it covers |
|------|---------------|
| `interfaces.md` | 2334 entries — all COM interfaces (IApplication, IAssemblyDocument, IMdExtrude, etc.) |
| `enums.md` | 68 entries — all enumerations (AccessMode, EntityEnumType, GeomType, etc.) |
| `procedures.md` | 98 entries — modeling procedures (MdExtrude, MdBlend, MdHole, etc.) |
| `geometry.md` | 22 entries — geometric types and services (Geom3DBody, curves, faces, etc.) |
| `filters.md` | 18 entries — entity filter interfaces |
| `object-containers.md` | 33 entries — sets, entity lists, object containers |
| `tools-commands-interaction.md` | 52 entries — tools, commands, pick tools, guide bars |
| `sketcher.md` | 16 entries — sketcher interfaces and objects |
| `topological-objects.md` | 15 entries — entities, edges, faces, bodies |
| `tips-and-faqs.md` | 16 entries — practical guides (external panes, PDM hooks, ELT files, plugins) |
| `setup.md` | 6 entries — CimSetup configuration interfaces |
| `introduction.md` | 9 entries — getting started, application object, documents |
| `attributes.md` | 4 entries — attribute factory and types |
| `release-notes.md` | 7 entries — version-specific changes |

### Path Note

Topic files reference HTM paths with a `CimatornHtm/` prefix (e.g., `CimatornHtm/assets/docs/...`). The actual files are at `assets/docs/...` under the CimatronDocs directory. Strip the `CimatornHtm/` prefix when constructing file paths to read.

## How to Handle Queries

Given the user's query: `$ARGUMENTS`

### Step 1: Identify the search domain

- **Interface/class lookup** (e.g., "IApplication", "IMdExtrude") → search `topics/interfaces.md`
- **Enum lookup** (e.g., "AccessMode", "EntityEnumType") → search `topics/enums.md`
- **Procedure/operation** (e.g., "extrude", "blend", "hole") → search `topics/procedures.md`
- **Geometry question** (e.g., "face types", "curve") → search `topics/geometry.md`
- **How-to / tips** (e.g., "external pane", "plugin") → search `topics/tips-and-faqs.md`
- **Broad/unclear** → Grep across all `topics/*.md` files, or read `mapping.md` to orient

### Step 2: Find the relevant HTM file(s)

- Grep the appropriate topic file for the search term
- Extract the Local File path from the matching row(s)
- Strip `CimatornHtm/` prefix and prepend the CimatronDocs base path

### Step 3: Read and extract documentation

- Read the HTM file(s)
- Extract meaningful content from the HTML: interface name, namespace, description, properties, methods, parameters, return types, remarks
- Ignore boilerplate HTML/CSS/JS scaffolding — focus on the `<h1>`, `<h2>`, `<p>`, `<table>` content within `<div data-rhtags="4">`

### Step 4: Return formatted results

Present findings clearly:
- **Interface/class name** and namespace
- **Description** of what it does
- **Properties and methods** with their signatures
- **Parameters and return types**
- **Related interfaces** if apparent from the docs
- **Source file path** for reference

## HTM Content Extraction Guide

The HTM files are RoboHelp-generated. Key content is inside `<div data-rhtags="4">` blocks:
- `<h1>` — Interface/enum/procedure name
- `<h2>` — Section headers (Namespace, Properties, Methods, Parameters, etc.)
- `<p class="ifaceNSpa">` — Namespace declaration
- `<table class="proptab">` — Property/method tables
- `<p>` — Descriptions and remarks

Common namespaces: `interop.CimServicesAPI`, `interop.CimatronE`

## Gotchas and Known Issues

> **IMPORTANT**: When you discover gotchas, undocumented behavior, missing docs, or corrections while answering queries, add them to this section so future lookups benefit. Format as bullet points with the interface/enum name and the issue.

- Many HTM entries in `interfaces.md` are listed as `(untitled)` — use the file path to identify the actual method/property (e.g., `iapplication_getactivedoc.htm` → `IApplication.GetActiveDoc`)
- Some interface names in the mapping have encoding artifacts (e.g., leading `Â` character) — search by the core name without special characters
- HTM files mix `.htm` and `.html` extensions — use Glob with both `*.htm` and `*.html` patterns when searching
- The `CimatornHtm/` path prefix in topic files is a legacy artifact — actual files are at `assets/docs/...` under CimatronDocs
- Enum values are typically in HTML tables within the HTM files — look for `<table>` elements after the enum name heading
