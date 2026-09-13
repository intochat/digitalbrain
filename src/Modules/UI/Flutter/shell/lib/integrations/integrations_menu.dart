import 'package:flutter/material.dart';

typedef OpenUrl = Future<void> Function(Uri url);

const _providers = [
  ('gmail', 'Gmail', Icons.mail_outline),
  ('salesforce', 'Salesforce', Icons.cloud_outlined),
  ('github', 'GitHub', Icons.code),
];

final class IntegrationsMenu extends StatelessWidget {
  const IntegrationsMenu({super.key, required this.kernelBaseUri, this.onOpen});

  final Uri? kernelBaseUri;
  final OpenUrl? onOpen;

  @override
  Widget build(BuildContext context) => Column(
    mainAxisSize: MainAxisSize.min,
    children: [
      for (final (id, label, icon) in _providers)
        ListTile(
          key: Key('integration_$id'),
          leading: Icon(icon),
          title: Text(label),
          onTap: kernelBaseUri == null || onOpen == null
              ? null
              : () =>
                    onOpen!(kernelBaseUri!.resolve('/integrations/$id/login')),
        ),
    ],
  );
}
