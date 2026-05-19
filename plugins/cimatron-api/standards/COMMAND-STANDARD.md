# The Cimatron command standard

This document is the canonical source of truth for **the Cimatron command standard** — the set of rules every Cimatron API plugin command (Plugin pattern or COM pattern) is required to satisfy. In one line: every command starts its `MenuPath` with `"API"`, keeps every user-visible string to roughly 20 characters or fewer (except `Description`), and bookends its entry-point body with a `Logger.LogInfo` start, a `try { … } catch (Exception ex) { Logger.LogException(ex, …); } finally { Logger.LogInfo(…); }` block.

The rules below currently also live, nearly verbatim, in four agent files (see [Source files](#source-files)). A migration plan exists (see that section); until it ships, treat this file as authoritative when the wording diverges.

## Rules

### 1. `MenuPath` first segment must be `"API"`

**Rule.** The first `\n`-separated segment of a command's `MenuPath` (Plugin pattern) or `GetMenuPath()` return (COM pattern) must be the literal string `"API"`. Format: `"API" + "\n" + "..short group.."`. Real values: `"API\nTools"`, `"API\nMold"`, `"API\nNC"`. The scaffolder's menu-path placeholder token must be resolved before shipping.

**Why.** The `"API"` root is the contract Cimatron's UI uses to surface third-party API plugins together under one top-level menu. Any other first segment leaves the command in an inconsistent place; an unresolved menu-path placeholder will fail to load at all (api-scaffold.md:43, api-reviewer.md:54).

**Good — Plugin pattern**

```csharp
var cmd = new ApiCommand();
cmd.MenuPath = "API" + "\n" + "Mold";
```

**Good — COM pattern**

```csharp
public string GetMenuPath() => "API" + "\n" + "Mold";
```

**Bad**

```csharp
cmd.MenuPath = "Tools\nMyPlugin";       // wrong root segment
cmd.MenuPath = "..MENU PATH placeholder..";  // scaffolder placeholder not resolved
```

### 2. Every user-visible string ≤ ~20 characters

**Rule.** Each of the following must be ≤ ~20 characters; `Description` (Plugin) / `GetDescription()` (COM) is the only string that may be a full sentence.

- **Plugin pattern:** `Name`, each `\n`-separated segment of `MenuPath`, `ToolbarName`, `Caption`, `ToolTip`.
- **COM pattern:** `GetCommandName`, each `\n`-separated segment of `GetMenuPath`, `GetCategoryName`, `GetToolbarName`, `GetPrompt`, `GetTooltip`.

Prefer crisp single words (`"MoldTest"`) over phrases. Strings materially longer than 20 (≳25 chars) are flagged.

**Why.** Cimatron's UI truncates or fully hides long values — the command appears clipped in the menu or missing from the toolbar entirely (api-scaffold.md:44, api-reviewer.md:55-59).

**Good**

```csharp
cmd.Name        = "MoldTest";
cmd.MenuPath    = "API\nMold";
cmd.ToolbarName = "Mold";
cmd.Caption     = "MoldTest";
cmd.ToolTip     = "Run mold checks";
cmd.Description = "Runs the standard set of mold-quality checks on the active part.";
```

**Bad**

```csharp
cmd.Name        = "Run Mold Quality Check";          // > 20 chars, will truncate
cmd.MenuPath    = "API\nMold Quality Checks";        // segment > 20 chars
cmd.ToolTip     = "Click here to run the mold ...";  // > 20 chars
```

### 3. Logging bookend on every entry point

**Rule.** Every `ICimWpfCommand.OnCommand()`, `ICimCommand.Execute()`, and DmHook callback must use the bookend pattern:

```csharp
Logger.LogInfo("<Command> started");
try { /* body */ }
catch (Exception ex) { Logger.LogException(ex, "<Command> failed"); }
finally { Logger.LogInfo("<Command> finished"); }
```

Use the project's own `Logger` (in the `<Namespace>.Helpers` namespace from the template). The same bookend applies to Feature Guide event entry points (`OnApply`, `OnEvent`, stage `OnPressed` / `OnReleased`) and to SP figure event handlers.

**Why.** The bookend is how production users trace customer-reported issues back to a specific run. Without it, "the command did nothing" reports have nothing to correlate against. `LogException(ex, …)` (rather than `LogError(ex.Message)`) preserves the stack trace and inner exceptions (api-scaffold.md:45-52, api-reviewer.md:84-86, feature-guide-scaffold.md:113-141, sp-figure-builder.md:97-130).

**Good**

```csharp
public bool OnCommand()
{
    Logger.LogInfo("MoldTest started");
    try
    {
        // body
        return true;
    }
    catch (Exception ex)
    {
        Logger.LogException(ex, "MoldTest failed");
        return false;
    }
    finally
    {
        Logger.LogInfo("MoldTest finished");
    }
}
```

**Bad**

```csharp
public bool OnCommand()
{
    try { /* body */ return true; }
    catch (Exception ex)
    {
        Logger.LogError(ex.Message);    // loses stack + inner exceptions
        return false;
    }
    // missing bookend LogInfo on entry/exit
}
```

### 4. No `Console.WriteLine` / `Debug.WriteLine` / `Trace.WriteLine`

**Rule.** Don't call `Console.WriteLine`, `Debug.WriteLine`, `Trace.WriteLine`, or hand-roll a log file. Route all diagnostic output through the project's `Logger` helper.

**Why.** Cimatron plugins run inside a host process with no attached console, no debug listener, and no guarantee of a writable working directory. Output to `Console`/`Debug`/`Trace` is silently dropped in production, so the customer-reportable trace data the standard relies on never reaches disk (api-scaffold.md:52, api-reviewer.md:86).

**Good**

```csharp
Logger.LogInfo("Loaded " + parts.Count + " parts");
```

**Bad**

```csharp
Console.WriteLine("Loaded " + parts.Count + " parts");
Debug.WriteLine("…");
Trace.WriteLine("…");
```

### 5. Icon path via `Path.Combine(GetExecutionPath(), …)`

**Rule.** When wiring `IconSource` in the Plugin pattern, construct the path with `Path.Combine(GetExecutionPath(), "<icon>.ico")`. Don't hardcode an absolute path.

**Why.** The plugin ships into `$(CimatronRootPath)` at install time, which differs per machine and per Cimatron version. A hardcoded path works on the author's machine and silently breaks on every other dev box, CI runner, and customer install (api-scaffold.md:35, api-reviewer.md:67).

**Good**

```csharp
cmd.IconSource = new CimWpfContracts.WpfImageIdentifier(
    Path.Combine(GetExecutionPath(), "icon.ico"),
    CimWpfContracts.ImageSize.Small);
```

**Bad**

```csharp
cmd.IconSource = new CimWpfContracts.WpfImageIdentifier(
    @"C:\cimatron\Program\icon.ico",
    CimWpfContracts.ImageSize.Small);
```

### 6. csproj must target `net48`, `x64`, `$(CimatronRootPath)`

**Rule.** The plugin csproj must declare:

- `<TargetFramework>net48</TargetFramework>` (SDK-style) or `<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>` (legacy).
- `x64` somewhere in `<Platform>` / `<Platforms>` / `<PlatformTarget>`.
- `<OutputPath>$(CimatronRootPath)</OutputPath>` so Debug drops directly into the live Cimatron install.
- `<OutputType>Library</OutputType>` (or `Exe` only when intentional).
- Interop references via `<HintPath>$(CimatronRootPath)interop.<X>.dll</HintPath>` + `<EmbedInteropTypes>True</EmbedInteropTypes>`.

**Why.** Cimatron loads only `net48` / `x64` DLLs into its process. Anything else won't load. Hardcoded absolute `OutputPath` values break on every other dev box and CI runner (api-scaffold.md:31, api-reviewer.md:23, api-reviewer.md:90-95).

**Good**

```xml
<PropertyGroup>
  <TargetFramework>net48</TargetFramework>
  <Platforms>x64</Platforms>
  <PlatformTarget>x64</PlatformTarget>
  <OutputType>Library</OutputType>
  <OutputPath>$(CimatronRootPath)</OutputPath>
</PropertyGroup>

<ItemGroup>
  <Reference Include="interop.CimBaseAPI">
    <HintPath>$(CimatronRootPath)interop.CimBaseAPI.dll</HintPath>
    <EmbedInteropTypes>True</EmbedInteropTypes>
  </Reference>
</ItemGroup>
```

**Bad**

```xml
<TargetFramework>net6.0</TargetFramework>                     <!-- won't load -->
<PlatformTarget>AnyCPU</PlatformTarget>                       <!-- not x64 -->
<OutputPath>C:\cimatron\Program\</OutputPath>                 <!-- hardcoded path -->
<HintPath>C:\cimatron\14.0\interop.CimBaseAPI.dll</HintPath>  <!-- version-pinned -->
```

### 7. No leftover scaffolder placeholders

**Rule.** Any scaffolder placeholder token left in shipped source is a hard fail. The categories the scaffolder emits are: the menu-path token, the project-guid token, the short-name / short-group tokens, the command-name token, and the root-namespace token. All must be replaced before the plugin loads. For the exact token spellings used by the current template, see `plugins/cimatron-api/template/` and the placeholder list in `api-reviewer.md:60`.

**Why.** An unresolved menu-path token in particular causes the command to fail loading; the other placeholders compile but ship a broken-looking UI and an obviously unfinished plugin (api-reviewer.md:60).

**Good** — every placeholder has been resolved to a real value:

```csharp
cmd.MenuPath = "API\nMold";
cmd.Name     = "MoldTest";
```

**Bad** — scaffolder tokens still present (shown here with dots in place of the literal token characters to avoid this doc tripping its own placeholder check):

```csharp
cmd.MenuPath = "..menu path token..";
cmd.Name     = "..command name token..";
```

### 8. No new NuGet packages

**Rule.** Don't add NuGet packages beyond what the template already references. The Cimatron interop assemblies come from `$(CimatronRootPath)` via `<Reference>` + `<HintPath>` + `<EmbedInteropTypes>True</EmbedInteropTypes>`.

**Why.** Cimatron's plugin loader expects a specific set of resolvable references; arbitrary NuGet packages drag in transitive dependencies that aren't on the Cimatron probing path and silently fail to bind at runtime. A new package is a smell — almost everything plugins need is already available via the interop refs (api-scaffold.md:227).

## Severity ladder

When grading a project against this standard, use these buckets (mirrors `api-reviewer.md:33-37`):

| Severity | Meaning | Examples from the rules above |
|---|---|---|
| **Critical** | Will fail to load in Cimatron, will silently truncate UI strings into invisibility, will lose customer trace data. Must fix before shipping. | Unresolved menu-path scaffolder token (rule 1, rule 7); `TargetFramework` not `net48` (rule 6); `PlatformTarget` not `x64` (rule 6); hardcoded `OutputPath` (rule 6); icon filename mismatch between `IconSource` and the csproj `<Content>` entry. |
| **Should fix** | Works today but diverges from the standard. Will surprise the next maintainer or break a future migration. | Menu path with a non-`"API"` first segment (rule 1); user-visible string materially >20 chars (rule 2); missing or partial logging bookend (rule 3); `Console.WriteLine`/`Debug.WriteLine`/`Trace.WriteLine` in plugin code (rule 4); `LogError(ex.Message)` instead of `LogException(ex, …)` (rule 3); hardcoded icon path (rule 5); version-pinned interop `<HintPath>` (rule 6). |
| **Nice to have** | Style / convention drift. Not blocking. | `<ApiName>Plugin.cs` filename not matching `<AssemblyName>`; minor `OnCommandUI` deviations from the template default. |

Reviewers grade each finding against this ladder; the `api-reviewer` agent's report groups its bullets under these three headings.

## Pattern variants

Most rules apply identically in both patterns. Two have surface-level differences worth calling out explicitly.

### `MenuPath` (rule 1) and visible strings (rule 2)

- **Plugin pattern** (`ICimApiCommandPlugin` + `AppendCommand` / `AppendCommands`): the rules apply to the `ApiCommand` field assignments — `MenuPath`, `Name`, `ToolbarName`, `Caption`, `ToolTip`. `Description` is the long-string exception. Inspected in `<ApiName>Plugin.cs`.
- **COM pattern** (`ICimCommand` + `ICreateCommand`): the rules apply to the `Get…` method returns — `GetMenuPath`, `GetCommandName`, `GetCategoryName`, `GetToolbarName`, `GetPrompt`, `GetTooltip`. `GetDescription` is the long-string exception. Inspected in the COM command class.

Both patterns share the `"API\n…"` requirement and the ≤20-char cap; only the surface where the strings live differs.

### Logging bookend (rule 3)

- **Plugin pattern:** the bookend wraps the body of `ICimWpfCommand.OnCommand()`.
- **COM pattern:** the bookend wraps the body of `ICimCommand.Execute()`.
- **Feature Guide:** the bookend wraps `OnApply`, `OnEvent`, stage `OnPressed`, and stage `OnReleased`. `OnApply` additionally calls `mFG.DeActivate()` in its `finally` block alongside `Logger.LogInfo(…)`.
- **SP figure event handlers:** the events class's `OnEvent` body is wrapped in try/catch with `Logger.LogException(ex, …)`; the entry-point `LogInfo` start/finish lines are optional for `OnEvent` specifically (events fire continuously and would flood the log), but the try/catch is mandatory.

The shape of the bookend is identical; only the *method* hosting it varies by pattern.

## Source files

These four agents currently embed the rules above nearly verbatim. They are the historical source:

- `plugins/cimatron-api/agents/api-scaffold.md` — "Cimatron command standard (non-negotiable)" section (≈ lines 39-54) and the "Things to avoid" entry on the logging bookend.
- `plugins/cimatron-api/agents/api-reviewer.md` — "Severity buckets" table (≈ lines 33-37), "Command-string conventions" checks (≈ lines 45-60), and "Logging conventions" checks (≈ lines 80-86).
- `plugins/cimatron-api/agents/feature-guide-scaffold.md` — `OnApply` / `OnEvent` try-catch guidance (≈ lines 112-141) and the verification checklist's logging entry (≈ line 335).
- `plugins/cimatron-api/agents/sp-figure-builder.md` — `OnEvent` try-catch shape (≈ lines 90-131) and `OnPressed` / `OnReleased` cleanup patterns (≈ lines 150-191).

This file is the source of truth. When phrasing here diverges from the agent files, this file wins.

### Migration path

Investigation (2026-05-18) confirmed there is no agent-body syntax that auto-loads an external file at subagent-spawn time — neither `@path` nor markdown links nor bare relative paths are resolved. Two viable migration shapes:

1. **Skill-based (preferred, spawn-time inclusion).** Convert this document into a Claude Code skill (e.g. `plugins/cimatron-api/skills/command-standard/SKILL.md`) and reference it from each agent's frontmatter via `skills:`. Per the Claude Code subagent docs, the full skill content is injected into the subagent's context at spawn time, with no runtime Read tool call required. This is the cleanest dedup.
2. **Runtime-read (works, but costs tokens).** Instruct each agent in its body to read this file with its Read tool before answering. Costs one tool call + the file's tokens per invocation, which adds up across the four affected agents.

Either approach is a separate PR; the surface area (which agent prompt fragments map to which standard rule) is documented above to make that PR a mechanical translation.
