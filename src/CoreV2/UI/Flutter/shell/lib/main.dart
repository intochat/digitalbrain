import 'dart:convert';

import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:flutter/material.dart';

void main() {
  final productBase = DigitalBrainHostEnvironment.requireProductBase();
  runApp(DigitalBrainShell(productBase: productBase));
}

class DigitalBrainShell extends StatefulWidget {
  const DigitalBrainShell({required this.productBase, this.api, super.key});

  final Uri productBase;
  final DigitalBrainProductApi? api;

  @override
  State<DigitalBrainShell> createState() => _DigitalBrainShellState();
}

class _DigitalBrainShellState extends State<DigitalBrainShell> {
  late final DigitalBrainProductApi _api;
  late final bool _ownsApi;
  final TextEditingController _input = TextEditingController(
    text: const JsonEncoder.withIndent('  ').convert({'value': 'hello'}),
  );
  List<ProductModule> _modules = const [];
  List<ProductOperation> _operations = const [];
  ProductActivity? _activity;
  Object? _error;
  bool _loading = true;
  bool _invoking = false;

  @override
  void initState() {
    super.initState();
    _ownsApi = widget.api == null;
    _api = widget.api ?? DigitalBrainProductClient(baseUri: widget.productBase);
    _load();
  }

  Future<void> _load() async {
    try {
      final values = await Future.wait([
        _api.getModules(),
        _api.getOperations(),
      ]);
      if (!mounted) return;
      setState(() {
        _modules = values[0] as List<ProductModule>;
        _operations = values[1] as List<ProductOperation>;
        _loading = false;
      });
    } on Object catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error;
        _loading = false;
      });
    }
  }

  Future<void> _invoke(ProductOperation operation) async {
    setState(() {
      _invoking = true;
      _error = null;
      _activity = null;
    });
    try {
      final decoded = jsonDecode(_input.text);
      if (decoded is! Map) {
        throw const FormatException('Input must be a JSON object.');
      }
      final receipt = await _api.invoke(
        operation.id,
        Map<String, Object?>.from(decoded),
        idempotencyKey: 'flutter-${DateTime.now().microsecondsSinceEpoch}',
      );
      ProductActivity? observed;
      await for (final update in _api.watchActivity(receipt.activity)) {
        observed = update;
        if (mounted) setState(() => _activity = update);
      }
      observed ??= await _api.getActivity(receipt.activity);
      if (mounted) setState(() => _activity = observed);
    } on Object catch (error) {
      if (mounted) setState(() => _error = error);
    } finally {
      if (mounted) setState(() => _invoking = false);
    }
  }

  @override
  void dispose() {
    _input.dispose();
    if (_ownsApi) _api.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'DigitalBrain CoreV2',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xff4f46e5),
          brightness: Brightness.dark,
        ),
        useMaterial3: true,
      ),
      home: Scaffold(
        appBar: AppBar(
          title: const Text('DigitalBrain CoreV2'),
          actions: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: Center(child: Text(widget.productBase.origin)),
            ),
          ],
        ),
        body: _body(),
      ),
    );
  }

  Widget _body() {
    if (_loading) {
      return const Center(child: CircularProgressIndicator());
    }
    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        Text('Modules', style: Theme.of(context).textTheme.headlineMedium),
        const SizedBox(height: 12),
        Wrap(
          spacing: 12,
          runSpacing: 12,
          children: _modules.map(_moduleCard).toList(growable: false),
        ),
        const SizedBox(height: 32),
        Text('Operations', style: Theme.of(context).textTheme.headlineMedium),
        const SizedBox(height: 12),
        ..._operations.map(_operationCard),
        if (_activity case final activity?) ...[
          const SizedBox(height: 24),
          Card(
            color: activity.isCompleted
                ? Colors.green.withValues(alpha: 0.16)
                : Colors.red.withValues(alpha: 0.16),
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Activity ${activity.statusLabel}'),
                  const SizedBox(height: 8),
                  SelectableText(activity.resultJson ?? activity.problem ?? ''),
                ],
              ),
            ),
          ),
        ],
        if (_error case final error?) ...[
          const SizedBox(height: 24),
          Text('$error', style: const TextStyle(color: Colors.redAccent)),
        ],
      ],
    );
  }

  Widget _moduleCard(ProductModule module) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            module.displayName,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 6),
          Chip(
            avatar: Icon(
              module.isReady ? Icons.check_circle : Icons.settings,
              size: 18,
            ),
            label: Text(module.statusLabel),
          ),
        ],
      ),
    ),
  );

  Widget _operationCard(ProductOperation operation) => Card(
    margin: const EdgeInsets.only(bottom: 16),
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            operation.displayName,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          Text(operation.id),
          const SizedBox(height: 14),
          TextField(
            key: const Key('operation-input'),
            controller: _input,
            minLines: 3,
            maxLines: 8,
            decoration: const InputDecoration(
              labelText: 'JSON input',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 12),
          FilledButton.icon(
            key: const Key('invoke-operation'),
            onPressed: _invoking ? null : () => _invoke(operation),
            icon: _invoking
                ? const SizedBox.square(
                    dimension: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.play_arrow),
            label: const Text('Invoke'),
          ),
        ],
      ),
    ),
  );
}
