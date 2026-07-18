import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/brain/brain_topology.dart';

void main() {
  group('topologyIdForGrain', () {
    test('maps known grain prefix', () {
      expect(topologyIdForGrain('cortexneuron/0'), equals('kernel.cortex'));
      expect(
        topologyIdForGrain('cortexneuron/alice:default'),
        equals('kernel.cortex'),
      );
    });

    test('maps travel grains to their topology ids', () {
      expect(
        topologyIdForGrain('flightsearchneuron/0'),
        equals('travel.flight_search'),
      );
      expect(
        topologyIdForGrain('hotelsearchneuron/alice'),
        equals('travel.hotel_search'),
      );
      expect(
        topologyIdForGrain('findflightsplan/alice:default'),
        equals('travel.find_flights'),
      );
      expect(
        topologyIdForGrain('plantripplan/alice:default'),
        equals('travel.plan'),
      );
    });

    test('returns null for unmapped grain', () {
      expect(topologyIdForGrain('mysterytypename/whatever'), isNull);
    });

    test('returns null for grain id missing the slash', () {
      expect(topologyIdForGrain('orphanstring'), isNull);
    });
  });
}
