# Architectural Analysis & Signature Verification Report: Living Canvas UI Unification (Slice 1)

This report details the deep read-only investigation and verification of Slice 1 (S1) of the Living Canvas UI Unification and Simplification in DigitalBrain. S1 replaces multiple heavy routes with a single unified screen (`LivingCanvasScreen`) that integrates the full-bleed neuron graph (`LiveScreen`), a floating query interface (`FloatingPromptDock`), and an RFW runtime host (`RfwRuntimeHost`).

---

## 1. Routing Setup Investigation (`UI/flutter/lib/router.dart`)

The current routing configuration in `UI/flutter/lib/router.dart` uses `go_router` to manage paths across three distinct full-screen pages. In S1, we will unify these screens into a single `LivingCanvasScreen`.

### A. Imports to be Retired
Once the routes are unified, the following legacy feature page imports are orphaned and **must be retired**:

| Import Statement | Purpose / Screen | Target Status |
| --- | --- | --- |
| `import 'features/brain/brain_scene_screen.dart';` | Legacy L2 Brain Scene Screen (~6,474 lines) | **Retire** (replaced by unified canvas) |
| `import 'features/constellation/constellation_screen.dart';` | Legacy L1 Constellation Screen (~5 files) | **Retire** (replaced by unified canvas) |
| `import 'features/home/constructor_editor_home_page.dart';` | Legacy Workspace Home Page (~3,840 lines) | **Retire** (replaced by unified canvas) |
| `import 'package:google_fonts/google_fonts.dart';` | Font loader for legacy placeholder | **Retire** (no longer used in `router.dart` once placeholder is deleted) |
| `import 'theme/digitalbrain_theme.dart';` | Theme color definitions for placeholder | **Retire** (no longer used in `router.dart` once placeholder is deleted) |

*Note:* `import 'package:flutter/services.dart';` must be **kept** because the Escape key shortcut mapping uses `LogicalKeyboardKey.escape`.

### B. Routes to be Retired
The following routes in `digitalbrainRouter` are to be fully retired and cleaned up:

1. **The Constellation Route (`/constellation`)**
   ```dart
   GoRoute(
     path: '/constellation',
     name: 'constellation',
     builder: (context, state) => const ConstellationScreen(),
   ),
   ```
   *Action:* **Retire** (delete).

2. **The Active Brain Workspace Route (`/brain/:brainId`)**
   ```dart
   GoRoute(
     path: '/brain/:brainId',
     name: 'brain-app',
     pageBuilder: (context, state) {
       final brainId = state.pathParameters['brainId'] ?? 'primary';
       return CustomTransitionPage(
         key: state.pageKey,
         child: CallbackShortcuts(
           bindings: <ShortcutActivator, VoidCallback>{
             const SingleActivator(LogicalKeyboardKey.escape): () {
               context.go('/');
             },
           },
           child: Focus(
             autofocus: true,
             child: BrainSceneScreen(brainId: brainId),
           ),
         ),
         transitionsBuilder: (context, animation, secondaryAnimation, child) {
           return FadeTransition(
             opacity: CurveTween(curve: Curves.easeInOutCirc).animate(animation),
             child: child,
           );
         },
       );
     },
   ),
   ```
   *Action:* **Retire** (delete).

3. **The Root Route (`/`) Redirect Target**
   The `/` route itself remains, but its target is re-routed:
   - **Before:** Returns `child: const ConstructorEditorHomePage(),` inside the keyboard focus wrapper.
   - **After:** Returns `child: const LivingCanvasScreen(),` inside the same focus wrapper.

4. **The `BrainScenePlaceholder` Widget Class**
   The class `BrainScenePlaceholder` (lines 80–175) is entirely retired and deleted because it serves as an offline L2 space which is no longer needed.

---

## 2. API Signature Verification of Living Canvas Core APIs

To construct the new `LivingCanvasScreen` safely, we investigated the actual declarations of all target classes and methods across the codebase. Below is the precise signature mapping and exact import statement required for each of the 10 components.

### 1. `resolveKernelEndpoint()`
- **File Location:** `UI/flutter/lib/grpc/endpoint.dart` (Line 4)
- **Signature:** 
  ```dart
  (String host, int port, bool secure) resolveKernelEndpoint()
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/grpc/endpoint.dart';
  ```

### 2. `createKernelChannel()`
- **File Location:** `UI/flutter/lib/grpc/grpc_channel.dart` (Line 7)
- **Signature:** 
  ```dart
  dynamic createKernelChannel({
    required String host,
    required int port,
    required bool secure,
  })
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
  ```

### 3. `kernelInterceptors()`
- **File Location:** `UI/flutter/lib/grpc/grpc_channel.dart` (Line 22)
- **Signature:** 
  ```dart
  List<ClientInterceptor> kernelInterceptors()
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
  ```

### 4. `DigitalBrainGatewayClient`
- **File Location:** `UI/flutter/lib/grpc/digitalbrain.pbgrpc.dart` (Line 24)
- **Signature (Class Ctor):** 
  ```dart
  class DigitalBrainGatewayClient extends $grpc.Client {
    DigitalBrainGatewayClient(super.channel, {super.options, super.interceptors});
    ...
  }
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
  ```

