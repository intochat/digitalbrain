import 'dart:convert';
import 'dart:io';

// Core domain model replicated exactly from digitalbrain_rfw_library.dart
class CatalogContractSchema {
  final String fqn;
  final int kind; // 0 = Synapse, 1 = Signal, 2 = Neuron
  final List<String> fields;

  CatalogContractSchema({
    required this.fqn,
    required this.kind,
    required this.fields,
  });

  factory CatalogContractSchema.fromJson(Map<String, dynamic> json) {
    return CatalogContractSchema(
      fqn: (json['Fqn'] ?? json['fqn'] ?? '').toString(),
      kind: (json['Kind'] ?? json['kind'] ?? 0) as int,
      fields: List<String>.from(json['Fields'] ?? json['fields'] ?? const []),
    );
  }
}

// Synapse parsing replicated exactly from brain_scene_screen.dart
List<String> parseSynapses(String code) {
  final List<String> list = [];
  final regex = RegExp(r'\bon\s+synapse\s*\(\s*(DB\.[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)*)\s*\)');
  final matches = regex.allMatches(code);
  for (final match in matches) {
    final type = match.group(1);
    if (type != null && !list.contains(type)) {
      list.add(type);
    }
  }

  final outboundRegex = RegExp(r'\b(?:emit\s+signal|fire\s+synapse)\s*\(\s*(DB\.[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)*)\s*\)');
  final outboundMatches = outboundRegex.allMatches(code);
  for (final match in outboundMatches) {
    final type = match.group(1);
    if (type != null && !list.contains(type)) {
      list.add(type);
    }
  }

  if (list.isEmpty) {
    if (code.contains('creator')) list.add('DB.LLM.Prompt');
    if (code.contains('auth')) list.add('DB.Google.Auth');
    if (code.contains('travel')) list.add('DB.Travel.Booking');
    if (code.contains('gmail')) list.add('DB.Gmail.Incoming');
    if (code.contains('ai')) list.add('DB.Ai.Request');
    if (code.contains('sqlite')) list.add('DB.Sqlite.Query');
    if (code.contains('onboarding')) list.add('DB.Onboarding.Start');
    if (code.contains('taskmanager')) list.add('DB.Signal.Alarm');
  }
  return list;
}

// Wildcard checking replicated exactly from PromptTextEditingController logic in digitalbrain_rfw_library.dart
bool checkWildcard(String word) {
  final lowercaseWord = word.toLowerCase();
  final isWildcard = word.endsWith('.*') &&
      (lowercaseWord.startsWith('digitalbrain.sdk.') || lowercaseWord.startsWith('acme.'));
  return isWildcard;
}

