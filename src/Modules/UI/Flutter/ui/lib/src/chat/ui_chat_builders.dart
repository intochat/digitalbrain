import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';

import '../components/button/ui_button.dart';
import '../components/card/ui_card.dart';
import '../components/chart/ui_chart.dart';
import '../components/graph/graph_models.dart';
import '../components/graph/graph_scene.dart';
import '../components/graph/ui_graph_controller.dart';
import '../components/graph/ui_graph_navigator.dart';
import '../components/graph/ui_graph_view.dart';
import '../components/clock/ui_clock.dart';
import '../components/image/ui_image.dart';
import '../components/sheet/ui_sheet.dart';
import '../components/table/ui_data_table.dart';
import '../components/table/ui_table_controller.dart';
import '../models/ui_part.dart';
import '../theme/ui_theme.dart';
import 'ui_copyable_message.dart';

typedef UiButtonPressed = void Function(UiButtonPart part);
typedef UiChartRefReader = Future<ChatChartOffer?> Function(String name);
typedef UiImageRefReader = Future<Uint8List?> Function(String name);
typedef UiSheetRefReader = Future<ChatSpreadsheetOffer?> Function(String name);
typedef UiGraphRefReader = Future<ChatGraphOffer?> Function(String name);

/// Flyer Chat [Builders] helpers for DigitalBrain ui components.
///
/// Official extension point: `Builders.customMessageBuilder` receives
/// [CustomMessage]; payload lives in `message.metadata` (kind-discriminated).
/// Docs: https://pub.dev/packages/flutter_chat_ui
abstract final class UiChatBuilders {
  static Widget customMessageBuilder(
    BuildContext context,
    CustomMessage message,
    int index, {
    required bool isSentByMe,
    MessageGroupStatus? groupStatus,
    UiButtonPressed? onButtonPressed,
    UiChartRefReader? onReadChart,
    UiImageRefReader? onReadImageBytes,
    UiSheetRefReader? onReadSpreadsheet,
    UiGraphRefReader? onReadGraph,
    ReadTable? onReadTable,
    UpdateTableView? onUpdateTableView,
    GraphSceneFactory? graphSceneFactory,
  }) {
    final part = UiPart.tryParse(
      message.metadata == null
          ? null
          : Map<String, dynamic>.from(message.metadata!),
    );

    if (part == null) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 4),
        child: Text(
          'Unsupported ui message',
          style: UiType.bodyMuted,
          key: Key('ui_custom_unsupported'),
        ),
      );
    }

    return UiCopyableMessage(
      copyText: (_) => part.copyText,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: switch (part) {
          UiButtonPart(:final buttonId) => UiButton(
            key: Key('chat_ui_button_$buttonId'),
            part: part,
            dense: true,
            onPressed: onButtonPressed,
          ),
          UiChartPart() => UiChart(part: part, height: 180),
          UiCardPart() => UiCard(part: part),
          UiTimerPart() => UiClock(part: part),
          UiChartRefPart(:final name, :final caption) => _UiChartRefLoader(
            name: name,
            caption: caption,
            reader: onReadChart,
          ),
          UiImageRefPart(:final name, :final caption) => _UiImageRefLoader(
            name: name,
            caption: caption,
            reader: onReadImageBytes,
          ),
          UiSheetPart() => UiSheet(part: part),
          UiSheetRefPart(:final name, :final caption) => _UiSheetRefLoader(
            name: name,
            caption: caption,
            reader: onReadSpreadsheet,
          ),
          UiGraphRefPart(:final name, :final caption) => _UiGraphRefLoader(
            name: name,
            caption: caption,
            reader: onReadGraph,
            sceneFactory: graphSceneFactory,
          ),
          UiTableRefPart(:final name, :final caption) => _UiTableRefLoader(
            name: name,
            caption: caption,
            read: onReadTable,
            update: onUpdateTableView,
          ),
        },
      ),
    );
  }

  /// Wraps text, stream, and custom bubbles with selection + copy.
  static Builders withCopy(Builders? host) {
    final base = host ?? const Builders();
    final text = base.textMessageBuilder;
    final stream = base.textStreamMessageBuilder;
    final custom = base.customMessageBuilder;
    var builders = base.copyWith(
      textMessageBuilder:
          (
            context,
            message,
            index, {
            required bool isSentByMe,
            MessageGroupStatus? groupStatus,
          }) {
            final child =
                text?.call(
                  context,
                  message,
                  index,
                  isSentByMe: isSentByMe,
                  groupStatus: groupStatus,
                ) ??
                SimpleTextMessage(message: message, index: index);
            return UiCopyableMessage(
              copyText: (_) => message.text,
              child: child,
            );
          },
    );
    if (stream != null) {
      builders = builders.copyWith(
        textStreamMessageBuilder:
            (
              context,
              message,
              index, {
              required bool isSentByMe,
              MessageGroupStatus? groupStatus,
            }) {
              return UiCopyableMessage(
                copyText: (context) =>
                    UiStreamCopy.streamText(context, message.streamId),
                child: stream(
                  context,
                  message,
                  index,
                  isSentByMe: isSentByMe,
                  groupStatus: groupStatus,
                ),
              );
            },
      );
    }
    if (custom != null) {
      builders = builders.copyWith(
        customMessageBuilder:
            (
              context,
              message,
              index, {
              required bool isSentByMe,
              MessageGroupStatus? groupStatus,
            }) {
              final child = custom(
                context,
                message,
                index,
                isSentByMe: isSentByMe,
                groupStatus: groupStatus,
              );
              if (child is UiCopyableMessage) {
                return child;
              }
              final part = UiPart.tryParse(
                message.metadata == null
                    ? null
                    : Map<String, dynamic>.from(message.metadata!),
              );
              final copy = part?.copyText ?? '';
              if (copy.trim().isEmpty) {
                return child;
              }
              return UiCopyableMessage(copyText: (_) => copy, child: child);
            },
      );
    }
    return builders;
  }

  /// Drop-in partial [Builders] for chat surfaces that only need ui customs.
  static Builders uiCustoms({
    UiButtonPressed? onButtonPressed,
    UiChartRefReader? onReadChart,
    UiImageRefReader? onReadImageBytes,
    UiSheetRefReader? onReadSpreadsheet,
    UiGraphRefReader? onReadGraph,
    ReadTable? onReadTable,
    UpdateTableView? onUpdateTableView,
  }) {
    return Builders(
      customMessageBuilder:
          (
            context,
            message,
            index, {
            required bool isSentByMe,
            MessageGroupStatus? groupStatus,
          }) => customMessageBuilder(
            context,
            message,
            index,
            isSentByMe: isSentByMe,
            groupStatus: groupStatus,
            onButtonPressed: onButtonPressed,
            onReadChart: onReadChart,
            onReadImageBytes: onReadImageBytes,
            onReadSpreadsheet: onReadSpreadsheet,
            onReadGraph: onReadGraph,
            onReadTable: onReadTable,
            onUpdateTableView: onUpdateTableView,
          ),
    );
  }
}

