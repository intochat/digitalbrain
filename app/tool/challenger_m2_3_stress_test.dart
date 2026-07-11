import 'dart:io';

// Simplified representation of VisualNode and VisualConnection for scalability simulation
class VisualNodeMock {
  final String id;
  final String label;
  final double x;
  final double y;
  final List<String> ports;

  VisualNodeMock({
    required this.id,
    required this.label,
    required this.x,
    required this.y,
    required this.ports,
  });
}

class VisualConnectionMock {
  final String id;
  final String fromNodeId;
  final String fromPortId;
  final String toNodeId;
  final String toPortId;

  VisualConnectionMock({
    required this.id,
    required this.fromNodeId,
    required this.fromPortId,
    required this.toNodeId,
    required this.toPortId,
  });
}

void main() {
  stdout.writeln('=== CHALLENGER MILESTONE 2 & 3 PERFORMANCE & STRESS TESTS ===\n');

  int totalTests = 0;
  int passedTests = 0;

  void assertTest(String name, bool condition) {
    totalTests++;
    if (condition) {
      passedTests++;
      stdout.writeln('[PASS] $name');
    } else {
      stdout.writeln('[FAIL] $name');
    }
  }

  // -------------------------------------------------------------
  // Test 1: Static Code Inspection of BrainCanvas2DGraphPainter
  // -------------------------------------------------------------
  stdout.writeln('--- Static Inspection: Allocations inside BrainCanvas2DGraphPainter.paint ---');
  try {
    final file = File('lib/widgets/brain_canvas_2d_graph.dart');
    final content = file.readAsStringSync();
    
    // Find the paint method in BrainCanvas2DGraphPainter
    final paintMethodStart = content.indexOf('void paint(Canvas canvas, Size size)');
    final paintMethodEnd = content.indexOf('shouldRepaint', paintMethodStart);
    
    if (paintMethodStart == -1 || paintMethodEnd == -1) {
      assertTest('BrainCanvas2DGraphPainter.paint method found', false);
    } else {
      final paintBody = content.substring(paintMethodStart, paintMethodEnd);
      
      // Count allocations in paint loop
      final paintAllocations = RegExp(r'Paint\(\)').allMatches(paintBody).length;
      final pathAllocations = RegExp(r'Path\(\)').allMatches(paintBody).length;
      final textPainterAllocations = RegExp(r'TextPainter\(\)').allMatches(paintBody).length;
      final textSpanAllocations = RegExp(r'TextSpan\(\)').allMatches(paintBody).length;
      final textLayoutCalls = RegExp(r'\.layout\(').allMatches(paintBody).length;
      
      stdout.writeln('Detected allocations inside 60fps Paint loop:');
      stdout.writeln('  - Paint() objects allocated: $paintAllocations');
      stdout.writeln('  - Path() objects allocated: $pathAllocations');
      stdout.writeln('  - TextPainter() allocated: $textPainterAllocations');
      stdout.writeln('  - TextSpan() allocated: $textSpanAllocations');
      stdout.writeln('  - TextPainter.layout() calls: $textLayoutCalls');

      assertTest(
        'REJECT: Paint objects should not be allocated dynamically in 60fps paint()',
        paintAllocations == 0,
      );
      assertTest(
        'REJECT: Path objects should not be allocated dynamically in 60fps paint()',
        pathAllocations == 0,
      );
      assertTest(
        'REJECT: TextPainter and layout calls should be cached, not run in 60fps paint()',
        textPainterAllocations == 0 && textLayoutCalls == 0,
      );
    }
  } catch (e) {
    assertTest('Static analysis of brain_canvas_2d_graph.dart completed without errors: $e', false);
  }

  // -------------------------------------------------------------
  // Test 2: Static Code Inspection of GridPainter and CablePainter
  // -------------------------------------------------------------
  stdout.writeln('\n--- Static Inspection: Allocations inside GridPainter and CablePainter ---');
  try {
    final file = File('lib/features/neuron_constructor/neuron_constructor_view.dart');
    final content = file.readAsStringSync();
    
    // GridPainter inspection
    final gridStart = content.indexOf('class GridPainter extends CustomPainter');
    final gridEnd = content.indexOf('class CablePainter', gridStart);
    final gridBody = content.substring(gridStart, gridEnd);
    final gridPaintAllocations = RegExp(r'Paint\(\)').allMatches(gridBody).length;
    stdout.writeln('GridPainter allocations in paint:');
    stdout.writeln('  - Paint() objects allocated: $gridPaintAllocations');
    
    // CablePainter inspection
    final cableStart = content.indexOf('class CablePainter extends CustomPainter');
    final cableBody = content.substring(cableStart);
    
    final cablePaintAllocations = RegExp(r'Paint\(\)').allMatches(cableBody).length;
    final cablePathAllocations = RegExp(r'Path\(\)').allMatches(cableBody).length;
    
    stdout.writeln('CablePainter allocations in paint:');
    stdout.writeln('  - Paint() objects allocated: $cablePaintAllocations');
    stdout.writeln('  - Path() objects allocated: $cablePathAllocations');

    final cableShouldRepaintAlways = cableBody.contains('bool shouldRepaint(covariant CablePainter oldDelegate) => true;');
    stdout.writeln('  - shouldRepaint returns true always: $cableShouldRepaintAlways');

    assertTest(
      'REJECT: CablePainter has 60fps dynamic Paint allocations in paint()',
      cablePaintAllocations == 0,
    );
    assertTest(
      'REJECT: CablePainter has dynamic Path allocations inside loops in paint()',
      cablePathAllocations == 0,
    );
    assertTest(
      'REJECT: CablePainter.shouldRepaint returns true always, causing redundant paint cycles',
      !cableShouldRepaintAlways,
    );
  } catch (e) {
    assertTest('Static analysis of neuron_constructor_view.dart completed without errors: $e', false);
  }

  // -------------------------------------------------------------
  // Test 3: Stress-Testing Layout Scale & Gesture Logic (100+ Connected Nodes)
  // -------------------------------------------------------------
  stdout.writeln('\n--- Simulation: Performance of 100+ Nodes and Connections ---');
  
  final List<VisualNodeMock> nodes = [];
  final List<VisualConnectionMock> connections = [];
  
  // Construct 100 nodes
  for (int i = 0; i < 100; i++) {
    nodes.add(VisualNodeMock(
      id: 'node_$i',
      label: 'DigitalBrain.Custom.Node$i',
      x: i * 150.0,
      y: (i % 5) * 100.0,
      ports: ['node_${i}_in', 'node_${i}_out'],
    ));
  }
  
  // Establish 99 connections sequentially
  for (int i = 0; i < 99; i++) {
    connections.add(VisualConnectionMock(
      id: 'conn_$i',
      fromNodeId: 'node_$i',
      fromPortId: 'node_${i}_out',
      toNodeId: 'node_${i + 1}',
      toPortId: 'node_${i + 1}_in',
    ));
  }
  
  // 1. Simulating _handleDragEnd Port Matching Complexity
  // When a user finishes dragging a cable, we search all input ports on other nodes
  // to check if the end point is within a threshold distance (30.0px).
  // Complexity: O(N * P) where N = nodes, P = ports per node.
  
  final stopwatch = Stopwatch()..start();
  
  // Simulate a drag release at a target port position
  final dragReleasePos = [150.0 * 50 + 0, 50.0]; // near node_50_in (port offset x=0, y=50)
  
  String? targetNodeId;
  String? targetPortId;
  double minDistance = 30.0;
  
  // Run simulated search loop 1000 times to pressure test gesture drag completion
  for (int run = 0; run < 1000; run++) {
    targetNodeId = null;
    targetPortId = null;
    minDistance = 30.0;
    
    final currentPos = [dragReleasePos[0] + 5.0, dragReleasePos[1] + 5.0]; // close to target
    
    for (var node in nodes) {
      if (node.id == 'node_0') continue; // dragFromNodeId is node_0
      
      for (var portId in node.ports) {
        if (!portId.endsWith('_in')) continue; // only input ports
        
        // Relative offset is Offset(0, 50)
        final portX = node.x + 0.0;
        final portY = node.y + 50.0;
        
        final dx = portX - currentPos[0];
        final dy = portY - currentPos[1];
        final dist = dx * dx + dy * dy; // squared distance to avoid expensive sqrt
        
        if (dist < minDistance * minDistance) {
          minDistance = 30.0; // matched inside threshold
          targetNodeId = node.id;
          targetPortId = portId;
        }
      }
    }
  }
  
  stopwatch.stop();
  final totalMs = stopwatch.elapsedMicroseconds / 1000.0;
  final avgMsPerSearch = totalMs / 1000.0;
  stdout.writeln('Gesture Drag Release Port Search (1000 runs, avg per search): ${avgMsPerSearch.toStringAsFixed(6)} ms');
  stdout.writeln('Total search execution time for 1000 runs: ${totalMs.toStringAsFixed(3)} ms');
  
  assertTest(
    'A single drag release port match executes under 0.1ms for 100 nodes',
    targetNodeId == 'node_50' && targetPortId == 'node_50_in' && avgMsPerSearch < 0.1,
  );

  // -------------------------------------------------------------
  // Test 4: Code Generation Performance under Scale
  // -------------------------------------------------------------
  stdout.writeln('\n--- Simulation: Ino Code Generation Performance (100 Nodes) ---');
  final codeGenStopwatch = Stopwatch()..start();
  
  final sb = StringBuffer();
  sb.writeln('neuron DigitalBrain.Custom.LargeInvoiceReviewer');
  sb.writeln('  "Automated large spec with 100 nodes."\n');
  
  // Generate on synapse blocks for all 100 nodes
  for (var node in nodes) {
    sb.writeln('  on synapse(${node.label}) it:');
    sb.writeln('    log "Processed Node ${node.id}"');
    
    // Find outbound connections
    final outConns = connections.where((c) => c.fromNodeId == node.id).toList();
    for (final _ in outConns) {
      sb.writeln('    emit signal(DigitalBrain.Custom.TargetNode)(summary: "forward")');
    }
    sb.writeln();
  }
  
  final generatedCode = sb.toString();
  codeGenStopwatch.stop();
  
  stdout.writeln('Generated ${generatedCode.split('\n').length} lines of Ino code.');
  stdout.writeln('Code generation took ${codeGenStopwatch.elapsedMilliseconds} ms.');
  assertTest('Code generation scales and executes in < 20ms', codeGenStopwatch.elapsedMilliseconds < 20);

  // -------------------------------------------------------------
  // Summary
  // -------------------------------------------------------------
  stdout.writeln('\n=== SUMMARY ===');
  stdout.writeln('Total performance checks executed: $totalTests');
  stdout.writeln('Passed: $passedTests');
  stdout.writeln('Failed: ${totalTests - passedTests}');
  
  if (passedTests == totalTests) {
    stdout.writeln('\nALL PERFORMANCE AND STRESS CHECKS PASSED!');
    exit(0);
  } else {
    stdout.writeln('\nPERFORMANCE AND STRESS CHECKS REJECTED!');
    exit(1);
  }
}
