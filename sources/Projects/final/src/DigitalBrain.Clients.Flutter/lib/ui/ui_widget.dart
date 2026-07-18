import 'package:flutter/material.dart';
import 'package:flutter_markdown/flutter_markdown.dart';
import 'dart:ui' show ImageFilter, lerpDouble;
import 'dart:math' as math;
import 'theme.dart';
import 'package:modular_ui/modular_ui.dart' as mu;
import 'package:getwidget/getwidget.dart';

sealed class UiWidget {
  const UiWidget();

  factory UiWidget.fromJson(Map<String, dynamic> json) {
    if (json.containsKey('IsDivider')) {
      return const UiDivider();
    }
    if (json.containsKey('WindowId')) {
      return UiWindowFrame(
        title: json['Title'] as String? ?? '',
        content: UiWidget.fromJson(json['Content'] as Map<String, dynamic>? ?? {}),
        windowId: json['WindowId'] as String? ?? '',
        x: (json['X'] as num?)?.toDouble() ?? 10.0,
        y: (json['Y'] as num?)?.toDouble() ?? 10.0,
        width: (json['Width'] as num?)?.toDouble() ?? 320.0,
        height: (json['Height'] as num?)?.toDouble() ?? 400.0,
        zIndex: json['ZIndex'] as int? ?? 0,
        state: json['State'] as String? ?? 'floating',
      );
    }
    if (json.containsKey('Child')) {
      return UiContainer(
        child: UiWidget.fromJson(json['Child'] as Map<String, dynamic>? ?? {}),
        padding: (json['Padding'] as num?)?.toDouble() ?? 0.0,
        decoration: json['Decoration'] as String?,
      );
    }
    if (json.containsKey('Name')) {
      return UiIcon(name: json['Name'] as String? ?? '');
    }
    if (json.containsKey('Url')) {
      return UiImageWidget(url: json['Url'] as String? ?? '');
    }
    if (json.containsKey('Label') && json.containsKey('OnTap')) {
      final onTap = json['OnTap'] as Map<String, dynamic>?;
      return UiButton(
        label: json['Label'] as String? ?? '',
        onTap: onTap,
      );
    }
    if (json.containsKey('Label') && json.containsKey('Value') && json.containsKey('OnChanged')) {
      final onChanged = json['OnChanged'] as Map<String, dynamic>?;
      final value = json['Value'];
      if (value is bool) {
        return UiToggle(
          label: json['Label'] as String? ?? '',
          value: value,
          onChanged: onChanged,
        );
      } else {
        return UiTextField(
          label: json['Label'] as String? ?? '',
          value: value?.toString() ?? '',
          onChanged: onChanged,
        );
      }
    }
    if (json.containsKey('Label') && json.containsKey('Value') && !json.containsKey('OnChanged')) {
      final value = json['Value'];
      if (value is num) {
        return UiProgress(
          value: value.toDouble(),
          label: json['Label'] as String?,
        );
      } else if (value is bool) {
        return UiToggle(
          label: json['Label'] as String? ?? '',
          value: value,
        );
      } else {
        return UiTextField(
          label: json['Label'] as String? ?? '',
          value: value?.toString() ?? '',
        );
      }
    }
    if (json.containsKey('Value') && !json.containsKey('Label')) {
      final value = json['Value'];
      if (value is num) {
        return UiProgress(value: value.toDouble());
      }
      final valueStr = value as String? ?? '';
      final isMarkdown = valueStr.contains('**') ||
          valueStr.contains('#') ||
          valueStr.contains('```') ||
          valueStr.contains('- ') ||
          valueStr.contains('TODO') ||
          valueStr.contains('\n');
      return isMarkdown ? UiMarkdown(value: valueStr) : UiText(value: valueStr);
    }
    if (json.containsKey('Children')) {
      final childrenJson = (json['Children'] as List?)?.cast<Map<String, dynamic>>() ?? [];
      final children = childrenJson.map(UiWidget.fromJson).toList();
      if (json.containsKey('IsRow') || json['IsRow'] == true) {
        return UiRow(children: children);
      }
      return UiColumn(children: children);
    }
    if (json.containsKey('Title')) {
      final bodyJson = (json['Body'] as Map<String, dynamic>?) ?? {};
      return UiCard(
        title: json['Title'] as String? ?? '',
        body: UiWidget.fromJson(bodyJson),
      );
    }
    if (json.containsKey('Content')) {
      final inner = (json['Content'] as Map<String, dynamic>?) ?? {};
      return UiMainPane(content: UiWidget.fromJson(inner));
    }
    if (json.containsKey('Nodes')) {
      final nodesJson = (json['Nodes'] as List?)?.cast<Map<String, dynamic>>() ?? [];
      final edgesJson = (json['Edges'] as List?)?.cast<Map<String, dynamic>>() ?? [];
      final nodes = nodesJson.map((n) => GraphNodeData(
        (n['Id'] ?? n['id'] ?? '') as String,
        (n['Label'] ?? n['label'] ?? '') as String,
        (n['Type'] ?? n['type'] ?? 'node') as String,
      )).toList();
      final edges = edgesJson.map((e) => GraphEdgeData(
        (e['SourceId'] ?? e['sourceId'] ?? '') as String,
        (e['TargetId'] ?? e['targetId'] ?? '') as String,
        (e['Type'] ?? e['type'] ?? 'edge') as String,
      )).toList();
      return UiGraph3D(nodes: nodes, edges: edges);
    }
    if (json.containsKey('Bars')) {
      final barsJson = (json['Bars'] as List?)?.cast<Map<String, dynamic>>() ?? [];
      final bars = barsJson.map((b) => UiBar(
        label: (b['Label'] ?? b['label'] ?? '') as String,
        value: ((b['Value'] ?? b['value'] ?? 0) as num).toDouble(),
        color: (b['Color'] ?? b['color']) as String?,
      )).toList();
      return UiBarChart(
        title: (json['Title'] ?? json['title'] ?? 'Chart') as String,
        bars: bars,
      );
    }
    return const UiText(value: '?');
  }
}

