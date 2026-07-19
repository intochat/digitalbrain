# Milestone 4 Hotfix Plan: Catalog Cache Hydration & Redundancy Refactoring

## 1. Objectives
Address the critical integration findings from Milestone 4 Reviewer 2:
1. **Unhydrated Singleton Cache [Critical]**: Ensure `BrainOSCatalogManager.instance` is loaded and hydrated during the widget lifecycles of both `_CodeEditorBodyState` and `_PromptInputBodyState`.
2. **Redundant Loading & Bypassed Cache [Major]**: Refactor `_CodeEditorBodyState._loadCatalog()` to utilize `BrainOSCatalogManager.instance.ensureLoaded(context)` instead of performing direct, bypassed gRPC introspection calls.

---

## 2. Technical Modifications in `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart`

### A. Hydrate Cache in `_PromptInputBodyState`
Currently, `_PromptInputBodyState` sets up `PromptTextEditingController` but never triggers `ensureLoaded(context)` on `BrainOSCatalogManager.instance`. We will add a `didChangeDependencies` hook to load the catalog schema and trigger a widget rebuild upon loading:

```dart
class _PromptInputBodyState extends State<_PromptInputBody> {
  // ...
  bool _catalogLoaded = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_catalogLoaded) {
      _catalogLoaded = true;
      BrainOSCatalogManager.instance.ensureLoaded(context).then((_) {
        if (mounted) {
          setState(() {}); // Trigger rebuild to apply syntax highlighting/hover overlays
        }
      });
    }
  }
  // ...
}
```

### B. Refactor `_CodeEditorBodyState._loadCatalog`
Currently, `_CodeEditorBodyState._loadCatalog()` performs its own gRPC request and populates the local `_catalog` list, bypassing the singleton completely. We will refactor this method to use the singleton:

```dart
  Future<void> _loadCatalog() async {
    await BrainOSCatalogManager.instance.ensureLoaded(context);
    if (mounted) {
      setState(() {
        _catalog = BrainOSCatalogManager.instance.catalog;
      });
    }
  }
```

This ensures `BrainOSCatalogManager.instance` is correctly hydrated and then we share the same cached catalog instance between both widgets.

---

## 3. Verification Criteria
1. The Flutter UI project builds cleanly with zero compile-time or runtime issues.
2. The gRPC and fallback offline catalog paths function correctly via the cached singleton.
3. Creator Prompt plain English FQN highlighting, wildcards, and overlay hover cards render correctly.
4. Fast-stage E2E integration tests compile and run cleanly:
   ```powershell
   dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj --filter Stage=fast
   ```
