import 'package:flutter/material.dart';

import '../../core/widgets/flow_components.dart';
import '../../data/demo_catalog.dart';

class EmailVerificationScreen extends StatelessWidget {
  const EmailVerificationScreen({super.key});
  @override
  Widget build(BuildContext context) => InformationCard(
    children: [
      Icon(
        Icons.mark_email_read_outlined,
        size: 56,
        color: Theme.of(context).colorScheme.primary,
      ),
      const SizedBox(height: 20),
      const DetailRow('Баталгаажуулах жишээ хаяг', DemoCatalog.email),
      const Text(
        'Үргэлжлүүлэх товч зөвхөн загварын баталгаажуулалтыг дуурайлгана. Код болон имэйл илгээхгүй.',
      ),
      const SizedBox(height: 16),
      const Text(
        'Бодит захиалгад серверээр баталгаажсан имэйл шаардана. Энэ загвар бодит эрх олгохгүй.',
      ),
    ],
  );
}
