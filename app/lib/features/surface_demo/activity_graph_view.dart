import 'dart:convert';
import 'dart:math';

import 'package:digitalbrain_flutter/features/live/graph/brain_painter.dart';
import 'package:digitalbrain_flutter/features/live/graph/cluster_layout.dart';
import 'package:digitalbrain_flutter/features/live/graph/comet.dart';
import 'package:flutter/material.dart';
import 'package:vector_math/vector_math_64.dart' as vm;

class ActivityGraphView extends StatefulWidget {
  const ActivityGraphView({super.key, required this.data});

  final Map<String, Object?>? data;

  @override
  State<ActivityGraphView> createState() => _ActivityGraphViewState();
}

class _ActivityGraphViewState extends State<ActivityGraphView>
    with SingleTickerProviderStateMixin {
  late final AnimationController _ticker;
  final List<GraphNode> _nodes = [];
  final List<GraphEdge> _edges = [];
  final List<Comet> _comets = [];
  final Set<String> _seenEdges = {};
  double _rotation = 0;

  @override
  void initState() {
    super.initState();
    _applyData(widget.data);
    _ticker = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 1),
    )
      ..addListener(_tick)
      ..repeat();
  }

  @override
  void didUpdateWidget(covariant ActivityGraphView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.data, widget.data)) {
      _applyData(widget.data);
    }
  }

  @override
  void dispose() {
    _ticker.dispose();
    super.dispose();
  }

  void _tick() {
    final now = DateTime.now();
    stepLayout(nodes: _nodes, edges: _edges, dt: 0.85);
    _comets.removeWhere((comet) => comet.isExpired(now));
    _rotation += 0.004;
    if (mounted) setState(() {});
  }

  void _applyData(Map<String, Object?>? data) {
    if (data == null) return;

    final now = DateTime.now();
    final activeNodeIds = _activeNodeIds(data);
    final currentNodes = {for (final node in _nodes) node.id: node};
    final nextNodes = <GraphNode>[];

    for (final row in _mapList(data['nodes'])) {
      final id = _string(row['id']);
      if (id.isEmpty) continue;

      final domain = _string(row['domain'], _domainFor(id));
      final activity = _number(row['activity']);
      final node = currentNodes[id] ??
          GraphNode(
            id: id,
            domain: domain,
            position: sphericalSeed(id, domain),
            firstSeenAt: now,
            lastSeenAt: now,
          );

      node.domain = domain;
      node.lastSeenAt = now;
      node.isActive =
          activeNodeIds.contains(id) || _boolean(row['active']) || activity > 0.68;
      nextNodes.add(node);
    }

    _nodes
      ..clear()
      ..addAll(nextNodes);

    final nextEdges = <GraphEdge>[];
    for (final row in _mapList(data['edges'])) {
      final from = _string(row['from']);
      final to = _string(row['to']);
      if (from.isEmpty || to.isEmpty) continue;

      final typeName = _string(row['type'], _string(row['typeName'], 'Synapse'));
      final correlationId =
          _string(row['correlationId'], _string(data['correlationId']));
      final at = _date(row['at'], now);
      final edge = GraphEdge(
        at: at,
        fromId: from,
        toId: to,
        typeName: typeName,
        methodName: _string(row['method'], _string(row['methodName'])),
        correlationId: correlationId,
        payload: utf8.encode(jsonEncode(row)),
        pulseUntil: at.add(const Duration(seconds: 8)),
      );

      final key = '$from>$to>$typeName>$correlationId';
      if (_seenEdges.add(key)) {
        _comets.add(Comet(edge: edge, startedAt: now));
        if (_seenEdges.length > 128) {
          _seenEdges.clear();
          _seenEdges.addAll(nextEdges.take(32).map(
                (e) => '${e.fromId}>${e.toId}>${e.typeName}>${e.correlationId}',
              ));
        }
      }
      nextEdges.add(edge);
    }

    _edges
      ..clear()
      ..addAll(nextEdges);
  }

  @override
  Widget build(BuildContext context) {
    final data = widget.data;
    final title = _string(data?['title'], 'Live Brain Observability');
    final phase = _string(data?['phase'], 'waiting for activity');
    final correlationId = _string(data?['correlationId']);
    final events = data == null ? const <Map<String, Object?>>[] : _mapList(data['events']);

    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: const Color(0xFF11141B),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final compact = constraints.maxWidth < 760;
          final graph = Stack(
            children: [
              Positioned.fill(
                child: _nodes.isEmpty
                    ? const _EmptyGraph()
                    : CustomPaint(
                        painter: BrainPainter(
                          nodes: List<GraphNode>.unmodifiable(_nodes),
                          edges: List<GraphEdge>.unmodifiable(_edges),
                          comets: List<Comet>.unmodifiable(_comets),
                          cam: _camera,
                          zoom: compact ? 0.82 : 1.0,
                          selectedNeuronId: null,
                          selectedEdge: _edges.isEmpty ? null : _edges.last,
                          now: DateTime.now(),
                          highlightEdge: _edges.isEmpty ? null : _edges.last,
                          activeCorrelations: correlationId.isEmpty
                              ? const <String>{}
                              : <String>{correlationId},
                          showSynapses: true,
                          rimGlowEnabled: true,
                        ),
                      ),
              ),
              Positioned(
                left: 14,
                top: 14,
                right: 14,
                child: _GraphHeader(
                  title: title,
                  phase: phase,
                  nodeCount: _nodes.length,
                  edgeCount: _edges.length,
                ),
              ),
            ],
          );

          if (compact) {
            return Column(
              children: [
                Expanded(child: graph),
                SizedBox(height: 188, child: _EventRail(events: events)),
              ],
            );
          }

          return Row(
            children: [
              Expanded(child: graph),
              SizedBox(width: 300, child: _EventRail(events: events)),
            ],
          );
        },
      ),
    );
  }

  vm.Matrix4 get _camera => vm.Matrix4.identity()
    ..setEntry(3, 2, 0.0007)
    ..rotateX(-0.34)
    ..rotateY(_rotation)
    ..rotateZ(sin(_rotation * 0.4) * 0.08);
}

