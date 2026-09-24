import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('AppManifestSummary reads examples, permissions and meters', () {
    final summary = AppManifestSummary.fromJson({
      'id': 'intochat.leadgenerator',
      'name': 'LeadGenerator',
      'description': 'Finds new customers.',
      'kind': 'declarative',
      'uiEntry': null,
      'examplePrompts': ['Find new dental clinics in Berlin'],
      'permissions': [
        {
          'semanticTypeId': 'person.birthDate',
          'reason': 'To greet you.',
          'write': false,
        },
      ],
      'meters': [
        {
          'meterId': 'search.request',
          'unit': 'request',
          'aggregation': 'sum',
          'proposedPriceInCompute': 5,
        },
      ],
    });

    expect(summary.examples, ['Find new dental clinics in Berlin']);
    expect(summary.permissions.single.semanticTypeId, 'person.birthDate');
    expect(summary.permissions.single.reason, 'To greet you.');
    expect(summary.meters.single.meterId, 'search.request');
    expect(summary.meters.single.proposedPriceInCompute, 5);
  });

  test('ConsentSheet parses the server sheet', () {
    final sheet = ConsentSheet.fromJson({
      'appId': 'intochat.leadgenerator',
      'version': '1.0.0',
      'name': 'LeadGenerator',
      'descriptionForPeople': 'Finds new customers.',
      'examples': ['Find clinics'],
      'dataTypes': [
        {'semanticTypeId': 'company.name', 'reason': 'Names them.'},
      ],
      'meters': [
        {
          'meterId': 'search.request',
          'unit': 'request',
          'aggregation': 'sum',
          'proposedPriceInCompute': 5,
        },
      ],
      'estimatedCompute': 50,
      'approved': false,
    });

    expect(sheet.estimatedCompute, 50);
    expect(sheet.dataTypes.single.semanticTypeId, 'company.name');
    expect(sheet.approved, isFalse);
  });

  test('GrantSummary reads the numeric server mode', () {
    final grant = GrantSummary.fromJson({
      'appId': 'intochat.leadgenerator',
      'semanticTypeId': 'person.birthDate',
      'mode': 0,
      'grantedAt': '2026-09-23T12:00:00+00:00',
    });

    expect(grant.mode, 'Once');
    expect(grant.grantedAt.year, 2026);
  });
}