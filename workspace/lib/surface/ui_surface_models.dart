class UiAction {
  const UiAction({
    required this.id,
    required this.label,
    required this.expectedRevision,
  });

  final String id;
  final String label;
  final int expectedRevision;

  factory UiAction.fromJson(Map<String, dynamic> json) {
    final id = json['id'];
    final label = json['label'];
    final expectedRevision = json['expectedRevision'];
    if (id is! String || label is! String) {
      throw const FormatException('ui action missing id or label');
    }
    if (expectedRevision is! int) {
      throw const FormatException('ui action missing expectedRevision');
    }
    return UiAction(id: id, label: label, expectedRevision: expectedRevision);
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'label': label,
    'expectedRevision': expectedRevision,
  };
}

class UiBlock {
  const UiBlock({
    required this.kind,
    required this.text,
    required this.actions,
  });

  static const Set<String> supportedKinds = {'text', 'failure'};

  final String kind;
  final String text;
  final List<UiAction> actions;

  bool get isSupported => supportedKinds.contains(kind);

  factory UiBlock.fromJson(Map<String, dynamic> json) {
    final kind = json['kind'];
    final text = json['text'];
    final rawActions = json['actions'];
    if (kind is! String || text is! String) {
      throw const FormatException('ui block missing kind or text');
    }
    if (rawActions is! List) {
      throw const FormatException('ui block missing actions');
    }
    final actions = rawActions.map((entry) {
      if (entry is! Map<String, dynamic>) {
        throw const FormatException('ui action must be an object');
      }
      return UiAction.fromJson(entry);
    }).toList();
    return UiBlock(kind: kind, text: text, actions: actions);
  }

  Map<String, dynamic> toJson() => {
    'kind': kind,
    'text': text,
    'actions': actions.map((action) => action.toJson()).toList(),
  };

  UiBlock copyWith({String? kind, String? text, List<UiAction>? actions}) {
    return UiBlock(
      kind: kind ?? this.kind,
      text: text ?? this.text,
      actions: actions ?? this.actions,
    );
  }
}

class UiSurface {
  const UiSurface({
    required this.surfaceId,
    required this.revision,
    required this.blocks,
  });

  final String surfaceId;
  final int revision;
  final List<UiBlock> blocks;

  factory UiSurface.fromJson(Map<String, dynamic> json) {
    final surfaceId = json['surfaceId'];
    final revision = json['revision'];
    final rawBlocks = json['blocks'];
    if (surfaceId is! String) {
      throw const FormatException('ui surface missing surfaceId');
    }
    if (revision is! int) {
      throw const FormatException('ui surface missing revision');
    }
    if (rawBlocks is! List) {
      throw const FormatException('ui surface missing blocks');
    }
    final blocks = rawBlocks.map((entry) {
      if (entry is! Map<String, dynamic>) {
        throw const FormatException('ui block must be an object');
      }
      return UiBlock.fromJson(entry);
    }).toList();
    return UiSurface(
      surfaceId: surfaceId,
      revision: revision,
      blocks: blocks,
    );
  }

  Map<String, dynamic> toJson() => {
    'surfaceId': surfaceId,
    'revision': revision,
    'blocks': blocks.map((block) => block.toJson()).toList(),
  };

  UiSurface copyWith({int? revision, List<UiBlock>? blocks}) {
    return UiSurface(
      surfaceId: surfaceId,
      revision: revision ?? this.revision,
      blocks: blocks ?? this.blocks,
    );
  }
}

class UiSurfaceSnapshot {
  const UiSurfaceSnapshot({required this.surface});

  final UiSurface surface;

  factory UiSurfaceSnapshot.fromJson(Map<String, dynamic> json) {
    final surface = json['surface'];
    if (surface is! Map<String, dynamic>) {
      throw const FormatException('ui snapshot missing surface');
    }
    return UiSurfaceSnapshot(surface: UiSurface.fromJson(surface));
  }
}