### 5. `SubmitPromptRequest`
- **File Location:** `UI/flutter/lib/grpc/digitalbrain.pb.dart` (Line 523, exported by `digitalbrain.pbgrpc.dart`)
- **Signature (Fields & Setters):** 
  ```dart
  class SubmitPromptRequest extends $pb.GeneratedMessage {
    factory SubmitPromptRequest({
      $core.String? userId,
      $core.String? text,
      $core.String? correlationId,
    }) { ... }
    
    $core.String get userId => $_getSZ(0);
    set userId($core.String value) => $_setString(0, value);
    
    $core.String get text => $_getSZ(1);
    set text($core.String value) => $_setString(1, value);
    
    $core.String get correlationId => $_getSZ(2);
    set correlationId($core.String value) => $_setString(2, value);
  }
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
  ```

### 6. `LiveScreen` (and `LiveScreenController`)
- **File Location:** `UI/flutter/lib/features/live/live_screen.dart` (Line 80)
- **Signature (Class Widget):** 
  ```dart
  class LiveScreen extends StatefulWidget {
    const LiveScreen({
      super.key,
      this.onNeuronZoom,
      this.controller,
      this.onSynapseEdge,
      this.selectedNeuronId,
      this.onSelectionCleared,
      this.activeScope = 'private',
      this.activeLayout = 'default',
      this.activeTab = 0,
      this.isMonochrome = false,
    });
    ...
  }
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/features/live/live_screen.dart';
  ```

### 7. `FloatingPromptDock`
- **File Location:** `UI/flutter/lib/features/brain/widgets/floating_prompt_dock.dart` (Line 10)
- **Signature (Class Widget):** 
  ```dart
  class FloatingPromptDock extends StatefulWidget {
    const FloatingPromptDock({
      required this.client,
      required this.onSubmit,
      this.onListeningChanged,
      super.key,
    });
    ...
  }
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/features/brain/widgets/floating_prompt_dock.dart';
  ```

### 8. `SynapseStreamScope` (and `SynapseStreamFeed`)
- **File Location:** `UI/flutter/lib/rfw_host/synapse_stream_scope.dart` (Line 18)
- **Signature:** 
  ```dart
  class SynapseStreamScope extends InheritedNotifier<SynapseStreamFeed> {
    const SynapseStreamScope({
      required super.notifier,
      required super.child,
      super.key,
    });
    
    static SynapseStreamFeed? maybeOf(BuildContext c) => ...
  }
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/rfw_host/synapse_stream_scope.dart';
  ```

### 9. `DigitalBrainClientScope`
- **File Location:** `UI/flutter/lib/shell/digitalbrain_client_scope.dart` (Line 8)
- **Signature:** 
  ```dart
  class DigitalBrainClientScope extends InheritedWidget {
    const DigitalBrainClientScope({
      super.key,
      required this.client,
      required super.child,
    });
    
    static DigitalBrainGatewayClient? of(BuildContext context) => ...
  }
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/shell/digitalbrain_client_scope.dart';
  ```

### 10. `RfwRuntimeHost`
- **File Location:** `UI/flutter/lib/rfw_host/rfw_runtime_host.dart` (Line 10)
- **Signature:** 
  ```dart
  class RfwRuntimeHost {
    RfwRuntimeHost();
    
    void ensureLoaded(String key, String source) { ... }
    
    Widget render(
      String key, {
      required Map<String, Object?> data,
      required RemoteEventHandler onEvent,
      String rootWidget = 'root',
    }) { ... }
  }
  ```
- **Exact Import:** 
  ```dart
  import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
  ```

---

## 3. LivingCanvasScreen Code Verification

The proposed implementation of `LivingCanvasScreen` has been verified in the codebase at `UI/flutter/lib/features/canvas/living_canvas_screen.dart` and compile-tested via `flutter analyze`.

### Compilation Output & Warnings
Running `flutter analyze lib/features/canvas/living_canvas_screen.dart` yields the following result:
```
warning - The value of the field '_host' isn't used - lib\features\canvas\living_canvas_screen.dart:24:24 - unused_field
warning - The value of the field '_voiceActive' isn't used - lib\features\canvas\living_canvas_screen.dart:32:8 - unused_field

2 issues found.
```

**Verification Assessment:**
- **No Compilation Errors:** The file compiles perfectly with zero errors. All type parameters, import targets, and gRPC client scopes are correctly matched.
- **Expected Warnings:** 
  - `_host` (instance of `RfwRuntimeHost`) is instantiated to be ready for card rendering in Slice 2 (S2). This is deliberate, as S1 is restricted to the basic canvas & dock layout.
  - `_voiceActive` is set via the listening toggle on the `FloatingPromptDock`, but its state is not yet read on the canvas level. This is standard as voice reaction animation triggers are part of follow-on slices.

This proves that `LivingCanvasScreen` perfectly aligns with the in-repo APIs and is ready to be mapped as the root route inside `UI/flutter/lib/router.dart`.
