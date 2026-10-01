import 'package:http/browser_client.dart';
import 'package:http/http.dart' as http;

// The browser owns HttpOnly cookies; credentials are required across shell/kernel ports.
http.Client createSessionHttpClient() =>
    BrowserClient()..withCredentials = true;
