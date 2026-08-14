import 'dart:convert';

final class ProductModule {
  const ProductModule({
    required this.id,
    required this.displayName,
    required this.status,
    this.setupMessage,
  });

  final String id;
  final String displayName;
  final int status;
  final String? setupMessage;

  bool get isReady => status == 0;
  String get statusLabel => switch (status) {
    0 => 'Ready',
    1 => 'Needs setup',
    _ => 'Unavailable',
  };

  factory ProductModule.fromJson(Map<String, Object?> json) => ProductModule(
    id: json['id'] as String,
    displayName: json['displayName'] as String,
    status: json['status'] as int,
    setupMessage: json['setupMessage'] as String?,
  );
}

final class ProductOperation {
  const ProductOperation({
    required this.id,
    required this.moduleId,
    required this.displayName,
    required this.inputSchema,
    required this.resultSchema,
  });

  final String id;
  final String moduleId;
  final String displayName;
  final String inputSchema;
  final String resultSchema;

  factory ProductOperation.fromJson(Map<String, Object?> json) =>
      ProductOperation(
        id: json['id'] as String,
        moduleId: json['moduleId'] as String,
        displayName: json['displayName'] as String,
        inputSchema: json['inputSchema'] as String,
        resultSchema: json['resultSchema'] as String,
      );
}

final class ProductActivityReceipt {
  const ProductActivityReceipt({
    required this.activity,
    required this.operationId,
  });

  final String activity;
  final String operationId;

  factory ProductActivityReceipt.fromJson(Map<String, Object?> json) =>
      ProductActivityReceipt(
        activity: json['activity'] as String,
        operationId: json['operationId'] as String,
      );
}

final class ProductActivity {
  const ProductActivity({
    required this.activity,
    required this.operationId,
    required this.workspace,
    required this.status,
    required this.sequence,
    this.resultJson,
    this.problem,
  });

  final String activity;
  final String operationId;
  final String workspace;
  final int status;
  final int sequence;
  final String? resultJson;
  final String? problem;

  bool get isTerminal => status == 2 || status == 3;
  bool get isCompleted => status == 2;
  Object? get result => resultJson == null ? null : jsonDecode(resultJson!);
  String get statusLabel => switch (status) {
    0 => 'Accepted',
    1 => 'Running',
    2 => 'Completed',
    _ => 'Failed',
  };

  factory ProductActivity.fromJson(Map<String, Object?> json) =>
      ProductActivity(
        activity: json['activity'] as String,
        operationId: json['operationId'] as String,
        workspace: json['workspace'] as String,
        status: json['status'] as int,
        sequence: json['sequence'] as int,
        resultJson: json['resultJson'] as String?,
        problem: json['problem'] as String?,
      );
}