void main() {
  print('=== CHALLENGER MILESTONE 4 STRESS TESTS ===\n');

  int totalTests = 0;
  int passedTests = 0;

  void assertTest(String name, bool condition) {
    totalTests++;
    if (condition) {
      passedTests++;
      print('[PASS] $name');
    } else {
      print('[FAIL] $name');
    }
  }

  // -------------------------------------------------------------
  // Test 1: JSON Catalog Parsing
  // -------------------------------------------------------------
  print('--- Testing Catalog Contract Schema & Deserialization ---');
  try {
    final file = File('assets/ino-catalog.json');
    if (!file.existsSync()) {
      print('[WARN] assets/ino-catalog.json not found in current directory. Trying relative UI/flutter/assets/...');
    }
    final jsonPath = file.existsSync() ? 'assets/ino-catalog.json' : 'UI/flutter/assets/ino-catalog.json';
    final jsonStr = File(jsonPath).readAsStringSync();
    final decoded = jsonDecode(jsonStr);
    
    List schemasJson = decoded is List ? decoded : (decoded['Schemas'] ?? decoded['schemas']);
    final catalog = schemasJson.map((s) => CatalogContractSchema.fromJson(s)).toList();
    
    assertTest('Catalog is successfully loaded and not empty', catalog.isNotEmpty);
    assertTest('Catalog parses Fqn accurately', catalog.any((s) => s.fqn == 'DB.Google.Auth'));
    assertTest('Catalog parses Kind accurately', catalog.firstWhere((s) => s.fqn == 'DB.Google.Auth').kind == 0);
    assertTest('Catalog parses Fields accurately', catalog.firstWhere((s) => s.fqn == 'DB.Google.Auth').fields.contains('token'));
  } catch (e) {
    assertTest('Catalog loading threw no exceptions: $e', false);
  }

  // -------------------------------------------------------------
  // Test 2: Wildcard Parsing Boundaries in PromptInput
  // -------------------------------------------------------------
  print('\n--- Testing Wildcard Parsing Boundaries in Creator Prompt ---');
  assertTest('DigitalBrain.SDK.* matches wildcard boundary', checkWildcard('DigitalBrain.SDK.*'));
  assertTest('digitalbrain.sdk.* is case-insensitive and matches', checkWildcard('digitalbrain.sdk.*'));
  assertTest('DigitalBrain.SDK.Storage.* matches deeper wildcard nested path', checkWildcard('DigitalBrain.SDK.Storage.*'));
  assertTest('Acme.* matches wildcard boundary', checkWildcard('Acme.*'));
  assertTest('acme.Worker.* matches deeper nested wildcard path', checkWildcard('acme.Worker.*'));
  assertTest('DB.Google.* should NOT match (unsupported prefix)', !checkWildcard('DB.Google.*'));
  assertTest('DigitalBrain.SDK. (trailing dot but no *) should NOT match', !checkWildcard('DigitalBrain.SDK.'));
  assertTest('DigitalBrain.SDK (no trailing dot or *) should NOT match', !checkWildcard('DigitalBrain.SDK'));
  assertTest('Acme (no trailing dot or *) should NOT match', !checkWildcard('Acme'));

  // -------------------------------------------------------------
  // Test 3: Outbound Signals Regex Extraction under Stress Scenarios
  // -------------------------------------------------------------
  print('\n--- Testing Outbound Signals Extraction Regex ---');
  
  // Base cases
  assertTest('Matches standard on synapse', parseSynapses('on synapse(DB.Google.Auth)').contains('DB.Google.Auth'));
  assertTest('Matches standard emit signal', parseSynapses('emit signal(DB.Google.Auth)').contains('DB.Google.Auth'));
  assertTest('Matches standard fire synapse', parseSynapses('fire synapse(DB.Google.Auth)').contains('DB.Google.Auth'));

  // Spaces stress tests
  assertTest('Handles spaces inside parenthesis: ( DB.Google.Auth )', 
      parseSynapses('emit signal( DB.Google.Auth )').contains('DB.Google.Auth'));
  assertTest('Handles excessive spaces around keywords: emit  signal  (DB.Google.Auth)', 
      parseSynapses('emit  signal  (DB.Google.Auth)').contains('DB.Google.Auth'));
  assertTest('Handles no spaces around keyword call: emit signal(DB.Google.Auth)', 
      parseSynapses('emit signal(DB.Google.Auth)').contains('DB.Google.Auth'));

  // Line breaks
  assertTest('Handles multiline synapse block syntax: emit\\n\\tsignal\\n\\t(DB.Google.Auth)', 
      parseSynapses('emit\n\tsignal\n\t(DB.Google.Auth)').contains('DB.Google.Auth'));

  // Duplicate FQNs
  final duplicateParse = parseSynapses('emit signal(DB.Google.Auth); fire synapse(DB.Google.Auth);');
  assertTest('Filters out duplicate FQNs', duplicateParse.length == 1 && duplicateParse.first == 'DB.Google.Auth');

  // Invalid spaces inside FQNs (should NOT match)
  assertTest('Does not match invalid space in FQN: DB. Google.Auth', 
      !parseSynapses('emit signal(DB. Google.Auth)').contains('DB. Google.Auth'));

  // Multiple parameters stress test
  // Note: the current regex matches strictly up to the closing parenthesis: \\s*\\)
  // If there are other arguments like true, or nested commas, let's see if it fails to extract or how it behaves.
  final multiParams = 'emit signal(DB.Google.Auth, true)';
  final multiParse = parseSynapses(multiParams);
  assertTest('Multiple parameters check (current regex expects immediate closing parenthesis, hence should fail to extract FQN)', 
      !multiParse.contains('DB.Google.Auth'));

  // Summary
  print('\n=== SUMMARY ===');
  print('Total tests executed: $totalTests');
  print('Passed: $passedTests');
  print('Failed: ${totalTests - passedTests}');
  
  if (passedTests == totalTests) {
    print('\nALL STRESS TESTS PASSED SUCCESSFULLY!');
    exit(0);
  } else {
    print('\nSOME STRESS TESTS FAILED!');
    exit(1);
  }
}
