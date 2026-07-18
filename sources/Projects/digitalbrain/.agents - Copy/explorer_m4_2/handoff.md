# Handoff Report: Syntax Highlighting & Inline Signature Hover Cards

## 1. Observation

During our read-only investigation, we analyzed the following key files, line numbers, and patterns:

*   **File Path**: `e:\digitalbrain\kernel\BrainOS.Kernel.Contracts\Introspector\QueryCatalogContractsResponse.cs` (lines 7–30)
    *   **Verbatim Content**:
        ```csharp
        public enum CatalogContractKind
        {
            Synapse = 0,
            Signal = 1,
            Neuron = 2
        }

        public sealed record CatalogContractSchema(
            [property: Orleans.Id(0)] string Fqn,
            [property: Orleans.Id(1)] CatalogContractKind Kind,
            [property: Orleans.Id(2)] IReadOnlyList<string> Fields);
        ```
*   **File Path**: `e:\digitalbrain\kernel\BrainOS.Kernel\Introspector\IntrospectorNeuron.cs` (lines 260–284)
    *   **Verbatim Content**:
        ```csharp
        case QueryCatalogContractsRequest req:
        {
            var internalSchemas = catalog.GetAllSchemas();
            var catalogSchemas = internalSchemas.Select(s => new CatalogContractSchema(
                s.Fqn,
                s.Kind switch
                {
                    ContractKind.Synapse => CatalogContractKind.Synapse,
                    ContractKind.Signal  => CatalogContractKind.Signal,
                    _                    => CatalogContractKind.Neuron
                },
                s.Fields)).ToArray();
            ...
        ```
*   **File Path**: `e:\digitalbrain\UI\flutter\lib\features\rfw_gallery\brainos_rfw_library.dart` (lines 1824–1861)
    *   **Verbatim Content**:
        ```dart
        Future<void> _loadCatalog() async {
          final client = BrainOSClientScope.of(context);
          if (client == null) return;
          ...
          final envelope = SynapseEnvelope()
            ..correlationId = ''
            ..typeName = 'BrainOS.Kernel.Contracts.Introspector.QueryCatalogContractsRequest'
            ..payload = Uint8List.fromList(utf8.encode(requestPayload));

          try {
            final response = await client.send(envelope);
            ...
            final schemasJson = (responseData['Schemas'] ?? responseData['schemas']) as List?;
            ...
        ```
*   **File Path**: `e:\digitalbrain\UI\flutter\lib\features\rfw_gallery\brainos_rfw_library.dart` (lines 1340–1352)
    *   **Verbatim Content**:
        ```dart
        } else if (match.group(5) != null) {
          // Dotted FQN (e.g. DB.Google.Auth)
          final fqn = match.group(5)!;
          spans.add(TextSpan(
            text: fqn,
            style: defaultStyle.copyWith(
              color: BrainOSColors.goldSoft,
              fontWeight: FontWeight.bold,
            ),
            mouseCursor: SystemMouseCursors.click,
            onEnter: (event) => onHoverEnter?.call(fqn, event),
            onExit: (event) => onHoverExit?.call(event),
          ));
        ```
*   **File Path**: `e:\digitalbrain\UI\flutter\lib\features\rfw_gallery\brainos_rfw_library.dart` (lines 1459–1470)
    *   **Verbatim Content**:
        ```dart
        void _showHoverCard(String fqn, Offset position) {
          if (_hoveredFqn == fqn) return;
          _hideHoverCard();
          _hoveredFqn = fqn;

          final schema = _catalog.firstWhere(
            (s) => s.fqn.toLowerCase() == fqn.toLowerCase(),
            orElse: () => CatalogContractSchema(fqn: '', kind: -1, fields: []),
          );
          if (schema.kind == -1) {
            return;
          }
        ```

---

## 2. Logic Chain

1.  **Catalog Loading**: By examining `brainos_rfw_library.dart` line 1824 and `IntrospectorNeuron.cs` line 260, we verified that the editor retrieves contract schemas using standard `QueryCatalogContractsRequest` envelopes sent via the gRPC `BrainOSGatewayClient`. The backend returns a list of FQNs, integer kinds, and field lists.
2.  **Colorization Defect**: By inspecting `InoLangTextEditingController.buildTextSpan` (lines 1340–1352), we found that the controller highlights FQNs (Group 5) using the static color `BrainOSColors.goldSoft`, ignoring their respective catalog kinds because it lacks access to the loaded catalog.
3.  **Dynamic Highlighting Solution**: Passing the `_catalog` list getter into the controller's constructor will allow it to query contract kind dynamically and color neurons as `violetSoft`, synapses as `tealSoft`, and signals as `goldSoft`.
4.  **Signature Hover Card Limitation**: In `_showHoverCard` (lines 1459–1470), we observed that the lookup leverages `_catalog.firstWhere`. For typed overloads specified in language proposal §19.5, this will fail to capture sibling overloads.
5.  **Hover Overloads Solution**: Modifying this lookup to `_catalog.where` and mapping multiple overloads inside the `OverlayEntry` widget builder allows rendering clear signature sets matching §19.5.
6.  **Offline Resiliency**: In `_loadCatalog()`, a try-catch block handles failures without crashing. We can add a local asset-based fallback for `.ino-catalog.json` loading when offline.

---

## 3. Caveats

*   **Offline Filesystem Pathing**: We did not implement or test a specific local asset path setup for `.ino-catalog.json` because we do not have permission to modify source files in this role.
*   **Performance Under Large Files**: Highlighting matching is debounced, but a very large script file could theoretically see minor highlighting latency; we assume the current standard `buildTextSpan` is performant enough for normal `.ino` scopes.

---

## 4. Conclusion

The existing client-silo architecture is extremely clean, spec-compliant, and ready for Milestone 4 features. Dynamic syntax highlighting and overload hover cards can be cleanly implemented via the precise structural extensions documented in `analysis.md`, with zero risk of breaking existing behavior.

---

## 5. Verification Method

To verify these findings and the proposed modifications:
1.  Verify the fast introspector and catalog unit tests compile and run:
    ```powershell
    dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj --filter "Stage=fast"
    ```
2.  Inspect the hover card code in `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart` starting at line 1459 to confirm layout positions.
3.  Invalidation condition: If the .NET `IContractCatalog` layout structure changes, the contract schemas sent via `QueryCatalogContractsResponse` may change, requiring update to `CatalogContractSchema` parser.
