import 'dart:convert';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'workspace_data_window_test.dart' show WorkspaceClient;
import 'workspace_remote_controller_test.dart' show MemoryPersistence;

class ComputeClient extends WorkspaceClient {
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (request.url.path.contains('/compute/')) {
      final body = request.url.path.endsWith('/summary')
          ? {'chargedCompute': 0, 'reservedCompute': 0, 'settledCompute': 2, 'limitCompute': 100, 'spentCompute': 2}
          : {'items': [], 'nextCursor': null};
      return http.StreamedResponse(Stream.value(utf8.encode(jsonEncode(body))), 200);
    }
    return super.send(request);
  }
}

void main() {
  for (final width in [1440.0, 390.0]) {
    testWidgets('Compute icon opens history without adding a workspace window at $width', (tester) async {
      tester.view.physicalSize = Size(width, 850);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final store = WorkspaceStore(persistence: MemoryPersistence());
      final api = ComputeClient();
      await tester.pumpWidget(WorkspaceApp(store: store,
        programmingClient: DigitalBrainUiClient(baseUri: Uri.parse('http://test'), httpClient: api)));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('compute-usage-button')));
      await tester.pumpAndSettle();
      expect(find.text('No usage recorded yet.'), findsOneWidget);
      expect(find.text('App settlements'), findsOneWidget);
      expect(find.byType(width < 600 ? BottomSheet : Dialog), findsOneWidget);
      expect(store.currentProject.presentation.openArtifactIds, isEmpty);
      expect(tester.takeException(), isNull);
      await tester.tap(find.byTooltip('Close Compute'));
      await tester.pumpAndSettle();
      await tester.pumpWidget(const SizedBox());
      await api.events.close();
      store.dispose();
    });
  }
}