class UiPatchOperation {
  const UiPatchOperation({
    required this.op,
    required this.path,
    required this.value,
  });

  final String op;
  final String path;
  final String value;

  factory UiPatchOperation.fromJson(Map<String, dynamic> json) {
    final op = json['op'];
    final path = json['path'];
    final value = json['value'];
    if (op is! String || path is! String || value is! String) {
      throw const FormatException('ui patch operation invalid');
    }
    return UiPatchOperation(op: op, path: path, value: value);
  }
}

class UiSurfacePatch {
  const UiSurfacePatch({
    required this.surfaceId,
    required this.fromRevision,
    required this.toRevision,
    required this.operations,
  });

  final String surfaceId;
  final int fromRevision;
  final int toRevision;
  final List<UiPatchOperation> operations;

  factory UiSurfacePatch.fromJson(Map<String, dynamic> json) {
    final surfaceId = json['surfaceId'];
    final fromRevision = json['fromRevision'];
    final toRevision = json['toRevision'];
    final rawOps = json['operations'];
    if (surfaceId is! String) {
      throw const FormatException('ui patch missing surfaceId');
    }
    if (fromRevision is! int || toRevision is! int) {
      throw const FormatException('ui patch missing revisions');
    }
    if (rawOps is! List) {
      throw const FormatException('ui patch missing operations');
    }
    final operations = rawOps.map((entry) {
      if (entry is! Map<String, dynamic>) {
        throw const FormatException('ui patch operation must be an object');
      }
      return UiPatchOperation.fromJson(entry);
    }).toList();
    return UiSurfacePatch(
      surfaceId: surfaceId,
      fromRevision: fromRevision,
      toRevision: toRevision,
      operations: operations,
    );
  }
}

sealed class UiFeedMessage {
  const UiFeedMessage({required this.schemaVersion, required this.sequence});

  static const int supportedSchemaVersion = 1;

  final int schemaVersion;
  final int sequence;

  static UiFeedMessage parse(Map<String, dynamic> json) {
    final schemaVersion = json['schemaVersion'];
    if (schemaVersion is! int || schemaVersion != supportedSchemaVersion) {
      throw const FormatException('unsupported schema version');
    }
    final type = json['type'];
    if (type is! String) {
      throw const FormatException('feed frame missing type');
    }
    final sequence = json['sequence'];
    if (sequence is! int || sequence < 0) {
      throw const FormatException('invalid feed sequence');
    }
    switch (type) {
      case 'snapshot':
        return UiSnapshotMessage(
          schemaVersion: schemaVersion,
          sequence: sequence,
          snapshot: UiSurfaceSnapshot.fromJson(json),
        );
      case 'patch':
        return UiPatchMessage(
          schemaVersion: schemaVersion,
          sequence: sequence,
          patch: UiSurfacePatch.fromJson(json),
        );
      case 'failure':
        final text = json['text'];
        if (text is! String) {
          throw const FormatException('failure frame missing text');
        }
        return UiFailureMessage(
          schemaVersion: schemaVersion,
          sequence: sequence,
          text: text,
        );
      default:
        throw FormatException('unsupported feed type $type');
    }
  }

  static UiFeedMessage? tryParse(Map<String, dynamic> json) {
    try {
      return parse(json);
    } on FormatException {
      return null;
    }
  }
}

class UiSnapshotMessage extends UiFeedMessage {
  const UiSnapshotMessage({
    required super.schemaVersion,
    required super.sequence,
    required this.snapshot,
  });

  final UiSurfaceSnapshot snapshot;
}

class UiPatchMessage extends UiFeedMessage {
  const UiPatchMessage({
    required super.schemaVersion,
    required super.sequence,
    required this.patch,
  });

  final UiSurfacePatch patch;
}

class UiFailureMessage extends UiFeedMessage {
  const UiFailureMessage({
    required super.schemaVersion,
    required super.sequence,
    required this.text,
  });

  final String text;
}