class UiText extends UiWidget {
  final String value;
  const UiText({required this.value});
}

class UiButton extends UiWidget {
  final String label;
  final Map<String, dynamic>? onTap; // raw serialized Synapse JSON
  const UiButton({required this.label, this.onTap});
}

class UiCard extends UiWidget {
  final String title;
  final UiWidget body;
  const UiCard({required this.title, required this.body});
}

class UiColumn extends UiWidget {
  final List<UiWidget> children;
  const UiColumn({required this.children});
}

class UiRow extends UiWidget {
  final List<UiWidget> children;
  const UiRow({required this.children});
}

class UiMarkdown extends UiWidget {
  final String value;
  const UiMarkdown({required this.value});
}

class UiHyperlink extends UiWidget {
  final String label;
  final String url;
  const UiHyperlink({required this.label, required this.url});
}

class UiMainPane extends UiWidget {
  final UiWidget content;
  const UiMainPane({required this.content});
}

class GraphNodeData {
  final String id;
  final String label;
  final String type;
  const GraphNodeData(this.id, this.label, this.type);
}

class GraphEdgeData {
  final String sourceId;
  final String targetId;
  final String type;
  const GraphEdgeData(this.sourceId, this.targetId, this.type);
}

class UiGraph3D extends UiWidget {
  final List<GraphNodeData> nodes;
  final List<GraphEdgeData> edges;
  const UiGraph3D({required this.nodes, required this.edges});
}

class UiDivider extends UiWidget {
  const UiDivider();
}

class UiIcon extends UiWidget {
  final String name;
  const UiIcon({required this.name});
}

class UiTextField extends UiWidget {
  final String label;
  final String value;
  final Map<String, dynamic>? onChanged;
  const UiTextField({required this.label, required this.value, this.onChanged});
}

class UiProgress extends UiWidget {
  final double value;
  final String? label;
  const UiProgress({required this.value, this.label});
}

class UiToggle extends UiWidget {
  final String label;
  final bool value;
  final Map<String, dynamic>? onChanged;
  const UiToggle({required this.label, required this.value, this.onChanged});
}

class UiImageWidget extends UiWidget {
  final String url;
  const UiImageWidget({required this.url});
}

class UiContainer extends UiWidget {
  final UiWidget child;
  final double padding;
  final String? decoration;
  const UiContainer({required this.child, this.padding = 0.0, this.decoration});
}

