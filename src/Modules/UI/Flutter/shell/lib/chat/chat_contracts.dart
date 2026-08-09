import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

typedef SendMessage = Future<void> Function(String text);
typedef StreamMessage = Stream<ChatDelta> Function(String text);
typedef LoadTopology = Future<BrainTopologySnapshot> Function();
typedef OpenUrl = Future<void> Function(Uri url);

const ownerUserId = 'owner';
const assistantUserId = 'assistant';
const brainDestinationIndex = 2;
const behaviorsDestinationIndex = 3;
const kitDestinationIndex = 4;
const windowingDestinationIndex = 5;

typedef LoadBehaviors = Future<BehaviorLibraryDocument> Function();
typedef OpenBehavior = Future<BehaviorDocument> Function(String behaviorId);
