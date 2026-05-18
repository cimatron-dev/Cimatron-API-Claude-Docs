This is the canonical Cimatron Feature Guide wiring snippet. It is loaded by the `feature-guide-scaffold` agent verbatim when scaffolding a new FG command. Edit here, not in the agent.

## When to include each interaction

The FeatureGuide interaction is always included. The other two are gated on the project's needs:

| Interaction | Include when | Skip when |
|---|---|---|
| `cmFeatureGuide` | Always. This is the FG itself. | Never. |
| `cmSPManager` | Any stage hosts SP figures (the common case — this is the default for `/feature-guide-scaffold` when the user just says "add a feature guide"). The events class implements `_ISPEvents` and `OnEvent` dispatches per-figure via `MySpFigureData`. | The FG is purely entity-picking with no SP UI. |
| `cmToolServices` | A stage drives a `cmContourBoundary`-style helper tool that needs the ToolServices surface. | Default. Include only when the user explicitly asks or names a tool that needs it. |

`_ICaptureImageEvents` is wired implicitly via the FeatureGuide interaction; no separate Advise needed. The events class implements it (with empty bodies by default) regardless.

## The wiring body

Drop this into the entry-point method (`ICimWpfCommand.OnCommand()` for the Plugin pattern, `ICimCommand.Execute()` for the COM pattern). `aApp` must already be resolved to an `interop.CimatronE.IApplication` at the top of the method.

```csharp
var aDoc = (interop.CimBaseAPI.ICimDocument)aApp.GetActiveDoc();
var aInteractionSink = (interop.CimBaseAPI.IInteractionSink)aDoc;

// 1) FeatureGuide interaction
var FeatureGuide = (interop.CimServicesAPI.FeatureGuide)
    aInteractionSink.CreateInteraction(interop.CimBaseAPI.InteractionType.cmFeatureGuide);

var events = new <Cmd>Events(FeatureGuide, aApp);

// 2) Advise _IFeatureGuideEvents on the FeatureGuide
AdviseConnectionPoint(FeatureGuide,
    new Guid("8B17C571-AD38-11D6-A773-000476215633"), events);   // _IFeatureGuideEvents

// 3) SPManager interaction (only when SP=yes)
var spInteraction = ((interop.CimServicesAPI.IInteractionSink)aInteractionSink)
    .CreateInteraction(interop.CimServicesAPI.InteractionType.cmSPManager);
var SPManager = (interop.CimServicesAPI.SPManager)spInteraction;
events.SpFigureData.SPManager = SPManager;
AdviseConnectionPoint(SPManager,
    new Guid("4583B016-72A5-4A99-BF9E-49F22BA6B208"), events);   // _ISPEvents

// 4) ToolServices interaction (only when ToolServices=yes)
var toolsInteraction = ((interop.CimServicesAPI.IInteractionSink)aInteractionSink)
    .CreateInteraction(interop.CimServicesAPI.InteractionType.cmToolServices);
var toolServices = (interop.CimServicesAPI.ToolServices)toolsInteraction;
events.ToolServicesData.ToolServices = toolServices;
AdviseConnectionPoint(toolServices,
    new Guid("DFAAA77F-6379-4EA2-94A0-8B9647B7DC1E"), events);   // _IToolServicesEvents

// 5) Stages
var stage1 = new FG_Stage1(FeatureGuide, events.FeatureData);
// var stage2 = new FG_Stage2(FeatureGuide, events.FeatureData, events.SpFigureData, events.ToolServicesData);

FeatureGuide.SetTitle("<short title>");
FeatureGuide.AddStage(stage1);
// FeatureGuide.AddStage(stage2);
FeatureGuide.ShowButton(interop.CimServicesAPI.FeatureGuideButtons.cmFeatureGuidePreviewButton, 0);
FeatureGuide.Activate();
FeatureGuide.SetInitStage();
```

…with the helper, declared once in the entry-point class:

```csharp
System.Runtime.InteropServices.ComTypes.IConnectionPoint mCnnctPt;
int mCookie;

void AdviseConnectionPoint(object source, Guid interfaceGuid, object sink)
{
    var container = (System.Runtime.InteropServices.ComTypes.IConnectionPointContainer)source;
    container.FindConnectionPoint(ref interfaceGuid, out mCnnctPt);
    mCnnctPt.Advise(sink, out mCookie);
}
```

## Connection-point GUIDs

These three are load-bearing and not in any docs the user can grep. Emit them verbatim with a comment naming the interface.

| Source object | Interface GUID | Comment |
|---|---|---|
| `FeatureGuide` | `8B17C571-AD38-11D6-A773-000476215633` | `_IFeatureGuideEvents` |
| `SPManager` | `4583B016-72A5-4A99-BF9E-49F22BA6B208` | `_ISPEvents` |
| `ToolServices` | `DFAAA77F-6379-4EA2-94A0-8B9647B7DC1E` | `_IToolServicesEvents` |

`_ICaptureImageEvents` is wired implicitly via the FeatureGuide interaction; no separate Advise needed.

## Variable names this snippet uses

The agent's surrounding prose references these names — keep them consistent when adapting the snippet so the rest of the FG scaffold (events class, stage classes, data classes) links up cleanly.

| Name | Type | Role |
|---|---|---|
| `aApp` | `interop.CimatronE.IApplication` | The active Cimatron application. Resolved by the entry-point method before the snippet runs. |
| `aDoc` | `interop.CimBaseAPI.ICimDocument` | The active document, obtained via `aApp.GetActiveDoc()`. |
| `aInteractionSink` | `interop.CimBaseAPI.IInteractionSink` | The document re-cast as the interaction sink. Source of all three `CreateInteraction` calls. |
| `FeatureGuide` | `interop.CimServicesAPI.FeatureGuide` | The FG interaction itself. Passed into stages and into the events class. |
| `events` | `<Cmd>Events` | The single events object that implements all four event interfaces (`_IFeatureGuideEvents`, `_ICaptureImageEvents`, `_IToolServicesEvents`, `_ISPEvents`). Same instance is Advised on all three sources. |
| `SPManager` | `interop.CimServicesAPI.SPManager` | The SP interaction. Stored on `events.SpFigureData.SPManager` so per-stage `OnPressed` can drive SP figures. Only present when SP=yes. |
| `toolServices` | `interop.CimServicesAPI.ToolServices` | The ToolServices interaction. Stored on `events.ToolServicesData.ToolServices`. Only present when ToolServices=yes. |
| `stage1` (`stage2`, …) | `FG_Stage<N>` | One per stage. Constructor takes `FeatureGuide` + whichever data classes that stage needs. |

The `<Cmd>` placeholder in `<Cmd>Events` is the FG command's base name (e.g. `AutoPlaneFeatureGuide` → `AutoPlaneFeatureGuideEvents`). Adapt only the names; the GUIDs are non-negotiable.
