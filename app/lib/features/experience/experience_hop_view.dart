import 'package:flutter/widgets.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'package:digitalbrain_flutter/rfw_host/inline_rfw_surface.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

/// Renders one experience hop full-screen. The semantics identifier is the hop's [correlationId]
/// (== the pack's surfaceId), which the browser E2E asserts via `flt-semantics-identifier`.
class ExperienceHopView extends StatelessWidget {
  const ExperienceHopView({
    super.key,
    required this.host,
    required this.data,
    required this.correlationId,
    required this.onEvent,
    this.rootWidget = 'root',
  });

  final RfwRuntimeHost host;
  final Map<String, Object?> data;
  final String correlationId;
  final RemoteEventHandler onEvent;
  final String rootWidget;

  @override
  Widget build(BuildContext context) {
    final tree = data['tree'];
    if (tree is Map) {
      return Semantics(
        identifier: correlationId,
        container: true,
        child: UiSurfaceTreeRenderer().build(
          tree.cast<String, Object?>(),
          onEvent,
          rfwHost: host,
        ),
      );
    }

    final body = buildInlineRfwSurface(
      host: host,
      data: data,
      fallbackKey: correlationId,
      defaultRootWidget: rootWidget,
      onEvent: onEvent,
      correlationId: correlationId,
      semanticsId: correlationId,
    );
    return body ?? const SizedBox.shrink();
  }
}
