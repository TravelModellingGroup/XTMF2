# How to Write an XTMF2 Tutorial

Tutorials can be shipped with a custom module library. They are discovered by the XTMF2 GUI alongside the library's modules, so you do not need to modify the XTMF2 repository or reference the GUI project.

## 1. Add a tutorial provider to your module library

Reference the `XTMF2.Interfaces` assembly from your module library, then add a public provider class. The provider must be concrete, non-generic, and have a public parameterless constructor.

The following example teaches users to add a custom `TripImporter` module:

```csharp
using System.Collections.Generic;
using XTMF2;

public sealed class ImporterTutorialProvider : ITutorialProvider
{
    public IEnumerable<TutorialDefinition> GetTutorials()
    {
        yield return new TutorialDefinition(
            "acme.trips.add-importer",
            "Add a Trip Importer",
            "Add the TripImporter module to a model system.",
            new[]
            {
                new TutorialStepDefinition(
                    "add-importer",
                    "Add the importer",
                    "Add a TripImporter module to the canvas.",
                    new TutorialCompletionCondition(
                        TutorialCompletionKinds.ModuleAdded,
                        typeof(TripImporter).AssemblyQualifiedName),
                    targetId: "canvas")
                {
                    SkipAction = context =>
                        context.EnsureModule(typeof(TripImporter), "Trip Importer")
                }
            },
            order: 1);
    }
}
```

Replace `TripImporter` with a module type from your library. The tutorial ID must be unique across all loaded libraries; IDs are compared without regard to case. The `order` controls the tutorial's position within your library's tutorial list.

## 2. Make the provider discoverable

Place the provider in an assembly that contains at least one module type loaded by XTMF2. The GUI inspects assemblies belonging to loaded module types and discovers their public `ITutorialProvider` implementations. An assembly containing only a tutorial provider, but no loaded module types, will not be found by this discovery path.

The GUI creates providers using their public parameterless constructors. Keep `GetTutorials()` self-contained; it is called while the tutorial catalog is being built.

## 3. Define useful steps

Each `TutorialStepDefinition` contains:

- A step ID, title, and learner-facing instructions.
- A `TutorialCompletionCondition` that determines when the learner can continue.
- An optional hint and optional UI `targetId`.
- An optional `SkipAction` that prepares the model state before moving to the next step.

Common completion kinds include:

| Kind | What it detects | Typical target |
|---|---|---|
| `Manual` | The learner can advance immediately | None |
| `ModuleAdded` | A new module was added during this step | Module type name; an assembly-qualified name is most specific |
| `ModuleRenamed` | A module was renamed during this step | The resulting module name |
| `StartAdded` | A Start was added | None |
| `LinkAdded` | A link was added | The origin hook name |
| `FunctionTemplateAdded` | A function template was added | Template name |
| `FunctionInstanceAdded` | An instance was added | Template name |
| `FunctionTemplateOpened` / `FunctionTemplateClosed` | The learner entered or left a template | None |
| `CommentAdded` | A comment was added | None |
| `ParameterValueChanged` | A parameter value changed | Node name |
| `ModelSystemSaved` | A dirty model system was saved | None |
| `ModelSystemRunCompleted` | A model-system run completed successfully | None |

For example, to wait for a particular module type, use `TutorialCompletionKinds.ModuleAdded` and `typeof(YourModule).AssemblyQualifiedName`. For `LinkAdded`, use the name of the hook from which the learner creates the link.

## 4. Prepare or skip steps safely

An optional tutorial initialization action can prepare a starting example before the first step:

```csharp
initializeAction: context => context.EnsureStart("Start")
```

Assign a step's `SkipAction` when there is a useful, deterministic model state to prepare. The `ITutorialSkipContext` exposes operations such as `EnsureStart`, `EnsureModule`, `EnsureLink`, `EnsureParameterValue`, `EnsureModelSystemVariable`, `EnsureScriptedParameter`, `EnsureFunctionInstance`, `EnsureComment`, and `SaveModelSystem`. These operations apply edits through XTMF2's editing session.

For a more involved initialization, return the first failed result so the GUI can report the problem:

```csharp
static TutorialSkipResult PrepareExample(ITutorialSkipContext context)
{
    var start = context.EnsureStart("Start");
    if (!start.Succeeded)
        return start;

    return context.EnsureModule(typeof(TripImporter), "Trip Importer");
}
```

Use the methods available on `ITutorialSkipContext`; tutorial providers should not mutate model-system objects directly.

## 5. UI targets and current limitations

`targetId` is optional. The built-in `"canvas"` target can point learners toward the canvas. Other targets only highlight controls that XTMF2's GUI explicitly supports; arbitrary target IDs do not create new highlight behavior.

Completion kinds are also currently defined and evaluated by the GUI. A library can use the built-in kinds, but adding a custom completion condition requires a change to XTMF2's GUI. The estimation- and calibration-specific kinds are intended for those built-in workflows rather than arbitrary library-specific behavior.

## 6. Try the tutorial

Build and load your module library the same way you load its modules. Open the tutorial picker in XTMF2 and check that your tutorial appears under the library name. Then walk through it once, including its skip actions, to verify the instructions, completion conditions, and prepared model state.