final class _UiChartRefLoader extends StatefulWidget {
  const _UiChartRefLoader({
    required this.name,
    required this.caption,
    required this.reader,
  });

  final String name;
  final String caption;
  final UiChartRefReader? reader;

  @override
  State<_UiChartRefLoader> createState() => _UiChartRefLoaderState();
}

final class _UiChartRefLoaderState extends State<_UiChartRefLoader> {
  Future<ChatChartOffer?>? _fetch;

  @override
  void initState() {
    super.initState();
    final reader = widget.reader;
    if (reader != null) {
      _fetch = reader(widget.name);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (widget.reader == null) {
      return Text(
        widget.caption,
        key: Key('ui_chart_ref_offline_${widget.name}'),
        style: UiType.bodyMuted,
      );
    }

    return FutureBuilder<ChatChartOffer?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('ui_chart_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final offer = snapshot.data;
        if (offer == null) {
          return Text(
            widget.caption,
            key: Key('ui_chart_ref_missing_${widget.name}'),
            style: UiType.bodyMuted,
          );
        }

        return UiChart(
          part: UiChartPart(
            title: offer.title,
            points: [
              for (final point in offer.points)
                UiChartPoint(label: point.label, value: point.value),
            ],
            chartKind: offer.chartKind,
          ),
          height: 180,
        );
      },
    );
  }
}