class UiWindowFrame extends UiWidget {
  final String title;
  final UiWidget content;
  final String windowId;
  final double x;
  final double y;
  final double width;
  final double height;
  final int zIndex;
  final String state;
  const UiWindowFrame({
    required this.title,
    required this.content,
    required this.windowId,
    required this.x,
    required this.y,
    required this.width,
    required this.height,
    required this.zIndex,
    required this.state,
  });
}

class UiBarChart extends UiWidget {
  final String title;
  final List<UiBar> bars;
  const UiBarChart({required this.title, required this.bars});
}

class UiBar {
  final String label;
  final double value;
  final String? color;
  const UiBar({required this.label, required this.value, this.color});
}

Widget buildFromUiWidget(
  UiWidget widget, {
  required BuildContext context,
  required void Function(Map<String, dynamic> synapseJson) onFire,
  UiWidget? mainContentOverride,
}) {
  final tokens = Theme.of(context).extension<LiquidGlassTokens>() ?? LiquidGlassTokens.fallback;
  switch (widget) {
    case UiText t:
      return Text(
        t.value,
        style: TextStyle(fontSize: 14, color: tokens.textColor),
      );

    case UiButton b:
      if (b.onTap == null) {
        return Padding(
          padding: EdgeInsets.symmetric(vertical: tokens.spacingSmall, horizontal: tokens.spacingTiny),
          child: Opacity(
            opacity: 0.5,
            child: ElevatedButton(
              onPressed: null,
              style: ElevatedButton.styleFrom(
                backgroundColor: tokens.buttonColor.withOpacity(0.3),
                foregroundColor: tokens.textColor.withOpacity(0.6),
              ),
              child: Text(b.label),
            ),
          ),
        );
      }
      return Padding(
        padding: EdgeInsets.symmetric(vertical: tokens.spacingSmall, horizontal: tokens.spacingTiny),
        child: ElevatedButton(
          onPressed: () => onFire(b.onTap!),
          style: ElevatedButton.styleFrom(
            backgroundColor: tokens.primaryColor,
            foregroundColor: Colors.black87,
            textStyle: const TextStyle(fontWeight: FontWeight.bold),
          ),
          child: Text(b.label),
        ),
      );

    case UiCard c:
      return Padding(
        padding: EdgeInsets.symmetric(vertical: tokens.spacingSmall),
        child: GFCard(
          content: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(c.title, style: TextStyle(fontWeight: FontWeight.w600, fontSize: 14, color: tokens.textColor)),
              SizedBox(height: tokens.spacingSmall),
              buildFromUiWidget(c.body, context: context, onFire: onFire, mainContentOverride: mainContentOverride),
            ],
          ),
        ),
      );

    case UiColumn col:
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: col.children
            .where((ch) => ch is! UiText || ch.value.isNotEmpty)
            .map((ch) => Padding(
                  padding: const EdgeInsets.symmetric(vertical: 2),
                  child: buildFromUiWidget(ch, context: context, onFire: onFire, mainContentOverride: mainContentOverride),
                ))
            .toList(),
      );

    case UiRow row:
      // Fluid responsive: use Wrap for small screens, Row for desktop (fluid layout idea)
      return LayoutBuilder(
        builder: (ctx, constraints) {
          if (constraints.maxWidth < 600) {
            return Wrap(
              spacing: tokens.spacingSmall,
              runSpacing: tokens.spacingSmall,
              children: row.children
                  .map((ch) => SizedBox(
                        width: constraints.maxWidth,
                        child: Padding(
                          padding: EdgeInsets.all(tokens.spacingTiny),
                          child: buildFromUiWidget(ch, context: context, onFire: onFire, mainContentOverride: mainContentOverride),
                        ),
                      ))
                  .toList(),
            );
          }
          return Row(
            children: row.children
                .map((ch) => Flexible(
                      child: Padding(
                        padding: EdgeInsets.symmetric(horizontal: tokens.spacingTiny),
                        child: buildFromUiWidget(ch, context: context, onFire: onFire, mainContentOverride: mainContentOverride),
                      ),
                    ))
                .toList(),
          );
        },
      );

    case UiMainPane mp:
      final contentToRender = mainContentOverride ?? mp.content;
      final mainPaneGlassBackground = tokens.cardColor.withOpacity(0.75);
      final mainPaneGlassBorder = Border.all(color: tokens.primaryColor.withOpacity(0.25), width: 1.5);
      final mainPaneHeaderBorder = Border(bottom: BorderSide(color: tokens.primaryColor.withOpacity(0.22), width: 1));
      return Padding(
        padding: const EdgeInsets.all(8),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(tokens.borderRadiusLarge),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: tokens.blurSigma, sigmaY: tokens.blurSigma),
            child: Container(
              decoration: BoxDecoration(
                color: mainPaneGlassBackground,
                borderRadius: BorderRadius.circular(tokens.borderRadiusLarge),
                border: mainPaneGlassBorder,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                    decoration: BoxDecoration(border: mainPaneHeaderBorder),
                    child: Text(
                      'MAIN CONTENT',
                      style: TextStyle(fontSize: 11, color: tokens.primaryColor, letterSpacing: 0.5),
                    ),
                  ),
                  Expanded(
                    child: CustomScrollView(
                      slivers: [
                        SliverPadding(
                          padding: const EdgeInsets.all(12),
                          sliver: SliverToBoxAdapter(
                            child: buildFromUiWidget(contentToRender, context: context, onFire: onFire, mainContentOverride: mainContentOverride),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      );

    case UiGraph3D g:
      return BrainGraph3DViewer(nodes: g.nodes, edges: g.edges, onFire: onFire);

    case UiMarkdown m:
      final markdownCodeBackground = tokens.cardColor.withOpacity(0.35);
      return MarkdownBody(
        data: m.value,
        selectable: true,
        styleSheet: MarkdownStyleSheet(
          p: TextStyle(fontSize: 13, color: tokens.textColor),
          strong: TextStyle(fontWeight: FontWeight.bold, color: tokens.textColor),
          code: TextStyle(fontFamily: 'monospace', backgroundColor: markdownCodeBackground, color: tokens.primaryColor),
        ),
      );

    case UiHyperlink h:
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: InkWell(
          onTap: () => onFire({'Type': 'OpenUrl', 'Url': h.url}),
          child: Text(
            '🔗 ${h.label}',
            style: TextStyle(color: tokens.primaryColor, decoration: TextDecoration.underline, fontSize: 13),
          ),
        ),
      );

    case UiDivider _:
      return Container(
        height: 1,
        margin: const EdgeInsets.symmetric(vertical: 8),
        decoration: BoxDecoration(
          gradient: LinearGradient(
            colors: [
              tokens.primaryColor.withOpacity(0.0),
              tokens.primaryColor.withOpacity(tokens.borderOpacity * 2),
              tokens.primaryColor.withOpacity(0.0),
            ],
          ),
        ),
      );

    case UiIcon icon:
      return Icon(
        _mapIcon(icon.name),
        color: tokens.primaryColor,
        size: 20,
      );

    case UiTextField tf:
      return GlassTextField(
        label: tf.label,
        value: tf.value,
        onChanged: tf.onChanged,
        onFire: onFire,
      );

    case UiProgress p:
      double pct = p.value;
      if (pct > 1.0) pct = pct / 100.0;
      pct = pct.clamp(0.0, 1.0);
      final progressTrack = tokens.cardColor.withOpacity(0.35);
      return Padding(
        padding: EdgeInsets.symmetric(vertical: tokens.spacingSmall, horizontal: tokens.spacingTiny),
        child: GFProgressBar(
          percentage: pct,
          backgroundColor: progressTrack,
          progressBarColor: tokens.primaryColor,
          progressHeadType: GFProgressHeadType.circular,
        ),
      );

    case UiToggle tg:
      return GFToggle(
        value: tg.value,
        onChanged: (val) {
          if (tg.onChanged != null) {
            // bind similar to before
            onFire(tg.onChanged!);
          }
        },
        enabledTrackColor: tokens.primaryColor.withOpacity(0.3),
        enabledThumbColor: tokens.primaryColor,
      );

    case UiImageWidget img:
      final imageGlassBorder = Border.all(color: Colors.white.withOpacity(tokens.borderOpacity));
      final imageLoadingPlaceholder = tokens.cardColor.withOpacity(0.12);
      final imageErrorPlaceholder = tokens.cardColor.withOpacity(0.12);
      final imageMutedText = tokens.textColor.withOpacity(0.6);
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 6, horizontal: 4),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
          child: Container(
            decoration: BoxDecoration(
              border: imageGlassBorder,
            ),
            child: Image.network(
              img.url,
              fit: BoxFit.cover,
              loadingBuilder: (context, child, loadingProgress) {
                if (loadingProgress == null) return child;
                return Container(
                  height: 150,
                  color: imageLoadingPlaceholder,
                  child: Center(
                    child: CircularProgressIndicator(color: tokens.primaryColor),
                  ),
                );
              },
              errorBuilder: (context, error, stackTrace) {
                return Container(
                  height: 100,
                  color: imageErrorPlaceholder,
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.broken_image_outlined, color: imageMutedText, size: 28),
                      const SizedBox(height: 4),
                      Text(img.url, style: TextStyle(fontSize: 10, color: imageMutedText), overflow: TextOverflow.ellipsis),
                    ],
                  ),
                );
              },
            ),
          ),
        ),
      );

    case UiContainer ctr:
      Widget childWidget = buildFromUiWidget(ctr.child, context: context, onFire: onFire);
      Decoration? boxDeco;
      if (ctr.decoration != null && ctr.decoration!.isNotEmpty) {
        if (ctr.decoration == 'glass') {
          final glassFill = tokens.cardColor.withOpacity(tokens.backgroundOpacity);
          final glassBorder = Border.all(color: Colors.white.withOpacity(tokens.borderOpacity));
          boxDeco = BoxDecoration(
            color: glassFill,
            borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
            border: glassBorder,
          );
          childWidget = ClipRRect(
            borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
            child: BackdropFilter(
              filter: ImageFilter.blur(sigmaX: tokens.blurSigma, sigmaY: tokens.blurSigma),
              child: Container(
                decoration: boxDeco,
                padding: EdgeInsets.all(ctr.padding),
                child: childWidget,
              ),
            ),
          );
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: 4),
            child: childWidget,
          );
        }
      }
      return Container(
        padding: EdgeInsets.all(ctr.padding),
        decoration: boxDeco,
        child: childWidget,
      );

    case UiWindowFrame wf:
      final windowFrameFill = tokens.cardColor.withOpacity(tokens.backgroundOpacity);
      final windowFrameBorder = Border.all(color: tokens.primaryColor.withOpacity(0.3));
      final windowTitleBarBackground = tokens.cardColor.withOpacity(0.12);
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 6),
        child: Container(
          decoration: BoxDecoration(
            color: windowFrameFill,
            borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
            border: windowFrameBorder,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                color: windowTitleBarBackground,
                child: Text(wf.title, style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: tokens.textColor)),
              ),
              Padding(
                padding: const EdgeInsets.all(12),
                child: buildFromUiWidget(wf.content, context: context, onFire: onFire),
              ),
            ],
          ),
        ),
      );

    case UiBarChart bc:
      return _buildBarChart(bc, tokens);
  }
}

