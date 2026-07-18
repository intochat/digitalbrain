import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/grpc/ino_client.dart';

void main() {
  group('InoGrpcClient', () {
    test('creates without error', () {
      final client = InoGrpcClient(host: 'localhost', port: 5400);
      expect(client, isNotNull);
    });
  });
}
