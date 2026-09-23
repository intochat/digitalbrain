class InboxAction {
  const InboxAction({required this.id, required this.label, this.route});

  final String id;
  final String label;
  final String? route;

  factory InboxAction.fromJson(Map<String, dynamic> json) => InboxAction(
    id: json['id'] as String? ?? '',
    label: json['label'] as String? ?? '',
    route: json['route'] as String?,
  );
}

class InboxItem {
  const InboxItem({
    required this.id,
    required this.kind,
    required this.title,
    required this.groupingKey,
    required this.actions,
    required this.isRead,
    required this.isResolved,
    required this.count,
    this.detail,
    this.deepLink,
  });

  final String id;
  final String kind;
  final String title;
  final String groupingKey;
  final List<InboxAction> actions;
  final bool isRead;
  final bool isResolved;
  final int count;
  final String? detail;
  final String? deepLink;

  factory InboxItem.fromJson(Map<String, dynamic> json) => InboxItem(
    id: json['id'] as String? ?? '',
    kind: _kindName(json['kind']),
    title: json['title'] as String? ?? '',
    groupingKey: json['groupingKey'] as String? ?? '',
    actions: [
      for (final action in json['actions'] as List? ?? const [])
        InboxAction.fromJson(Map<String, dynamic>.from(action as Map)),
    ],
    isRead: json['isRead'] as bool? ?? false,
    isResolved: json['isResolved'] as bool? ?? false,
    count: (json['count'] as num?)?.toInt() ?? 1,
    detail: json['detail'] as String?,
    deepLink: json['deepLink'] as String?,
  );

  // The read API serializes the enum as a number; the events stream serializes it as a name.
  static String _kindName(Object? value) {
    if (value is String) return value;
    const names = [
      'AutomationResult',
      'AutomationFailed',
      'ApprovalWaiting',
      'ConnectionExpired',
      'ComputeLimitAlert',
      'AppDisabled',
    ];
    if (value is num) {
      final index = value.toInt();
      if (index >= 0 && index < names.length) return names[index];
    }
    return 'Unknown';
  }
}

class InboxSnapshot {
  const InboxSnapshot({required this.items, required this.unreadCount});

  final List<InboxItem> items;
  final int unreadCount;

  factory InboxSnapshot.fromJson(Map<String, dynamic> json) => InboxSnapshot(
    items: [
      for (final item in json['items'] as List? ?? const [])
        InboxItem.fromJson(Map<String, dynamic>.from(item as Map)),
    ],
    unreadCount: (json['unreadCount'] as num?)?.toInt() ?? 0,
  );
}
