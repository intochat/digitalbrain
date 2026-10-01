import 'package:http/http.dart' as http;

/// Native hosts read the process environment; only a served web bundle has a
/// same-origin config.json to ask for.
Future<Map<String, String>?> fetchServedConfig(http.Client? client) async =>
    null;
