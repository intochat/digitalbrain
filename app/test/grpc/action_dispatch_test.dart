import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/grpc/action_dispatch.dart';

void main() {
  Map<String, Object?> payloadOf(envelope) =>
      (jsonDecode(utf8.decode(envelope!.payload)) as Map)
          .cast<String, Object?>();

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

  test('Google auth button press dispatches GoogleAuthRequested', () {
    final envelope = buildActionEnvelope('press', {
      'synapseType': 'GoogleAuthRequested',
      'props': {'sessionId': 'session-gmail-auth'},
    });

    expect(envelope, isNotNull);
    expect(envelope!.typeName, 'GoogleAuthRequested');
    expect(payloadOf(envelope), {'sessionId': 'session-gmail-auth'});
  });

  test('Salesforce auth button adds OAuth callback URL', () {
    final envelope = buildActionEnvelope('press', {
      'synapseType': 'SalesforceAuthRequested',
      'props': {
        'pack': 'salesforce',
        'callbackPath': '/salesforce-callback',
        'client_id': 'connected-app-id',
        'client_secret': 'connected-app-secret',
      },
    });

    expect(envelope, isNotNull);
    expect(envelope!.typeName, 'SalesforceAuthRequested');
    expect(payloadOf(envelope), {
      'pack': 'salesforce',
      'callbackPath': '/salesforce-callback',
      'client_id': 'connected-app-id',
      'client_secret': 'connected-app-secret',
      'redirect_uri': 'http://localhost:8081/salesforce-callback',
    });
  });

  test('default client id is added when action props omit it', () {
    final envelope = buildActionEnvelope('press', {
      'synapseType': 'SalesforceAuthRequested',
      'props': {'pack': 'salesforce', 'callbackPath': '/salesforce-callback'},
    }, defaultClientId: 'flutter');

    expect(envelope, isNotNull);
    expect(payloadOf(envelope), {
      'pack': 'salesforce',
      'callbackPath': '/salesforce-callback',
      'clientId': 'flutter',
      'redirect_uri': 'http://localhost:8081/salesforce-callback',
    });
  });

  test('nested props preserve explicit client id over default', () {
    final envelope = buildActionEnvelope('press', {
      'synapseType': 'SalesforceAuthRequested',
      'props': {'pack': 'salesforce', 'clientId': 'surface-client'},
    }, defaultClientId: 'flutter');

    expect(envelope, isNotNull);
    expect(payloadOf(envelope), {
      'pack': 'salesforce',
      'clientId': 'surface-client',
    });
  });

  test('top-level action context is merged with nested props', () {
    final envelope = buildActionEnvelope('press', {
      'synapseType': 'ConfigurationProvided',
      'clientId': 'outer-client',
      'props': {'pack': 'salesforce', 'username': 'user@example.com'},
    });

    expect(envelope, isNotNull);
    expect(payloadOf(envelope), {
      'clientId': 'outer-client',
      'pack': 'salesforce',
      'username': 'user@example.com',
    });
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
      'props': {
        'pack': 'p',
        'eventName': 'go',
        'agree': true,
        'level': 0.5,
        'missing': null,
      },
    });
    final decoded =
        jsonDecode(utf8.decode(env!.payload)) as Map<String, dynamic>;
    expect(decoded['agree'], 'true');
    expect(decoded['level'], '0.5');
    expect(decoded['pack'], 'p');
    expect(decoded['eventName'], 'go');
    expect(decoded['missing'], '');
  });

  test('panel config action flattens props for ConfigurationProvided', () {
    final envelope = buildPanelEventEnvelope(
      'surface.pack-config.salesforce',
      'press',
      {
        'synapseType': 'ConfigurationProvided',
        'props': {
          'pack': 'salesforce',
          'eventName': 'ConfigurationProvided',
          'client_id': 'connected-app-id',
          'client_secret': 'connected-app-secret',
          'username': 'user@example.com',
          'password': 'pw',
        },
      },
      defaultClientId: 'flutter',
    );

    expect(envelope, isNotNull);
    expect(envelope!.typeName, 'ConfigurationProvided');
    expect(payloadOf(envelope), {
      'pack': 'salesforce',
      'eventName': 'ConfigurationProvided',
      'clientId': 'flutter',
      'client_id': 'connected-app-id',
      'client_secret': 'connected-app-secret',
      'username': 'user@example.com',
      'password': 'pw',
    });
  });

  test(
    'panel legacy type event preserves typed payload and panel correlation',
    () {
      final envelope = buildPanelEventEnvelope('panel-reminder', 'snooze', {
        'type': 'DigitalBrain.WidgetCanvas.Snooze',
        'minutes': 5,
      });

      expect(envelope, isNotNull);
      expect(envelope!.correlationId, 'panel-reminder');
      expect(envelope.typeName, 'DigitalBrain.WidgetCanvas.Snooze');
      expect(payloadOf(envelope), {'minutes': 5});
    },
  );
}
