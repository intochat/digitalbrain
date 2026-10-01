import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

import 'compute_usage_details.dart';

class ComputeUsagePanel extends StatefulWidget {
  const ComputeUsagePanel({
    super.key,
    required this.client,
    required this.workspaceId,
    required this.workspaceName,
    required this.onClose,
    this.refreshSignal,
    this.savedHistory,
  });
  final DigitalBrainUiClient client;
  final String workspaceId, workspaceName;
  final VoidCallback onClose;
  final Listenable? refreshSignal;
  final List<ComputeUsageItem> Function()? savedHistory;
  @override
  State<ComputeUsagePanel> createState() => _ComputeUsagePanelState();
}

class _ComputeUsagePanelState extends State<ComputeUsagePanel> {
  final _items = <String, ComputeUsageItem>{};
  ComputeAccountSummary? _summary;
  ComputeUsageItem? _selected;
  String? _cursor, _error, _summaryError;
  bool _busy = false, _pendingRefresh = false, _retryMore = false;
  int _generation = 0;
  Timer? _autoRefresh;

  @override
  void initState() {
    super.initState();
    widget.refreshSignal?.addListener(_refresh);
    // Cancellation can prevent the final receipt reaching chat. Recover committed
    // usage while this panel remains open, without depending on that event.
    _autoRefresh = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _refresh(),
    );
    unawaited(_load());
  }

  void _refresh() => unawaited(_load());

  @override
  void didUpdateWidget(ComputeUsagePanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.refreshSignal != widget.refreshSignal) {
      oldWidget.refreshSignal?.removeListener(_refresh);
      widget.refreshSignal?.addListener(_refresh);
    }
    if (oldWidget.workspaceId != widget.workspaceId ||
        oldWidget.client != widget.client) {
      _generation++;
      _items.clear();
      _summary = null;
      _selected = null;
      _cursor = _error = _summaryError = null;
      _busy = _pendingRefresh = false;
      unawaited(_load());
    }
  }

  @override
  void dispose() {
    _generation++;
    _autoRefresh?.cancel();
    widget.refreshSignal?.removeListener(_refresh);
    super.dispose();
  }

  Future<void> _load({bool more = false}) async {
    if (_busy) {
      if (!more) _pendingRefresh = true;
      return;
    }
    final generation = ++_generation;
    setState(() {
      _busy = true;
      _error = null;
      _retryMore = more;
    });
    final summary = more ? Future<void>.value() : _loadSummary(generation);
    try {
      final page = await widget.client.readComputeUsage(
        widget.workspaceId,
        cursor: more ? _cursor : null,
      );
      if (!mounted || generation != _generation) return;
      setState(() {
        if (!more) _items.clear();
        for (final item in page.items) {
          _items[item.id] = item;
        }
        _cursor = page.nextCursor;
        if (_selected != null) _selected = _items[_selected!.id] ?? _selected;
      });
    } catch (_) {
      if (mounted && generation == _generation) {
        setState(
          () => _error =
              'Could not load usage. Displayed history may be out of date.',
        );
      }
    } finally {
      await summary;
      if (mounted && generation == _generation) {
        setState(() => _busy = false);
        if (_pendingRefresh) {
          _pendingRefresh = false;
          unawaited(_load());
        }
      }
    }
  }

  Future<void> _loadSummary(int generation) async {
    try {
      final value = await widget.client.readComputeSummary();
      if (mounted && generation == _generation) {
        setState(() {
          _summary = value;
          _summaryError = null;
        });
      }
    } catch (_) {
      if (mounted && generation == _generation) {
        setState(
          () => _summaryError = _summary == null
              ? 'Account totals unavailable.'
              : 'Account totals could not refresh. Previous totals shown.',
        );
      }
    }
  }

  List<ComputeUsageItem> get _visible {
    final merged = <String, ComputeUsageItem>{
      for (final item in widget.savedHistory?.call() ?? <ComputeUsageItem>[])
        item.id: item,
      ..._items,
    };
    return merged.values.toList()..sort(
      (a, b) => (b.occurredAt ?? DateTime(1970)).compareTo(
        a.occurredAt ?? DateTime(1970),
      ),
    );
  }

  Widget _accountSummary(BuildContext context) {
    final summary = _summary;
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Account · all workspaces',
            style: Theme.of(context).textTheme.labelMedium,
          ),
          const SizedBox(height: 12),
          if (_summaryError != null) Text(_summaryError!),
          if (summary != null) ...[
            Wrap(
              spacing: 32,
              runSpacing: 12,
              children: [
                _amount(context, 'Charged', summary.chargedCompute),
                _amount(context, 'Reserved', summary.reservedCompute),
                if (summary.settledCompute != null)
                  _amount(context, 'App settlements', summary.settledCompute),
              ],
            ),
            const SizedBox(height: 12),
            if (summary.settledCompute != null)
              const Padding(
                padding: EdgeInsets.only(bottom: 8),
                child: Text(
                  'Wallet charges and app settlements are separate records; they are not added together.',
                  style: TextStyle(fontSize: 12),
                ),
              ),
            if (summary.limitCompute != null && summary.limitCompute! > 0) ...[
              if (summary.spentCompute != null)
                LinearProgressIndicator(
                  value: (summary.spentCompute! / summary.limitCompute!).clamp(
                    0.0,
                    1.0,
                  ),
                ),
              const SizedBox(height: 6),
              Text(
                '${computeAmount(summary.spentCompute)} / ${computeAmount(summary.limitCompute)} allowance used, including reservations',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ] else if (summary.limitCompute == 0)
              const Text('No account limit'),
            if (summary.hardStopped)
              Text(
                'Account limit reached',
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
          ],
        ],
      ),
    );
  }

  Widget _amount(BuildContext context, String label, double? value) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(label, style: Theme.of(context).textTheme.labelMedium),
      Text(
        '${computeAmount(value)} Compute',
        style: Theme.of(context).textTheme.titleMedium,
      ),
    ],
  );

  @override
  Widget build(BuildContext context) {
    final items = _visible;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(12, 8, 8, 4),
          child: Row(
            children: [
              if (_selected != null)
                IconButton(
                  tooltip: 'Back to usage',
                  onPressed: () => setState(() => _selected = null),
                  icon: const Icon(Icons.arrow_back),
                ),
              Expanded(
                child: Text(
                  'Compute',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
              ),
              IconButton(
                tooltip: 'Refresh usage',
                onPressed: _busy ? null : _refresh,
                icon: const Icon(Icons.refresh),
              ),
              IconButton(
                tooltip: 'Close Compute',
                onPressed: widget.onClose,
                icon: const Icon(Icons.close),
              ),
            ],
          ),
        ),
        if (_busy) const LinearProgressIndicator(minHeight: 2),
        if (_selected == null) ...[
          _accountSummary(context),
          const Divider(height: 1),
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 8),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Activity · ${widget.workspaceName}',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 4),
                const Text(
                  'Newest first · previews are not charges',
                  style: TextStyle(fontSize: 12),
                ),
              ],
            ),
          ),
          Expanded(
            child: UiCollectionView(
              fileMode: false,
              selectionEnabled: false,
              items: [
                for (final item in items)
                  {
                    'id': item.id,
                    'label': item.title,
                    'kind': 'usage',
                    'canActivate': true,
                    'secondaryText':
                        '${usageTime(item.occurredAt)}${item.localHistory
                            ? ' · Saved on this device'
                            : item.outcome.isEmpty
                            ? ''
                            : ' · ${item.outcome}'}',
                    'trailingText': usageAmountLabel(item),
                  },
              ],
              emptyLabel: _busy
                  ? 'Loading usage…'
                  : _error != null
                  ? 'Usage history is unavailable.'
                  : 'No usage recorded yet.',
              error: _error,
              onRetry: _busy ? null : () => unawaited(_load(more: _retryMore)),
              onActivate: (row) => setState(
                () => _selected = items.firstWhere((i) => i.id == row['id']),
              ),
              onLoadMore: _cursor == null || _busy
                  ? null
                  : () => unawaited(_load(more: true)),
              loadingMore: _busy && _retryMore,
            ),
          ),
        ] else
          Expanded(child: ComputeUsageDetails(item: _selected!)),
      ],
    );
  }
}
