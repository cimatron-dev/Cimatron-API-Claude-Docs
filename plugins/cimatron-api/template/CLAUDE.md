# ApiName — agent notes

This project is a Cimatron 2026 API plugin (`net48`, `x64`, C# 7.3) that loads into `CimatronE.exe`. Build output goes straight into the Cimatron Program folder, so the build *is* the deploy.

## Cimatron interop has overlapping namespaces

The Cimatron COM interop assemblies redeclare many type names across namespaces. The two biggest offenders are `interop.CimBaseAPI` and `interop.CimMdlrAPI`, which both declare:

- `ICimDocument`, `ICimEntity`, `ICimEntityList`
- `IEntityFilter`, `IEntityQuery`, `EFilterEnumType`, `EntityEnumType`
- `IInteraction`, `InteractionType`

(non-exhaustive — assume any plain-sounding name may collide).

Any file that does `using interop.CimBaseAPI;` **and** `using interop.CimMdlrAPI;` will hit CS0104 "ambiguous reference" on every unqualified use of one of those types. The compiler can't pick a winner.

### Rule

When writing or editing a file that imports both namespaces, add file-scoped `using` aliases at the top to pin each shared type to a single namespace. The canonical choice in this project is `interop.CimBaseAPI`:

```csharp
using interop.CimBaseAPI;
using interop.CimMdlrAPI;
using ICimEntity     = interop.CimBaseAPI.ICimEntity;
using ICimEntityList = interop.CimBaseAPI.ICimEntityList;
using ICimDocument   = interop.CimBaseAPI.ICimDocument;
using EntityEnumType = interop.CimBaseAPI.EntityEnumType;
using IEntityFilter  = interop.CimBaseAPI.IEntityFilter;
using IEntityQuery   = interop.CimBaseAPI.IEntityQuery;
using EFilterEnumType = interop.CimBaseAPI.EFilterEnumType;
using InteractionType = interop.CimBaseAPI.InteractionType;
using IInteraction   = interop.CimBaseAPI.IInteraction;
```

`helpers/FeatureGuide.cs` is the canonical example — copy its alias block when starting a new file that touches both interop namespaces. Aliases are file-scoped, so they must be added per-file; there is no project-wide shortcut for net48 / C# 7.3.

If you genuinely need the `CimMdlrAPI` variant of one of these types in the same file, qualify it inline (`interop.CimMdlrAPI.ICimEntityList`) rather than dropping the alias.

### Watch out for a *third* declaration in CimServicesAPI

A few names — notably `IEntityFilter` — are *also* declared in `interop.CimServicesAPI`, and that's the variant some service-layer methods expect. For example, `IPickTool.SetFilter(IEntityFilter, ...)` in `helpers/FeatureGuide.cs` wants `interop.CimServicesAPI.IEntityFilter`, not the CimBaseAPI one the field is typed as. Cast at the call site:

```csharp
m_PickToolHelper.SetFilter((interop.CimServicesAPI.IEntityFilter)m_EnttFilter, 0);
```

If a method signature complains about a type that "looks right", check whether `CimServicesAPI` declares the same name and cast at the boundary.

## Plugin discovery via ExternalCommands.ini

A built DLL in the Cimatron Program folder is invisible to Cimatron unless its plugin class is also listed in `C:\ProgramData\Cimatron\Cimatron\2026.0\Data\ExternalCommands.ini`:

```ini
[Plugin Ext Commands]
ApiName.ApiNamePlugin=ApiName.ApiNamePlugin@1
```

**The INI key must be the class implementing `CimUIInfrastructure.PlugIn.ICimApiCommandPlugin`** (the class with `AppendCommand()` returning an `ApiCommand`). **NOT** the `ICimWpfCommand` class (the class with `OnCommand`/`OnCommandUI`). Cimatron instantiates whatever the key names and casts it to `ICimApiCommandPlugin`; pointing at the wrong class throws `System.InvalidCastException` inside `CimUIInfrastructure.dll` at load time, which manifests as "the plugin loaded but the toolbar button never appears".

`@1` tells Cimatron to re-read the command's UI properties (caption, menu path, icon) on next launch; it auto-flips to `@0` after that launch. Use `@1` after editing the `ApiCommand` initialization in your Plugin class.

Use `/cimatron-api:register-command` / `/cimatron-api:unregister-command` to maintain this file rather than hand-editing it.

## One plugin DLL = one ApiCommand = one INI entry

Every working Cimatron 2026 plugin observed so far has exactly one `ICimApiCommandPlugin` class, one `AppendCommand()` (singular), and one `ApiCommand` return — producing one toolbar button. There is no verified working example of `AppendCommands()` plural returning multiple commands from a single DLL.

To ship a second toolbar button, scaffold a sibling plugin via `/cimatron-api:new-cimatron-api` rather than trying to add a second `ApiCommand` to this one. Share code between them by extracting a helper class or referencing a common library project; don't try to multiplex two buttons inside one DLL.

## Look up Cimatron APIs, don't guess them

The `cimatron-api` skill indexes the Cimatron SDK documentation. Before writing code that calls a Cimatron interface, procedure, or enum you haven't seen in this project, search there for the canonical namespace, signature, and usage example. Inferring from type names is how the ambiguity above gets reintroduced.

## Verify before reporting done

Cimatron interop quirks (the namespace overlap above, embedded COM types, COM `IPicture` conversions, etc.) routinely produce code that *looks* right and doesn't compile. Always:

1. Run `dotnet build` (`Ctrl+Shift+B` in VSCode) and resolve every error and warning before saying a task is complete.
2. If the user can run the plugin, ask them to F5 and confirm the new behavior in Cimatron itself — type checking proves the code compiles, not that the feature works.

## Build/deploy quirks worth knowing

- `OutputPath` is `$(CimatronRootPath)` — the DLL drops straight into `Program Files`. VSCode must be elevated, and `CimatronE.exe` must be closed, or the build fails with "access denied" or "file locked".
- All `interop.*` references use `EmbedInteropTypes=True`. Don't disable that — it's how the plugin avoids shipping the Cimatron COM PIAs.
- `LangVersion` is pinned to `7.3` because of net48. Don't use `using` declarations, target-typed `new`, `record`, pattern-matching switch expressions, or other C# 8+ features — they won't compile.
