import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

class InboxBanner extends StatefulWidget {
  const InboxBanner({super.key, required this.client});

  final DigitalBrainUiClient client;

  @override
  State<InboxBanner> createState() => _InboxBannerState();
}

class _InboxBannerState extends State<InboxBanner> {
  Timer? _poll;
  List<String> _lines = const [];

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
      if (!mounted) return;
      setState(() => _lines = lines);
    } catch (_) {
      // Kernel may be down; keep last lines.
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_lines.isEmpty) return const SizedBox.shrink();
    return Material(
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        children: [
          for (final line in _lines)
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Text(line),
            ),
        ],
      ),
    );
  }
}
