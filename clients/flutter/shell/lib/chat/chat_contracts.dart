import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

typedef SendMessage = Future<void> Function(String text);
typedef StreamMessage = Stream<ChatDelta> Function(String text);
typedef LoadTopology = Future<BrainTopologySnapshot> Function();
typedef OpenUrl = Future<void> Function(Uri url);

const ownerUserId = 'owner';
const assistantUserId = 'assistant';
const brainDestinationIndex = 2;
