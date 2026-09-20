import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

class InboxBanner extends StatefulWidget {
  const InboxBanner({super.key, required this.client});

  final DigitalBrainUiClient client;

  @override
  State<InboxBanner> createState() => _InboxBannerState();
}

class _InboxBannerState extends State<InboxBanner> {
  static const liveInstance = 'e2e';
  Timer? _poll;
  List<String> _lines = const [];
  UiChartPart? _chart;
  UiWebBrowserPart? _browser;
  UiVideoPart? _video;
  UiExpanderPart? _expander;
  UiButtonPart? _button;

  @override
  void initState() {
    super.initState();
    unawaited(_refresh());
    _poll = Timer.periodic(const Duration(seconds: 2), (_) => _refresh());
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  Future<void> _refresh() async {
    try {
      final lines = await widget.client.readInbox();
      UiChartPart? chart;
      UiWebBrowserPart? browser;
      UiVideoPart? video;
      UiExpanderPart? expander;
      UiButtonPart? button;
      try {
        final chartJson = await widget.client.readUi('charts', liveInstance);
        final title = chartJson['title'] as String? ?? '';
        if (title.isNotEmpty) {
          chart = UiChartPart.fromMetadata(chartJson);
        }
      } catch (_) {}
      try {
        final browserJson = await widget.client.readUi('browsers', liveInstance);
        final uri = browserJson['uri'] as String? ?? '';
        if (uri.isNotEmpty) {
          browser = UiWebBrowserPart.fromMetadata(browserJson);
        }
      } catch (_) {}
      try {
        final videoJson = await widget.client.readUi('videos', liveInstance);
        final url = videoJson['url'] as String? ?? '';
        if (url.isNotEmpty) {
          video = UiVideoPart.fromMetadata(videoJson);
        }
      } catch (_) {}
      try {
        final expanderJson = await widget.client.readUi('expanders', liveInstance);
        final header = expanderJson['header'] as String? ?? '';
        if (header.isNotEmpty) {
          expander = UiExpanderPart.fromMetadata(expanderJson);
        }
      } catch (_) {}
      try {
        final buttonJson = await widget.client.readUi('buttons', liveInstance);
        final label = buttonJson['label'] as String? ?? '';
        if (label.isNotEmpty) {
          button = UiButtonPart(
            buttonId: buttonJson['name'] as String? ?? liveInstance,
            label: label,
            action: buttonJson['action'] as String? ?? '',
          );
        }
      } catch (_) {}
      if (!mounted) return;
      setState(() {
        _lines = lines;
        _chart = chart;
        _browser = browser;
        _video = video;
        _expander = expander;
        _button = button;
      });
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    if (_lines.isEmpty &&
        _chart == null &&
        _browser == null &&
        _video == null &&
        _expander == null &&
        _button == null) {
      return const SizedBox.shrink();
    }
    return Material(
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            for (final line in _lines)
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Text(line),
              ),
            if (_chart != null) UiChart(part: _chart!),
            if (_browser != null) UiWebBrowser(part: _browser!),
            if (_video != null) UiVideo(part: _video!),
            if (_expander != null)
              UiExpander(
                part: _expander!,
                onToggle: () => unawaited(
                  widget.client.postUi('/ui/expanders/$liveInstance/toggle'),
                ),
              ),
            if (_button != null)
              UiButton(
                part: _button!,
                onPressed: (_) => unawaited(
                  widget.client.postUi('/ui/buttons/$liveInstance/click'),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
