import 'package:flutter/material.dart';

Future<void> showAppSource(
  BuildContext context,
  List<String> paths,
  Map<String, String> files,
  String? revision,
) => showModalBottomSheet<void>(
  context: context,
  showDragHandle: true,
  isScrollControlled: true,
  builder: (context) => SafeArea(
    child: SizedBox(
      height: MediaQuery.sizeOf(context).height * .7,
      child: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          Text('Source', style: Theme.of(context).textTheme.titleLarge),
          if (revision != null) Text('Revision $revision'),
          if (paths.isEmpty)
            const Padding(
              padding: EdgeInsets.only(top: 24),
              child: Text('Source has not been linked to this behavior yet.'),
            ),
          for (final path in paths) ...[
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 16),
              child: Text(path, style: Theme.of(context).textTheme.titleSmall),
            ),
            SelectableText(
              files[path] ??
                  'This source is not available in the selected revision.',
              style: const TextStyle(fontFamily: 'monospace'),
            ),
          ],
        ],
      ),
    ),
  ),
);
