import 'package:url_launcher/url_launcher.dart';

Future<void> openExternalUrl(Uri url) async {
  if ((!url.isScheme('https') && !url.isScheme('http')) ||
      url.host.isEmpty ||
      url.userInfo.isNotEmpty) {
    throw ArgumentError('Only web URLs can be opened.');
  }
  if (!await launchUrl(url, mode: LaunchMode.externalApplication)) {
    throw StateError('The browser could not be opened.');
  }
}
