import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';

/// Share this controller by table ID: cards always display the same server view.
final class UiTableController extends ChangeNotifier {
  UiTableController({required this._snapshot, this.read, this.update});
  TableSnapshot _snapshot;
  TableSnapshot get snapshot => _snapshot;
  final ReadTable? read;
  final UpdateTableView? update;
  bool busy = false;
  String? error;
  bool _disposed = false;
  int _request = 0;

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  void accept(TableSnapshot snapshot) {
    if (_disposed ||
        snapshot.id != _snapshot.id ||
        snapshot.revision < _snapshot.revision) {
      return;
    }
    _snapshot = snapshot;
    _notify();
  }

  Future<void> reload({int? offset}) async {
    final reader = read;
    if (reader == null || _disposed) return;
    final request = ++_request;
    busy = true;
    error = null;
    _notify();
    try {
      final next = await reader(
        snapshot.id,
        offset: offset ?? snapshot.offset,
        limit: snapshot.limit,
      );
      if (!_disposed && request == _request) accept(next);
    } catch (e) {
      if (!_disposed && request == _request) {
        error = 'Could not load the table. $e';
      }
    } finally {
      if (!_disposed && request == _request) {
        busy = false;
        _notify();
      }
    }
  }

  Future<void> change({
    List<TableFilter>? filters,
    TableSort? sort,
    bool replaceSort = false,
    List<String>? visibleColumns,
  }) async {
    final writer = update;
    if (writer == null || busy || _disposed) return;
    final current = snapshot;
    final request = ++_request;
    busy = true;
    error = null;
    _notify();
    try {
      final next = await writer(
        current.id,
        TableViewUpdate(
          expectedRevision: current.revision,
          filters: filters ?? current.filters,
          sort: replaceSort ? sort : current.sort,
          visibleColumns: visibleColumns ?? current.visibleColumns,
        ),
      );
      if (!_disposed && request == _request) accept(next);
    } catch (e) {
      if (_disposed || request != _request) return;
      final conflict = e is TableRequestException && e.statusCode == 409;
      error = conflict
          ? 'This table changed elsewhere. Latest state loaded; review it and apply your change again.'
          : 'The change could not be saved. $e';
      // A timed-out request may have applied. Always reconcile; never replay it.
      if (read case final reader?) {
        try {
          final latest = await reader(
            current.id,
            offset: 0,
            limit: current.limit,
          );
          if (!_disposed && request == _request) accept(latest);
        } catch (_) {
          if (!_disposed && request == _request) {
            error =
                '${conflict ? 'This table changed elsewhere.' : 'The change could not be saved.'} Reload failed. Use Refresh before trying again.';
          }
        }
      }
    } finally {
      if (!_disposed && request == _request) {
        busy = false;
        _notify();
      }
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _request++;
    super.dispose();
  }
}