Widget _buildBarChart(UiBarChart bc, LiquidGlassTokens tokens) {
  if (bc.bars.isEmpty) {
    return Text('No data for ${bc.title}', style: TextStyle(color: tokens.textColor.withOpacity(0.6)));
  }
  final maxVal = bc.bars.map((b) => b.value).reduce((a, b) => a > b ? a : b);
  return Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(bc.title, style: TextStyle(fontWeight: FontWeight.w600, fontSize: 14, color: tokens.textColor)),
      const SizedBox(height: 8),
      ...bc.bars.map((bar) {
        final pct = maxVal > 0 ? (bar.value / maxVal).clamp(0.0, 1.0) : 0.0;
        final barColor = bar.color != null
            ? Color(int.parse(bar.color!.replaceFirst('#', '0xff')))
            : tokens.primaryColor;
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 3),
          child: Row(
            children: [
              SizedBox(
                width: 140,
                child: Text(bar.label, style: TextStyle(fontSize: 11, color: tokens.textColor), overflow: TextOverflow.ellipsis),
              ),
              Expanded(
                child: Container(
                  height: 16,
                  decoration: BoxDecoration(
                    color: tokens.cardColor.withOpacity(0.3),
                    borderRadius: BorderRadius.circular(3),
                  ),
                  child: FractionallySizedBox(
                    alignment: Alignment.centerLeft,
                    widthFactor: pct,
                    child: Container(
                      decoration: BoxDecoration(
                        color: barColor,
                        borderRadius: BorderRadius.circular(3),
                      ),
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 6),
              Text(bar.value.toStringAsFixed(0), style: TextStyle(fontSize: 11, color: tokens.textColor)),
            ],
          ),
        );
      }).toList(),
    ],
  );
}

