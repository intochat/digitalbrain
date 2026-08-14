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
  final TextEditingController _operationInput = TextEditingController(
    text: const JsonEncoder.withIndent('  ').convert({'value': 'hello'}),
  );
  final TextEditingController _conversationInput = TextEditingController();
  List<ProductModule> _modules = const [];
  List<ProductOperation> _operations = const [];
  List<Map<String, Object?>> _messages = const [];
  ProductOperation? _selectedOperation;
  ProductActivity? _activity;
  Object? _error;
  bool _loading = true;
  bool _invoking = false;
  bool _conversationBusy = false;

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
      final operations = values[1] as List<ProductOperation>;
      setState(() {
        _modules = values[0] as List<ProductModule>;
        _operations = operations;
        _selectedOperation = _defaultOperation(operations);
        _loading = false;
      });
      if (_operation('conversation/read@1') != null) {
        await _readConversation();
      }
    } on Object catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error;
        _loading = false;
      });
    }
  }

  ProductOperation? _defaultOperation(List<ProductOperation> operations) {
    for (final operation in operations) {
      if (operation.id == 'proof/run@1') return operation;
    }
    return operations.isEmpty ? null : operations.first;
  }

  ProductOperation? _operation(String id) {
    for (final operation in _operations) {
      if (operation.id == id) return operation;
    }
    return null;
  }

  Future<ProductActivity> _execute(
    ProductOperation operation,
    Map<String, Object?> input,
  ) async {
    final receipt = await _api.invoke(
      operation.id,
      input,
      idempotencyKey: 'flutter-${DateTime.now().microsecondsSinceEpoch}',
    );
    ProductActivity? observed;
    await for (final update in _api.watchActivity(receipt.activity)) {
      observed = update;
      if (mounted) setState(() => _activity = update);
    }
    return observed ?? await _api.getActivity(receipt.activity);
  }

  Future<void> _readConversation() async {
    final operation = _operation('conversation/read@1');
    if (operation == null) return;
    await _runConversation(operation, const {'conversationId': 'main'});
  }

  Future<void> _sendConversation() async {
    final operation = _operation('conversation/send@1');
    final message = _conversationInput.text.trim();
    if (operation == null || message.isEmpty) return;
    await _runConversation(operation, {
      'conversationId': 'main',
      'message': message,
    });
    if (mounted) _conversationInput.clear();
  }

  Future<void> _runConversation(
    ProductOperation operation,
    Map<String, Object?> input,
  ) async {
    setState(() {
      _conversationBusy = true;
      _error = null;
    });
    try {
      final activity = await _execute(operation, input);
      final result = activity.result;
      if (result is! Map || result['messages'] is! List) {
        throw const FormatException('Conversation result has no messages.');
      }
      final messages = (result['messages'] as List)
          .map((value) => Map<String, Object?>.from(value as Map))
          .toList(growable: false);
      if (mounted) setState(() => _messages = messages);
    } on Object catch (error) {
      if (mounted) setState(() => _error = error);
    } finally {
      if (mounted) setState(() => _conversationBusy = false);
    }
  }

  Future<void> _invokeSelected() async {
    final operation = _selectedOperation;
    if (operation == null) return;
    setState(() {
      _invoking = true;
      _error = null;
      _activity = null;
    });
    try {
      final decoded = jsonDecode(_operationInput.text);
      if (decoded is! Map) {
        throw const FormatException('Input must be a JSON object.');
      }
      final activity = await _execute(
        operation,
        Map<String, Object?>.from(decoded),
      );
      if (mounted) setState(() => _activity = activity);
    } on Object catch (error) {
      if (mounted) setState(() => _error = error);
    } finally {
      if (mounted) setState(() => _invoking = false);
    }
  }

  @override
  void dispose() {
    _operationInput.dispose();
    _conversationInput.dispose();
    if (_ownsApi) _api.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain CoreV2',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: Colors.deepPurple),
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
        if (_operation('conversation/send@1') != null) ...[
          const SizedBox(height: 32),
          _conversationCard(),
        ],
        const SizedBox(height: 32),
        _operationLab(),
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
          if (module.setupMessage case final message?)
            SizedBox(width: 220, child: Text(message)),
        ],
      ),
    ),
  );

  Widget _conversationCard() => Card(
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Conversation',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 12),
          if (_messages.isEmpty)
            const Text('Start the durable main conversation.')
          else
            ..._messages.map(
              (message) => ListTile(
                leading: const Icon(Icons.person_outline),
                title: Text('${message['text']}'),
                subtitle: Text('${message['principal']}'),
              ),
            ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: TextField(
                  key: const Key('conversation-input'),
                  controller: _conversationInput,
                  onSubmitted: (_) => _sendConversation(),
                  decoration: const InputDecoration(
                    labelText: 'Message',
                    border: OutlineInputBorder(),
                  ),
                ),
              ),
              const SizedBox(width: 12),
              FilledButton.icon(
                key: const Key('conversation-send'),
                onPressed: _conversationBusy ? null : _sendConversation,
                icon: _conversationBusy
                    ? const SizedBox.square(
                        dimension: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.send),
                label: const Text('Send'),
              ),
            ],
          ),
        ],
      ),
    ),
  );

  Widget _operationLab() => Card(
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Operation lab',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<ProductOperation>(
            key: const Key('operation-selector'),
            isExpanded: true,
            initialValue: _selectedOperation,
            items: _operations
                .map(
                  (operation) => DropdownMenuItem(
                    value: operation,
                    child: Text(
                      '${operation.displayName} · ${operation.id}',
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                )
                .toList(growable: false),
            onChanged: (operation) =>
                setState(() => _selectedOperation = operation),
            decoration: const InputDecoration(
              labelText: 'Operation',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 14),
          TextField(
            key: const Key('operation-input'),
            controller: _operationInput,
            minLines: 3,
            maxLines: 8,
            decoration: const InputDecoration(
              labelText: 'JSON input',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 12),
          Align(
            alignment: Alignment.centerLeft,
            child: FilledButton.icon(
              key: const Key('invoke-operation'),
              onPressed: _invoking || _selectedOperation == null
                  ? null
                  : _invokeSelected,
              icon: _invoking
                  ? const SizedBox.square(
                      dimension: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.play_arrow),
              label: const Text('Invoke'),
            ),
          ),
        ],
      ),
    ),
  );
}
