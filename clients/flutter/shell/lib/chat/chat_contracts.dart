import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

typedef SendMessage = Future<void> Function(String text);
typedef StreamMessage = Stream<ChatDelta> Function(String text);

const ownerUserId = 'owner';
const assistantUserId = 'assistant';
