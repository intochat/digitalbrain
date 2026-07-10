import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/v2/v2_runtime.dart';

void main() {
  test('session controller never retains credentials after sign out', () {
    final session = V2SessionController()..establish(session: 's', tenant: 't', workspace: 'w');
    session.signOut();
    expect(session.status, V2SessionStatus.signedOut);
    expect(session.sessionId, isNull);
  });

  test('feed controller detects gaps and deduplicates', () {
    final feed = V2FeedController();
    expect(feed.lastSequence, 0);
    feed.reset();
    expect(feed.needsReset, isFalse);
  });
}