class BrainGraph3DViewer extends StatefulWidget {
  final List<GraphNodeData> nodes;
  final List<GraphEdgeData> edges;
  final void Function(Map<String, dynamic> synapseJson) onFire;
  const BrainGraph3DViewer({required this.nodes, required this.edges, required this.onFire});

  @override
  State<BrainGraph3DViewer> createState() => _BrainGraph3DViewerState();
}

class _BrainGraph3DViewerState extends State<BrainGraph3DViewer> {
  double _yaw = 0.5;
  double _pitch = 0.3;
  Offset? _lastFocal;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onPanStart: (d) => _lastFocal = d.localPosition,
      onPanUpdate: (d) {
        final prev = _lastFocal ?? d.localPosition;
        setState(() {
          _yaw += (d.localPosition.dx - prev.dx) * 0.01;
          _pitch += (d.localPosition.dy - prev.dy) * 0.01;
          _pitch = _pitch.clamp(-1.2, 1.2);
          _lastFocal = d.localPosition;
        });
      },
      onPanEnd: (_) => _lastFocal = null,
      child: Container(
        height: 420,
        decoration: BoxDecoration(
          color: LiquidGlassTokens.fallback.backgroundColor,
          border: Border.all(color: LiquidGlassTokens.fallback.primaryColor.withOpacity(0.4)),
        ),
        child: CustomPaint(
          painter: _BrainGraphPainter(
            nodes: widget.nodes.isEmpty ? _defaultNodes() : widget.nodes,
            edges: widget.edges.isEmpty ? _defaultEdges() : widget.edges,
            yaw: _yaw,
            pitch: _pitch,
          ),
          child: Center(
            child: Text('Brain Graph 3D (pan to rotate) • neurons & synapses', style: TextStyle(color: LiquidGlassTokens.fallback.textColor.withOpacity(0.35), fontSize: 11)),
          ),
        ),
      ),
    );
  }

  List<GraphNodeData> _defaultNodes() => const [
    GraphNodeData('shell', 'shell', 'core'),
    GraphNodeData('kerneltasks', 'kerneltasks', 'tasks'),
    GraphNodeData('marketplace', 'marketplace', 'market'),
    GraphNodeData('creator', 'creator', 'create'),
    GraphNodeData('llm-agent', 'llm-agent', 'agent'),
    GraphNodeData('memory', 'memory', 'mem'),
    GraphNodeData('brain-graph', 'brain-graph', 'viz'),
    GraphNodeData('google-auth', 'google-auth', 'auth'),
  ];

  List<GraphEdgeData> _defaultEdges() => const [
    GraphEdgeData('shell', 'kerneltasks', 'handles'),
    GraphEdgeData('shell', 'marketplace', 'composes'),
    GraphEdgeData('marketplace', 'creator', 'installs'),
    GraphEdgeData('shell', 'llm-agent', 'routes'),
    GraphEdgeData('llm-agent', 'memory', 'recalls'),
    GraphEdgeData('shell', 'brain-graph', 'emits'),
  ];
}

