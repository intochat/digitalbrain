import 'package:flutter/material.dart';

class ConnectionsPage extends StatelessWidget {
  const ConnectionsPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text('Connections', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 8),
          const Text('Coming in a later slice.'),
        ],
      ),
    );
  }
}
