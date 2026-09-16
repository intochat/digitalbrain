import 'package:web/web.dart' as web;

Future<void> openExternalUrl(Uri url) async {
  if ((!url.isScheme('https') && !url.isScheme('http')) ||
      url.host.isEmpty ||
      url.userInfo.isNotEmpty) {
    throw ArgumentError('Only web URLs can be opened.');
  }
  web.window.open(url.toString(), '_blank', 'noopener,noreferrer');
}
