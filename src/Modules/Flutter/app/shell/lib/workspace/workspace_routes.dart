import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

/// Keeps the shell's durable project selection in browser navigation history.
class WorkspaceRouteBridge extends NavigatorObserver
    with WidgetsBindingObserver {
  WorkspaceRouteBridge({required this.onNavigate}) {
    WidgetsBinding.instance.addObserver(this);
  }
  final ValueChanged<Uri> onNavigate;
  String _basePath = '/projects';
  String? _overlayPath;
  String? _publishedPath;
  bool _scheduled = false;
  bool _receiving = false;
  bool _disposed = false;

  void sync(String path) {
    _basePath = path;
    if (!kIsWeb || _scheduled || _disposed) return;
    _scheduled = true;
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      _scheduled = false;
      if (_disposed) return;
      final target = _overlayPath ?? _basePath;
      if (target == _publishedPath && !_receiving) {
        _receiving = false;
        return;
      }
      final replace = _publishedPath == null || _receiving;
      _publishedPath = target;
      _receiving = false;
      await SystemNavigator.selectMultiEntryHistory();
      await SystemNavigator.routeInformationUpdated(
        uri: Uri.parse(target),
        replace: replace,
      );
    });
  }

  @override
  void didChangeTop(Route<dynamic> topRoute, Route<dynamic>? previousTopRoute) {
    final name = topRoute.settings.name;
    _overlayPath = name?.startsWith('/settings/') == true ? name : null;
    sync(_basePath);
  }

  @override
  Future<bool> didPushRouteInformation(
    RouteInformation routeInformation,
  ) async {
    final uri = routeInformation.uri;
    if (uri.path != '/explore' &&
        !uri.path.startsWith('/projects') &&
        !uri.path.startsWith('/settings')) {
      return false;
    }
    _receiving = true;
    _publishedPath = uri.path;
    onNavigate(uri);
    return true;
  }

  void dispose() {
    _disposed = true;
    WidgetsBinding.instance.removeObserver(this);
  }
}
