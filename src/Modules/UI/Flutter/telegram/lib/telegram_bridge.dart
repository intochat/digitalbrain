import 'dart:js_interop';

@JS('Telegram')
external TelegramRoot? get telegram;

extension type TelegramRoot(JSObject _) implements JSObject {
  @JS('WebApp')
  external TelegramWebApp? get webApp;
}

extension type TelegramWebApp(JSObject _) implements JSObject {
  external String get initData;
  external void ready();
  external void expand();
}

String initializeTelegram() {
  final app = telegram?.webApp;
  if (app == null) return '';
  app.ready();
  app.expand();
  return app.initData;
}
