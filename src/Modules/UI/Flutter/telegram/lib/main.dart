import 'package:flutter/material.dart';

import 'app.dart';
import 'telegram_bridge.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(TelegramApp(initData: initializeTelegram()));
}
