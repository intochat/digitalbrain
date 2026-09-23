import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

class InboxPanel extends StatefulWidget {
  const InboxPanel({
    super.key,
    required this.client,
    required this.workspaceId,
  });

  final DigitalBrainUiClient client;
  final String workspaceId;

  @override
  State<InboxPanel> createState() => _InboxPanelState();
}

class _InboxPanelState extends State<InboxPanel> {
  late final Stream<InboxSnapshot> _events = widget.client.watchInbox(
    widget.workspaceId,
  );

  Future<void> _resolve(InboxItem item) async {
    await widget.client.resolveInboxItem(widget.workspaceId, item.id);
  }

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 440,
    height: 540,
    child: StreamBuilder<InboxSnapshot>(
      stream: _events,
      builder: (context, async) {
        final snapshot = async.data;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 10),
              child: Row(
                children: [
                  const Icon(Icons.inbox_outlined, size: 20),
                  const SizedBox(width: 8),
                  Text(
                    'Inbox',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const Spacer(),
                  if (snapshot != null)
                    Text(
                      '${snapshot.unreadCount} unread',
                      style: Theme.of(context).textTheme.labelMedium,
                    ),
                ],
              ),
            ),
            const Divider(height: 1),
            Expanded(child: _body(context, async, snapshot)),
          ],
        );
      },
    ),
  );

  Widget _body(
    BuildContext context,
    AsyncSnapshot<InboxSnapshot> async,
    InboxSnapshot? snapshot,
  ) {
    if (snapshot == null) {
      if (async.hasError) {
        return Center(child: Text('Inbox unavailable. ${async.error}'));
      }
      return const Center(child: CircularProgressIndicator());
    }
    if (snapshot.items.isEmpty) {
      return const Center(child: Text('Nothing waiting.'));
    }
    return ListView.separated(
      itemCount: snapshot.items.length,
      separatorBuilder: (_, _) => const Divider(height: 1),
      itemBuilder: (context, index) {
        final item = snapshot.items[index];
        return ListTile(
          leading: Icon(
            item.isResolved
                ? Icons.check_circle_outline
                : Icons.notifications_none,
            size: 20,
          ),
          title: Text(item.count > 1 ? '${item.title} (${item.count})' : item.title),
          subtitle: item.detail == null ? null : Text(item.detail!),
          trailing: item.isResolved
              ? null
              : TextButton(
                  onPressed: () => _resolve(item),
                  child: const Text('Resolve'),
                ),
        );
      },
    );
  }
}
