import 'package:flutter/material.dart';

import '../../app/demo_flow_controller.dart';
import '../../core/widgets/flow_components.dart';
import '../../data/demo_catalog.dart';

class BookingConfirmationScreen extends StatelessWidget {
  const BookingConfirmationScreen({super.key, required this.flow});
  final DemoFlowController flow;
  @override
  Widget build(BuildContext context) => InformationCard(
    children: [
      if (flow.complete) ...[
        Icon(
          Icons.task_alt,
          size: 56,
          color: Theme.of(context).colorScheme.primary,
        ),
        const SizedBox(height: 16),
        Text(
          'Загварын урсгал дууслаа',
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: 8),
        const Text(
          'Цаг захиалга үүсгээгүй. Имэйл илгээгээгүй.',
          key: ValueKey('demo-complete'),
        ),
        const Divider(height: 32),
      ],
      DetailRow('Эмнэлэг', flow.clinic!.name),
      DetailRow('Үйлчилгээ', flow.service!.name),
      DetailRow('Эмч', flow.doctor!.name),
      DetailRow(
        'Огноо, цаг · жишээ',
        '2026.11.02 · ${flow.slot!.label(flow.clinic!)}',
      ),
      DetailRow('Цагийн бүс', flow.clinic!.timeZoneId),
      const DetailRow('Өвчтөн', DemoCatalog.patientName),
      const DetailRow('Жишээ имэйл', DemoCatalog.email),
      const Divider(height: 32),
      Text(
        flow.demoEmailVerified
            ? 'Имэйл: зөвхөн загвараар баталгаажсан'
            : 'Имэйл баталгаажуулах шаардлагатай',
      ),
      const SizedBox(height: 8),
      const Text(
        'Бодит хувилбарт захиалгын transaction амжилттай commit болсны дараа баталгаажуулах имэйл илгээнэ.',
      ),
    ],
  );
}
