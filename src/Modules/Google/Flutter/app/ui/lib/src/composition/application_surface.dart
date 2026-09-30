import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

import 'neuron_view.dart';

import 'package:flutter/material.dart';

/// Starts an application and renders the surface it declares. The shell supplies
/// platform renderers; the application owns the component tree.
class ApplicationSurface extends StatefulWidget {
  const ApplicationSurface({
    super.key,
    required this.client,
    required this.workspace,
    required this.application,
    this.onActivate,
    this.startArguments = const {},
  });

  final DigitalBrainUiClient client;
  final String workspace, application;
  final Map<String, dynamic> startArguments;
  final ValueChanged<Map<String, dynamic>>? onActivate;

  @override
  State<ApplicationSurface> createState() => _ApplicationSurfaceState();
}

class _ApplicationSurfaceState extends State<ApplicationSurface> {
  late Future<Map<String, dynamic>> _surface = _start();

  Future<Map<String, dynamic>> _start() async {
    final result = await widget.client.jsonRequest(
      'POST',
      '/brains/${Uri.encodeComponent(widget.workspace)}/applications/'
          '${Uri.encodeComponent(widget.application)}/start',
      widget.startArguments,
    );
    return Map<String, dynamic>.from((result as Map)['surface'] as Map);
  }

  @override
  void didUpdateWidget(ApplicationSurface oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.workspace != widget.workspace ||
        oldWidget.application != widget.application ||
        oldWidget.client != widget.client) {
      _surface = _start();
    }
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Map<String, dynamic>>(
    future: _surface,
    builder: (context, snapshot) {
      if (snapshot.hasError) {
        return Center(
          child: TextButton(
            onPressed: () => setState(() {
              _surface = _start();
            }),
            child: const Text('Retry application'),
          ),
        );
      }
      if (snapshot.connectionState != ConnectionState.done ||
          !snapshot.hasData) {
        return const Center(child: CircularProgressIndicator());
      }
      final surface = snapshot.data!;
      return NeuronView(
        key: ValueKey('${widget.workspace}/${widget.application}'),
        kind: surface['kind'] as String,
        name: surface['name'] as String,
        load: (kind, name) => widget.client.appRequest(
          widget.workspace,
          'node?${Uri(queryParameters: {'kind': kind, 'name': name}).query}',
        ),
        onAction: (event) async {
          await widget.client.appRequest(
            widget.workspace,
            'event',
            body: event,
          );
        },
        onActivate: widget.onActivate,
      );
    },
  );
}