final class _UiImageRefLoader extends StatefulWidget {
  const _UiImageRefLoader({
    required this.name,
    required this.caption,
    required this.reader,
  });

  final String name;
  final String caption;
  final UiImageRefReader? reader;

  @override
  State<_UiImageRefLoader> createState() => _UiImageRefLoaderState();
}

final class _UiImageRefLoaderState extends State<_UiImageRefLoader> {
  Future<Uint8List?>? _fetch;

  @override
  void initState() {
    super.initState();
    final reader = widget.reader;
    if (reader != null) {
      _fetch = reader(widget.name);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (widget.reader == null) {
      return Text(
        widget.caption,
        key: Key('ui_image_ref_offline_${widget.name}'),
        style: UiType.bodyMuted,
      );
    }

    return FutureBuilder<Uint8List?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('ui_image_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final bytes = snapshot.data;
        if (bytes == null) {
          return Text(
            widget.caption,
            key: Key('ui_image_ref_missing_${widget.name}'),
            style: UiType.bodyMuted,
          );
        }

        return UiImage(bytes: bytes, caption: widget.caption);
      },
    );
  }
}

final class _UiSheetRefLoader extends StatefulWidget {
  const _UiSheetRefLoader({
    required this.name,
    required this.caption,
    required this.reader,
  });

  final String name;
  final String caption;
  final UiSheetRefReader? reader;

  @override
  State<_UiSheetRefLoader> createState() => _UiSheetRefLoaderState();
}

final class _UiSheetRefLoaderState extends State<_UiSheetRefLoader> {
  Future<ChatSpreadsheetOffer?>? _fetch;

  @override
  void initState() {
    super.initState();
    final reader = widget.reader;
    if (reader != null) {
      _fetch = reader(widget.name);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (widget.reader == null) {
      return Text(
        widget.caption,
        key: Key('ui_sheet_ref_offline_${widget.name}'),
        style: UiType.bodyMuted,
      );
    }

    return FutureBuilder<ChatSpreadsheetOffer?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('ui_sheet_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final offer = snapshot.data;
        if (offer == null) {
          return Text(
            widget.caption,
            key: Key('ui_sheet_ref_missing_${widget.name}'),
            style: UiType.bodyMuted,
          );
        }

        return UiSheet(
          part: UiSheetPart(
            title: offer.title,
            sheetName: offer.sheetName,
            columns: offer.columns,
            rows: offer.rows,
          ),
        );
      },
    );
  }
}

/// Fetches a named graph entity, then renders it as a navigable 3D graph.
///
/// The controller is created once and disposed with the widget so the camera
/// and navigation history survive rebuilds.
final class _UiGraphRefLoader extends StatefulWidget {
  const _UiGraphRefLoader({
    required this.name,
    required this.caption,
    required this.reader,
    this.sceneFactory,
  });

  final String name;
  final String caption;
  final UiGraphRefReader? reader;
  final GraphSceneFactory? sceneFactory;

  @override
  State<_UiGraphRefLoader> createState() => _UiGraphRefLoaderState();
}

final class _UiGraphRefLoaderState extends State<_UiGraphRefLoader> {
  Future<ChatGraphOffer?>? _fetch;
  UiGraphController? _controller;