class _BrainGraphPainter extends CustomPainter {
  final List<GraphNodeData> nodes;
  final List<GraphEdgeData> edges;
  final double yaw;
  final double pitch;

  _BrainGraphPainter({required this.nodes, required this.edges, required this.yaw, required this.pitch});

  @override
  void paint(Canvas canvas, Size size) {
    final cx = size.width / 2;
    final cy = size.height / 2;
    final scale = 110.0;

    final pts3 = <String, List<double>>{};
    for (int i = 0; i < nodes.length; i++) {
      final n = nodes[i];
      final a = i * 1.0 / nodes.length * 6.28;
      final r = 1.0;
      final z = (i % 3 - 1) * 0.6;
      pts3[n.id] = [r * math.cos(a), r * math.sin(a) * 0.6 + z * 0.3, z];
    }

    final proj = <String, Offset>{};
    final zlist = <String, double>{};
    for (final e in pts3.entries) {
      final p = e.value;
      final x = p[0];
      final y = p[1];
      final z = p[2];
      final x1 = x * math.cos(yaw) - z * math.sin(yaw);
      final z1 = x * math.sin(yaw) + z * math.cos(yaw);
      final y2 = y * math.cos(pitch) - z1 * math.sin(pitch);
      final z2 = y * math.sin(pitch) + z1 * math.cos(pitch);
      proj[e.key] = Offset(cx + x1 * scale, cy + y2 * scale);
      zlist[e.key] = z2;
    }

    final edgePaint = Paint()..color = LiquidGlassTokens.fallback.secondaryColor.withOpacity(0.6)..strokeWidth = 1.2;
    for (final e in edges) {
      final a = proj[e.sourceId];
      final b = proj[e.targetId];
      if (a != null && b != null) canvas.drawLine(a, b, edgePaint);
    }

    final nodePaint = Paint()..color = LiquidGlassTokens.fallback.primaryColor;
    final textStyle = TextStyle(color: LiquidGlassTokens.fallback.textColor, fontSize: 10);
    for (final n in nodes) {
      final p = proj[n.id];
      final z = zlist[n.id] ?? 0;
      if (p == null) continue;
      final r = 7.0 + (z + 1.5) * 2.5;
      canvas.drawCircle(p, r, nodePaint);
      final tp = TextPainter(text: TextSpan(text: n.label, style: textStyle), textDirection: TextDirection.ltr);
      tp.layout();
      tp.paint(canvas, p.translate(-tp.width / 2, r + 2));
    }
  }

