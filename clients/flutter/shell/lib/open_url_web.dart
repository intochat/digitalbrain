import 'package:web/web.dart' as web;

Future<void> openExternalUrl(Uri url) async {
  web.window.open(url.toString(), '_blank');
}