  @override
  void initState() {
    super.initState();
    final reader = widget.reader;
    if (reader != null) {
      _fetch = reader(widget.name);
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  UiGraphController _controllerFor(ChatGraphOffer offer) {
    final nodes = [
      for (final node in offer.nodes)
        GraphNode(
          id: node.id,
          label: node.label,
          kind: switch (node.kind) {
            'hub' => GraphNodeKind.hub,
            'entity' => GraphNodeKind.entity,
            'module' => GraphNodeKind.module,
            _ => GraphNodeKind.leaf,
          },
          cluster: node.cluster,
        ),
    ];
    final edges = [
      for (final edge in offer.edges)
        GraphEdge(
          id: edge.id,
          sourceId: edge.sourceId,
          targetId: edge.targetId,
          dotted: edge.dotted,
        ),
    ];

    final existing = _controller;
    if (existing != null) {
      existing.setGraph(nodes: nodes, edges: edges);
      return existing;
    }
    return _controller = UiGraphController(nodes: nodes, edges: edges);
  }

  @override
  Widget build(BuildContext context) {
    if (widget.reader == null) {
      return Text(
        widget.caption,
        key: Key('ui_graph_ref_offline_${widget.name}'),
        style: UiType.bodyMuted,
      );
    }

    return FutureBuilder<ChatGraphOffer?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('ui_graph_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final offer = snapshot.data;
        if (offer == null) {
          return Text(
            widget.caption,
            key: Key('ui_graph_ref_missing_${widget.name}'),
            style: UiType.bodyMuted,
          );
        }

        final controller = _controllerFor(offer);
        return SizedBox(
          height: 360,
          child: Column(
            children: [
              Expanded(
                child: UiGraphView(
                  controller: controller,
                  sceneFactory: widget.sceneFactory,
                ),
              ),
              UiGraphNavigator(controller: controller),
            ],
          ),
        );
      },
    );
  }
}

/// Reads a named table on display and renders it as the same server-paged
/// [UiDataTable] the workspace uses, so filters, sort and paging round-trip
/// through /ui/tables/{name}. A later offer for the same name re-reads it.
final class _UiTableRefLoader extends StatefulWidget {
  const _UiTableRefLoader({
    required this.name,
    required this.caption,
    required this.read,
    required this.update,
  });

  final String name;
  final String caption;
  final ReadTable? read;
  final UpdateTableView? update;

  @override
  State<_UiTableRefLoader> createState() => _UiTableRefLoaderState();
}

final class _UiTableRefLoaderState extends State<_UiTableRefLoader> {
  Future<TableSnapshot?>? _fetch;
  UiTableController? _controller;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(covariant _UiTableRefLoader oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.name != widget.name) {
      _controller?.dispose();
      _controller = null;
      _load();
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  void _load() {
    final reader = widget.read;
    if (reader != null) {
      _fetch = _read(reader);
    }
  }

  Future<TableSnapshot?> _read(ReadTable reader) async {
    try {
      return await reader(widget.name);
    } on TableRequestException {
      return null;
    }
  }

  UiTableController _controllerFor(TableSnapshot snapshot) {
    final existing = _controller;
    if (existing != null) {
      existing.accept(snapshot);
      return existing;
    }
    return _controller = UiTableController(
      snapshot: snapshot,
      read: widget.read,
      update: widget.update,
    );
  }

  @override
  Widget build(BuildContext context) {
    if (widget.read == null) {
      return Text(
        widget.caption,
        key: Key('ui_table_ref_offline_${widget.name}'),
        style: UiType.bodyMuted,
      );
    }

    return FutureBuilder<TableSnapshot?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('ui_table_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final table = snapshot.data;
        if (table == null) {
          return Text(
            widget.caption,
            key: Key('ui_table_ref_missing_${widget.name}'),
            style: UiType.bodyMuted,
          );
        }

        return UiDataTable(controller: _controllerFor(table), showTitle: true);
      },
    );
  }
}
