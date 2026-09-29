import 'package:flutter/material.dart';

class EmbeddedBrowser extends StatelessWidget {
  const EmbeddedBrowser({
    super.key,
    required this.onConnect,
    required this.onDisconnect,
  });
  final Future<void> Function(int, String)? onConnect;
  final Future<void> Function(String)? onDisconnect;

  @override
  Widget build(BuildContext context) => const Center(
    child: Text('Live research requires the Windows desktop app.'),
  );
}
