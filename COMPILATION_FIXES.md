# XTMF2 GUI Compilation Issues - Solutions

## 1. Rect.Empty and Rect.IsEmpty in Avalonia

**The Issue**: Errors like "Rect does not contain a definition for Empty" or "IsEmpty"

**The Solution**: In Avalonia 11.3.14, use the correct Rect-related APIs:

```csharp
// ✅ CORRECT: Initialize empty Rect
private Rect _sidePanelBounds = Rect.Empty;

// ✅ CORRECT: Check if Rect is empty
if (!_sidePanelLinkBounds.IsEmpty && _sidePanelLinkBounds.Contains(pos))
{
    // Rect has bounds and contains the point
}

// ✅ CORRECT: Create rect with specific bounds
var bounds = new Rect(x, y, width, height);

// ✅ CORRECT: Zero-size rect
var emptyRect = new Rect(0, 0, 0, 0);  // Alternative to Rect.Empty
```

**Key Points**:
- `Rect.Empty` is a static property that returns a rect with zero width/height
- `IsEmpty` is a read-only property that returns `true` if Width or Height is 0 or negative
- Requires: `using Avalonia;` in your imports

**Live Example from Codebase**:
- [ModelSystemCanvas.cs](src/XTMF2.GUI/Controls/ModelSystemCanvas.cs#L192): `private Rect _sidePanelBounds = Rect.Empty;`
- [ModelSystemCanvas.Input.cs](src/XTMF2.GUI/Controls/ModelSystemCanvas/ModelSystemCanvas.Input.cs#L661): `if (!_sidePanelLinkBounds.IsEmpty && _sidePanelLinkBounds.Contains(pos) && ...)`

---

## 2. Accessing ModuleRepository from ModelSystemSession in GUI Code

**The Issue**: `GetModuleRepository()` is marked as `internal`, so it's not directly accessible from external assemblies.

**The Solution**: There are multiple correct approaches depending on your context:

### Option A: Use Public Properties (Recommended for GUI Code)
Instead of accessing `ModuleRepository` directly, use the public properties that expose what you need:

```csharp
// ❌ WRONG: GetModuleRepository() is internal 
var repo = modelSystemSession.GetModuleRepository();

// ✅ CORRECT: Use public properties
var loadedModuleTypes = modelSystemSession.LoadedModuleTypes;  // IObservable<Type>
var openGenericTypes = modelSystemSession.OpenGenericModuleTypes;  // IReadOnlyList<Type>
var allExportedTypes = modelSystemSession.AllExportedTypes;  // IReadOnlyList<Type>

// ✅ CORRECT: For module compatibility checking
var compatibleTypes = modelSystemSession.GetCompatibleModuleTypes(hookType);
```

**Example from ModelSystemCanvas.Rendering.cs**:
```csharp
// Getting module metadata without direct repo access
private void GetModuleMetadata(Node node)
{
    // _vm.Session is the ModelSystemSession
    // Access LoadedModuleTypes through public property
    if (node.Type != null && _vm.Session.LoadedModuleTypes.Contains(node.Type))
    {
        // Type is valid and loaded
    }
}
```

### Option B: In Same Assembly (XTMF2.Editing Layer)
If you're within the XTMF2 core assembly, you can call the `internal` method:

```csharp
// ✅ CORRECT (only in XTMF2.Editing namespace or XTMF2 assembly)
internal ModuleRepository GetModuleRepository()
{
    return _session.GetModuleRepository();
}
```

### Option C: Via Hook Metadata
If you have a Node, you can query hooks through the node itself:

```csharp
// ✅ CORRECT: Access hooks from a Node
if (node.Type != null && node.Hooks != null)  // Hooks populated lazily
{
    foreach (var hook in node.Hooks)
    {
        // hook.Type, hook.Name, hook.Cardinality, etc.
    }
}
```

---

## 3. Common Patterns in GUI Code

### Pattern: Get Module Metadata in ModelSystemCanvas
```csharp
// DO NOT: var moduleRepo = _vm.Session.GetModuleRepository();  // ❌ Internal

// DO: Use public session properties
var module = node?.Type;
if (module != null && _vm.Session.LoadedModuleTypes.Contains(module))
{
    var hooks = node.Hooks;  // Populated lazily by the node
    foreach (var hook in hooks)
    {
        // Render hook information
    }
}
```

### Pattern: Get Compatible Types for a Hook
```csharp
// ✅ CORRECT: Use public method
var compatibleTypes = _vm.Session.GetCompatibleModuleTypes(hookType);
foreach (var type in compatibleTypes)
{
    // Add to type picker
}
```

### Pattern: Access All Exported Types
```csharp
// ✅ CORRECT: Use public property for context type picking
var allTypes = _vm.Session.AllExportedTypes;
// Use for IAction<Context> or IFunction<Context, ReturnType> context selection
```

---

## 4. Visibility Rules Summary

| Member | Scope | Accessibility | GUI Usable? |
|--------|-------|----------------|------------|
| `GetModuleRepository()` | ModelSystemSession | `internal` | ❌ Only in XTMF2 assembly |
| `LoadedModuleTypes` | ModelSystemSession | `public` | ✅ Yes, directly |
| `OpenGenericModuleTypes` | ModelSystemSession | `public` | ✅ Yes, directly |
| `AllExportedTypes` | ModelSystemSession | `public` | ✅ Yes, directly |
| `GetCompatibleModuleTypes()` | ModelSystemSession | `public` | ✅ Yes, directly |
| `Hooks` | Node | `public` | ✅ Yes, lazily populated |

---

## 5. Quick Fix Checklist

When you get compilation errors, check:

- [ ] Is `using Avalonia;` present for `Rect` types?
- [ ] Are you using `Rect.Empty` (not `Rect.default` or similar)?
- [ ] Are you accessing ModuleRepository through public session properties?
- [ ] If you need repo data, is it available through `LoadedModuleTypes`, `OpenGenericModuleTypes`, or `GetCompatibleModuleTypes()`?
- [ ] For hook metadata, are you accessing `node.Hooks` directly instead of repo?
- [ ] Are you in the GUI (XTMF2.GUI) or core (XTMF2) assembly? If GUI, don't use `internal` methods.

---

## Files Referenced

- **Avalonia Version**: 11.3.14 (from XTMF2.GUI.csproj)
- **Working Examples**:
  - [ModelSystemCanvas.cs](src/XTMF2.GUI/Controls/ModelSystemCanvas.cs)
  - [ModelSystemCanvas.Rendering.cs](src/XTMF2.GUI/Controls/ModelSystemCanvas/ModelSystemCanvas.Rendering.cs) - Line 1370 shows session usage
  - [ModelSystemSession.cs](src/XTMF2/Editing/ModelSystemSession.cs) - Lines 77-110 show repository access patterns
