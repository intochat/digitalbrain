import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter_test/flutter_test.dart';

const _nodes = [
  GraphNode(id: 'brain', label: 'BRAIN', kind: GraphNodeKind.hub),
  GraphNode(id: 'chat', label: 'chat'),
];
const _edges = [GraphEdge(id: 'e1', sourceId: 'brain', targetId: 'chat')];

/// Stands in for the three_js scene so the view can be tested without GL.
final class FakeGraphScene implements GraphScene, AnimatedGraphScene {
  int loads = 0;
  int cameraFrames = 0;
  bool disposed = false;
  String? nextPick;
  GraphPulse? pulse;
  List<GraphEdge> edges = const [];
  int animationFrames = 0;
  int rendererScaleUpdates = 0;

  @override
  Future<void> load(
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Map<String, GraphPoint> layout,
  ) async {
    loads++;
    this.edges = edges;
  }

  @override
  void applyCamera(GraphCameraState state) => cameraFrames++;

  @override
  String? pick(Offset local) => nextPick;

  @override
  Offset? project(String nodeId) =>
      nodeId == 'chat' ? const Offset(7, 9) : null;

  @override
  Widget build(BuildContext context) => GestureDetector(
    onScaleUpdate: (_) => rendererScaleUpdates++,
    child: const SizedBox.expand(key: Key('fake_scene')),
  );

  @override
  void dispose() => disposed = true;

  @override
  void setPulse(GraphPulse? pulse) => this.pulse = pulse;

  @override
  void advance(double seconds) => animationFrames++;
}

Future<(KitGraphController, FakeGraphScene)> mount(
  WidgetTester tester, {
  String? pick,
}) async {
  final scene = FakeGraphScene()..nextPick = pick;
  final controller = KitGraphController(nodes: _nodes, edges: _edges);
  await tester.pumpWidget(
    MaterialApp(
      home: KitGraphView(controller: controller, sceneFactory: () => scene),
    ),
  );
  await tester.pump();
  return (controller, scene);
}

void main() {
  testWidgets('builds and loads the graph into the scene', (tester) async {
    final (_, scene) = await mount(tester);
    expect(find.byKey(const Key('kit_graph_view')), findsOneWidget);
    expect(find.byKey(const Key('fake_scene')), findsOneWidget);
    expect(scene.loads, 1);
  });

  testWidgets('a tap that hits a node focuses it', (tester) async {
    final (controller, _) = await mount(tester, pick: 'chat');
    await tester.tap(find.byKey(const Key('kit_graph_view')));
    await tester.pump();
    expect(controller.selected, 'chat');
  });

  testWidgets('a tap that hits nothing changes no selection', (tester) async {
    final (controller, _) = await mount(tester);
    await tester.tap(find.byKey(const Key('kit_graph_view')));
    await tester.pump();
    expect(controller.selected, isNull);
  });

  testWidgets('the view drives the camera each frame', (tester) async {
    final (_, scene) = await mount(tester);
    await tester.pump(const Duration(milliseconds: 32));
    expect(scene.cameraFrames, greaterThan(0));
  });

  testWidgets('one mouse move preserves the full orbit drag', (tester) async {
    final (controller, _) = await mount(tester);
    final before = controller.camera.target;
    final gesture = await tester.startGesture(
      tester.getCenter(find.byKey(const Key('kit_graph_view'))),
      kind: PointerDeviceKind.mouse,
    );
    await gesture.moveBy(const Offset(-94, 30));
    await gesture.up();
    await tester.pump();
    expect(controller.camera.target.yaw, lessThan(before.yaw));
    expect(controller.camera.target.pitch, greaterThan(before.pitch));
  });

  testWidgets('renderer scale recognizers cannot steal graph orbit drags', (
    tester,
  ) async {
    final (controller, scene) = await mount(tester);
    final before = controller.camera.target;
    await tester.drag(
      find.byKey(const Key('kit_graph_view')),
      const Offset(-100, 40),
    );
    await tester.pump();
    expect(controller.camera.target.yaw, lessThan(before.yaw));
    expect(controller.camera.target.pitch, greaterThan(before.pitch));
    expect(scene.rendererScaleUpdates, 0);
  });

  testWidgets('installs a projector and removes it on dispose', (tester) async {
    final (controller, scene) = await mount(tester);
    expect(controller.projectToScreen('chat'), const Offset(7, 9));

    await tester.pumpWidget(const MaterialApp(home: SizedBox()));
    expect(scene.disposed, isTrue);
    expect(controller.projectToScreen('chat'), isNull);
  });

  testWidgets('subscription graph changes reload without resetting camera', (
    tester,
  ) async {
    final (controller, scene) = await mount(tester);
    controller.camera.orbitBy(20, 5);
    final yaw = controller.camera.target.yaw;
    controller.setGraph(nodes: _nodes, edges: const []);
    await tester.pump();
    expect(scene.loads, 2);
    expect(scene.edges, isEmpty);
    expect(controller.camera.target.yaw, yaw);
    controller.focus('chat');
    await tester.pump();
    expect(scene.loads, 2, reason: 'Selection does not rebuild geometry');
  });

  testWidgets('pulse changes and stopping reach the 3D renderer', (
    tester,
  ) async {
    final scene = FakeGraphScene();
    final controller = KitGraphController(nodes: _nodes, edges: _edges);
    const pulse = GraphPulse(fromId: 'brain', toId: 'chat', signature: 'run:1');
    await tester.pumpWidget(
      MaterialApp(
        home: KitGraphView(
          controller: controller,
          sceneFactory: () => scene,
          pulse: pulse,
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 32));
    expect(scene.pulse, pulse);
    expect(scene.animationFrames, greaterThan(0));
    await tester.pumpWidget(
      MaterialApp(
        home: KitGraphView(controller: controller, sceneFactory: () => scene),
      ),
    );
    expect(scene.pulse, isNull);
    await tester.pumpWidget(const SizedBox());
    controller.dispose();
  });
}