class _GraphHeader extends StatelessWidget {
  const _GraphHeader({
    required this.title,
    required this.phase,
    required this.nodeCount,
    required this.edgeCount,
  });

  final String title;
  final String phase;
  final int nodeCount;
  final int edgeCount;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        color: const Color(0xCC0D0E12),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        child: Row(
          children: [
            const Icon(Icons.bubble_chart_outlined, color: Color(0xFF00FFD2)),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: Colors.white,
                      fontWeight: FontWeight.w800,
                      fontSize: 15,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    phase,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(color: Colors.white70, fontSize: 12),
                  ),
                ],
              ),
            ),
            _MetricPill(label: 'N', value: nodeCount.toString()),
            const SizedBox(width: 6),
            _MetricPill(label: 'S', value: edgeCount.toString()),
          ],
        ),
      ),
    );
  }
}

class _MetricPill extends StatelessWidget {
  const _MetricPill({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Container(
      constraints: const BoxConstraints(minWidth: 44),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.06),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: Text(
        '$label $value',
        textAlign: TextAlign.center,
        style: const TextStyle(
          color: Colors.white70,
          fontSize: 11,
          fontWeight: FontWeight.w700,
        ),
      ),
    );
  }
}

class _EventRail extends StatelessWidget {
  const _EventRail({required this.events});

  final List<Map<String, Object?>> events;

  @override
  Widget build(BuildContext context) {
    final latest = events.reversed.take(7).toList();
    return DecoratedBox(
      decoration: BoxDecoration(
        color: const Color(0xFF151820),
        border: Border(left: BorderSide(color: Colors.white.withValues(alpha: 0.08))),
      ),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Row(
              children: [
                Icon(Icons.timeline, color: Color(0xFFFFD166), size: 18),
                SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Journaled Synapses',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontWeight: FontWeight.w800, fontSize: 13),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            if (latest.isEmpty)
              const Expanded(
                child: Align(
                  alignment: Alignment.topLeft,
                  child: Text(
                    'Awaiting first graph surface',
                    style: TextStyle(color: Colors.white60),
                  ),
                ),
              )
            else
              Expanded(
                child: ListView.separated(
                  padding: EdgeInsets.zero,
                  itemCount: latest.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (context, index) => _EventRow(event: latest[index]),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _EventRow extends StatelessWidget {
  const _EventRow({required this.event});

  final Map<String, Object?> event;

  @override
  Widget build(BuildContext context) {
    final type = _string(event['type'], 'event');
    final title = _string(
      event['title'],
      _string(event['activity'], _string(event['synapseId'], type)),
    );
    final nodeId = _string(event['nodeId']);
    final causation = _string(event['causationId']);

    return Container(
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.045),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.white.withValues(alpha: 0.06)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              color: Colors.white,
              fontWeight: FontWeight.w700,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 5),
          Text(
            nodeId.isEmpty ? type : '$type -> $nodeId',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(color: Colors.white60, fontSize: 11),
          ),
          if (causation.isNotEmpty) ...[
            const SizedBox(height: 4),
            Text(
              'cause ${causation.substring(0, min(8, causation.length))}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(color: Color(0xFFFFD166), fontSize: 10),
            ),
          ],
        ],
      ),
    );
  }
}

class _EmptyGraph extends StatelessWidget {
  const _EmptyGraph();

  @override
  Widget build(BuildContext context) {
    return const Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.hub_outlined, color: Color(0xFF00FFD2), size: 42),
          SizedBox(height: 12),
          Text(
            'Waiting for activity graph',
            style: TextStyle(color: Colors.white70, fontWeight: FontWeight.w700),
          ),
        ],
      ),
    );
  }
}

Set<String> _activeNodeIds(Map<String, Object?> data) {
  final ids = <String>{};
  for (final event in _mapList(data['events'])) {
    final id = _string(event['nodeId']);
    if (id.isNotEmpty) ids.add(id);
  }
  return ids;
}

List<Map<String, Object?>> _mapList(Object? value) {
  if (value is! Iterable) return const [];
  return value.map(_map).where((row) => row.isNotEmpty).toList(growable: false);
}

Map<String, Object?> _map(Object? value) {
  if (value is Map<String, Object?>) return value;
  if (value is Map) {
    return value.map((key, value) => MapEntry(key.toString(), value));
  }
  return const {};
}

String _string(Object? value, [String fallback = '']) {
  if (value == null) return fallback;
  final text = value.toString();
  return text.isEmpty ? fallback : text;
}

double _number(Object? value) {
  if (value is num) return value.toDouble();
  return double.tryParse(value?.toString() ?? '') ?? 0;
}

bool _boolean(Object? value) {
  if (value is bool) return value;
  return value?.toString().toLowerCase() == 'true';
}

DateTime _date(Object? value, DateTime fallback) =>
    DateTime.tryParse(value?.toString() ?? '') ?? fallback;

String _domainFor(String id) {
  final lower = id.toLowerCase();
  if (lower.contains('gateway') || lower.contains('kernel')) return 'kernel';
  if (lower.contains('pack') || lower.contains('generated')) return 'dynamic';
  if (lower.contains('journal') || lower.contains('data')) return 'data';
  if (lower.contains('market')) return 'system';
  return 'system';
}