  @override
  bool shouldRepaint(covariant _BrainGraphPainter old) => old.yaw != yaw || old.pitch != pitch || old.nodes.length != nodes.length;
}

IconData _mapIcon(String name) {
  final n = name.toLowerCase().trim();
  if (n.contains('home')) return Icons.home_outlined;
  if (n.contains('task') || n.contains('check')) return Icons.check_circle_outline;
  if (n.contains('market') || n.contains('shop') || n.contains('cart')) return Icons.shopping_cart_outlined;
  if (n.contains('create') || n.contains('pencil') || n.contains('edit')) return Icons.edit_note_outlined;
  if (n.contains('mail') || n.contains('email') || n.contains('envelope')) return Icons.mail_outline;
  if (n.contains('weather') || n.contains('sun') || n.contains('cloud')) return Icons.wb_sunny_outlined;
  if (n.contains('setting') || n.contains('gear')) return Icons.settings_outlined;
  if (n.contains('lock') || n.contains('auth') || n.contains('key')) return Icons.lock_open_outlined;
  if (n.contains('search')) return Icons.search;
  if (n.contains('close') || n.contains('dismiss')) return Icons.close;
  if (n.contains('person') || n.contains('user')) return Icons.person_outline;
  if (n.contains('arrow_back') || n.contains('back')) return Icons.arrow_back;
  if (n.contains('arrow_forward') || n.contains('forward')) return Icons.arrow_forward;
  if (n.contains('info')) return Icons.info_outline;
  if (n.contains('alarm')) return Icons.alarm;
  return Icons.widgets_outlined;
}

Map<String, dynamic> _bindSynapseValue(Map<String, dynamic> json, String textValue) {
  final copy = Map<String, dynamic>.from(json);
  bool replaced = false;
  for (final key in copy.keys) {
    final val = copy[key];
    if (val is String && (val == '\$value' || val == '\$Value')) {
      copy[key] = textValue;
      replaced = true;
    } else if (val is Map<String, dynamic>) {
      copy[key] = _bindSynapseValue(val, textValue);
      replaced = true;
    }
  }
  if (!replaced) {
    copy['Value'] = textValue;
  }
  return copy;
}

