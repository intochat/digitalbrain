import 'dart:io';

Future<void> openExternalUrl(Uri url) async {
  if (Platform.isWindows) {
    await Process.start('cmd', ['/c', 'start', '', url.toString()]);
    return;
  }

  if (Platform.isMacOS) {
    await Process.start('open', [url.toString()]);
    return;
  }

  await Process.start('xdg-open', [url.toString()]);
}
