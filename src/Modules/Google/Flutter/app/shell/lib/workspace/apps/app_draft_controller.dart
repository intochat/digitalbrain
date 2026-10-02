import 'package:flutter/foundation.dart';

import 'app_document.dart';
import 'apps_screen.dart';

class AppDraftController extends ChangeNotifier {
  AppDraftController(this.id, this.request, [Map<String, dynamic>? initial])
    : view = initial ?? {};
  final String id;
  final AppsRequest request;
  Map<String, dynamic> view;
  AppDocument? pending;
  bool busy = false;
  String? error;
  Map<String, dynamic> get draft => appMap(view['draft']);
  int get revision => draft['revision'] as int? ?? 0;
  AppDocument? get document => draft['document'] == null
      ? null
      : AppDocument.fromJson(appMap(draft['document']));
  String get path => '/packages/drafts/$id';
  Future<void> load() => _update('GET', path);
  Future<void> save(AppDocument value) async {
    pending = value;
    await _update('PUT', '$path/document', {
      'expectedRevision': revision,
      'document': value.toJson(),
    });
    pending = null;
  }

  Future<void> build() => _update('POST', '$path/build');
  Future<AppDocument> proposeConversion() async => AppDocument.fromJson(
    appMap(
      await request('POST', '$path/conversion', {'expectedRevision': revision}),
    ),
  );
  Future<void> _update(String method, String path, [Object? body]) async {
    if (busy) throw StateError('An update is already in progress.');
    busy = true;
    error = null;
    notifyListeners();
    try {
      view = appMap(await request(method, path, body));
    } catch (e) {
      error = e.toString();
      rethrow;
    } finally {
      busy = false;
      notifyListeners();
    }
  }
}