Map<String, dynamic> _bindSynapseBool(Map<String, dynamic> json, bool boolValue) {
  final copy = Map<String, dynamic>.from(json);
  bool replaced = false;
  for (final key in copy.keys) {
    final val = copy[key];
    if (val is String && (val == '\$value' || val == '\$Value')) {
      copy[key] = boolValue;
      replaced = true;
    } else if (val is Map<String, dynamic>) {
      copy[key] = _bindSynapseBool(val, boolValue);
      replaced = true;
    }
  }
  if (!replaced) {
    copy['Value'] = boolValue;
  }
  return copy;
}

class GlassTextField extends StatefulWidget {
  final String label;
  final String value;
  final Map<String, dynamic>? onChanged;
  final void Function(Map<String, dynamic> synapseJson) onFire;

  const GlassTextField({
    required this.label,
    required this.value,
    this.onChanged,
    required this.onFire,
  });

  @override
  State<GlassTextField> createState() => _GlassTextFieldState();
}

class _GlassTextFieldState extends State<GlassTextField> {
  late final TextEditingController _controller;
  bool _isFocused = false;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController(text: widget.value);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _submit(String text) {
    if (widget.onChanged != null) {
      final bound = _bindSynapseValue(widget.onChanged!, text);
      widget.onFire(bound);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tokens = Theme.of(context).extension<LiquidGlassTokens>() ?? LiquidGlassTokens.fallback;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6, horizontal: 4),
      child: Focus(
        onFocusChange: (focused) => setState(() => _isFocused = focused),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: tokens.blurSigma, sigmaY: tokens.blurSigma),
            child: Container(
              decoration: BoxDecoration(
                color: tokens.cardColor.withOpacity(tokens.backgroundOpacity),
                borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
                border: Border.all(
                  color: _isFocused
                      ? tokens.primaryColor
                      : Colors.white.withOpacity(tokens.borderOpacity),
                  width: 1.2,
                ),
              ),
              child: TextField(
                controller: _controller,
                onSubmitted: _submit,
                style: TextStyle(color: tokens.textColor, fontSize: 14),
                decoration: InputDecoration(
                  labelText: widget.label.isNotEmpty ? widget.label : null,
                  labelStyle: TextStyle(
                    color: _isFocused ? tokens.primaryColor : tokens.textColor.withOpacity(0.5),
                    fontSize: 12,
                  ),
                  contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  border: InputBorder.none,
                  suffixIcon: widget.onChanged != null
                      ? IconButton(
                          icon: Icon(Icons.send, color: tokens.primaryColor, size: 18),
                          onPressed: () => _submit(_controller.text),
                        )
                      : null,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class GlassToggle extends StatefulWidget {
  final String label;
  final bool value;
  final Map<String, dynamic>? onChanged;
  final void Function(Map<String, dynamic> synapseJson) onFire;

  const GlassToggle({
    required this.label,
    required this.value,
    this.onChanged,
    required this.onFire,
  });

  @override
  State<GlassToggle> createState() => _GlassToggleState();
}

class _GlassToggleState extends State<GlassToggle> {
  late bool _currentValue;

  @override
  void initState() {
    super.initState();
    _currentValue = widget.value;
  }

  @override
  void didUpdateWidget(GlassToggle oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.value != widget.value) {
      _currentValue = widget.value;
    }
  }

  void _onChanged(bool val) {
    setState(() => _currentValue = val);
    if (widget.onChanged != null) {
      final bound = _bindSynapseBool(widget.onChanged!, val);
      widget.onFire(bound);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tokens = Theme.of(context).extension<LiquidGlassTokens>() ?? LiquidGlassTokens.fallback;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4, horizontal: 4),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: tokens.blurSigma, sigmaY: tokens.blurSigma),
          child: Container(
            decoration: BoxDecoration(
              color: tokens.cardColor.withOpacity(tokens.backgroundOpacity),
              borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
              border: Border.all(color: Colors.white.withOpacity(tokens.borderOpacity)),
            ),
            child: SwitchListTile(
              title: Text(
                widget.label,
                style: TextStyle(fontSize: 14, color: tokens.textColor),
              ),
              value: _currentValue,
              onChanged: _onChanged,
              activeColor: tokens.primaryColor,
              activeTrackColor: tokens.primaryColor.withOpacity(0.3),
              inactiveThumbColor: tokens.textColor.withOpacity(0.4),
              inactiveTrackColor: tokens.cardColor.withOpacity(0.2),
            ),
          ),
        ),
      ),
    );
  }
}
