import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/grpc/action_dispatch.dart';

void main() {
  Map<String, Object?> payloadOf(envelope) =>
      (jsonDecode(utf8.decode(envelope!.payload)) as Map).cast<String, Object?>();

  test('action with synapseType + props produces a unary envelope (login)', () {
    final envelope = buildActionEnvelope('action', {
      'actionId': 'LoginRequest',
      'label': 'Sign in',
      'synapseType': 'LoginRequest',
      'props': {'username': 'ada', 'password': 'pw', 'clientId': 'flutter'},
    });

    expect(envelope, isNotNull);
    expect(envelope!.typeName, 'LoginRequest');
    // The unary Send handlers read props at the TOP LEVEL of the payload.
    expect(payloadOf(envelope), {
      'username': 'ada',
      'password': 'pw',
      'clientId': 'flutter',
    });
  });

  test('action descriptor nested under "action" key is unwrapped', () {
    final envelope = buildActionEnvelope('press', {
      'label': 'Sign Out',
      'targetSurfaceKind': 'login',
      'action': {
        'actionId': 'LogoutRequest',
        'synapseType': 'LogoutRequest',
        'props': {'sessionId': 's1', 'clientId': 'flutter'},
      },
    });

    expect(envelope, isNotNull);
    expect(envelope!.typeName, 'LogoutRequest');
    expect(payloadOf(envelope), {'sessionId': 's1', 'clientId': 'flutter'});
  });

  test('nav-only event (no synapseType) produces no envelope', () {
    final envelope = buildActionEnvelope('press', {
      'label': 'Marketplace',
      'targetSurfaceKind': 'marketplace-list',
    });
    expect(envelope, isNull);
  });

  test('non-action event names produce no envelope', () {
    expect(buildActionEnvelope('hover', {'synapseType': 'X'}), isNull);
  });

  test('coerces non-string props to strings before encoding', () {
    final env = buildActionEnvelope('press', {
      'synapseType': 'ExperienceStep',
      'props': {'pack': 'p', 'eventName': 'go', 'agree': true, 'level': 0.5, 'missing': null},
    });
    final decoded = jsonDecode(utf8.decode(env!.payload)) as Map<String, dynamic>;
    expect(decoded['agree'], 'true');
    expect(decoded['level'], '0.5');
    expect(decoded['pack'], 'p');
    expect(decoded['eventName'], 'go');
    expect(decoded['missing'], '');
  });
}
