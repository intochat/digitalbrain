import 'package:shared_preferences/shared_preferences.dart';

/// Per-brain layout persistence (V4-3 isolation). Keyed by brainId so each
/// brain restores its own arrangement. Uses the platform key-value store
/// (localStorage on web, managed store on desktop) — never plain token files.
class LayoutStore {
  LayoutStore({SharedPreferencesAsync? prefs})
    : _prefs = prefs ?? SharedPreferencesAsync();

  final SharedPreferencesAsync _prefs;

  static String _key(String brainId) => 'dbrain.canvas.layout.$brainId';

  Future<void> save(String brainId, String serializedLayout) =>
      _prefs.setString(_key(brainId), serializedLayout);

  Future<String?> load(String brainId) => _prefs.getString(_key(brainId));

  Future<void> clear(String brainId) => _prefs.remove(_key(brainId));
}
