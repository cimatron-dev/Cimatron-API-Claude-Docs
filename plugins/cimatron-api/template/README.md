# ApiName — Cimatron 2026 API Plugin

A Cimatron 2026 API plugin scaffolded from `dotnet new cimatron-api`.

## Prerequisites

- Cimatron 2026 installed and licensed
- .NET Framework 4.8 targeting pack (`dotnet --info` should list it under SDKs/Runtimes)
- VSCode + the `ms-dotnettools.csdevkit` extension (provides the `clr` debugger for .NET Framework)
- **VSCode launched as Administrator** — the project's build output goes inside the Cimatron Program folder, which lives under `Program Files`

## First run

1. Close Cimatron.
2. Open this folder in VSCode (running as Administrator).
3. Press `F5`. VSCode will:
   - Verify it's running elevated (`check-admin` task)
   - `dotnet build` the project, dropping `ApiName.dll` directly into the Cimatron Program folder
   - Launch `CimatronE.exe` and attach the managed (`clr`) debugger
4. In Cimatron, open or create a Part/Assembly. The plugin command appears under the `APIs` toolbar.

## Customizing the command

- `ApiNamePlugin.cs` — implements `ICimApiCommandPlugin`. Controls the toolbar/menu/caption/icon. Edit `MenuPath`, `Caption`, etc. to change how the command appears.
- `ApiNameCommand.cs` — implements `ICimWpfCommand`. `OnCommand` is called when the user clicks the toolbar button. Put your feature logic there.
- `icon.ico` — replace this file with your own 16x16 or 32x32 ICO to change the toolbar icon.

## Adding more commands

Each Cimatron API command needs:
- An `ApiCommand` registered in a class that implements `ICimApiCommandPlugin`
- An `ICimWpfCommand` implementation that handles the click

The `cimatron-api` Claude Code plugin can scaffold a second command into this project — run `/cimatron-api:add-command` from inside this folder.

## Where things live

| File | Purpose |
|------|---------|
| `ApiName.csproj` | net48 / x64 library. Outputs straight to `$(CimatronRootPath)` so the build *is* the deploy. |
| `Directory.Build.props` | Interop references pulled from the Cimatron install. Edit `<CimatronRootPath>` here if your install path differs. |
| `ApiNamePlugin.cs` | Implements `ICimApiCommandPlugin`. Cimatron discovers the plugin via this class. |
| `ApiNameCommand.cs` | Implements `ICimWpfCommand`. Where command behavior goes. |
| `helpers/Logger.cs` | File + Debug logger; default log path is `%USERPROFILE%\Downloads\ApiName.log.txt`. |
| `helpers/FeatureGuide.cs` | Thin wrapper around Cimatron's FeatureGuide / FG_Stage / entity-pick pipeline. Use it for multi-stage entity-pick dialogs. |
| `.vscode/launch.json` | F5 configurations: launch+attach, attach-only, build-only. |
| `.vscode/tasks.json` | `check-admin`, `build`, `clean`, `prelaunch`, `rebuild`. |

## Troubleshooting

- **Build fails with "access denied" writing to Program Files** — VSCode is not elevated. Close it, right-click → Run as administrator, reopen.
- **Build fails because the DLL is locked** — Cimatron is open. Close it, retry.
- **F5 doesn't attach** — make sure `ms-dotnettools.csdevkit` is installed; the older `ms-dotnettools.csharp` alone is not enough for the `clr` debugger type.
- **Cimatron doesn't show the command** — confirm the DLL landed in the right folder (check `Directory.Build.props` `<CimatronRootPath>`). The plugin must be in the same folder as `CimatronE.exe`.
