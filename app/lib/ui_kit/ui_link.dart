import 'package:flutter/widgets.dart';
import 'package:url_launcher/url_launcher.dart';

class UiKitLink extends StatelessWidget {
  final String label;
  final String url;

  const UiKitLink({required this.label, required this.url, super.key});

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: () async {
        final uri = Uri.tryParse(url);
        if (uri != null) {
          await launchUrl(uri, mode: LaunchMode.externalApplication);
        }
      },
      child: Text(
        label,
        style: const TextStyle(
          color: Color(0xFF0066CC),
          decoration: TextDecoration.underline,
        ),
      ),
    );
  }
}
