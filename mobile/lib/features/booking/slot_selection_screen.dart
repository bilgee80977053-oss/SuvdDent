import 'package:flutter/material.dart';

import '../../app/demo_flow_controller.dart';
import '../../core/widgets/flow_components.dart';

class SlotSelectionScreen extends StatelessWidget {
  const SlotSelectionScreen({super.key, required this.flow});
  final DemoFlowController flow;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      InformationCard(
        children: [
          const DetailRow('Загварын тогтмол огноо', '2026.11.02'),
          DetailRow('Эмнэлгийн цагийн бүс', flow.clinic!.timeZoneId),
          const Text(
            'Эдгээр нь бодит сул цаг биш. Серверийн хуваарь, давхардлыг хараахан шалгахгүй.',
          ),
        ],
      ),
      const SizedBox(height: 16),
      for (final slot in flow.slots)
        SelectionCard(
          key: ValueKey('slot-${slot.id}'),
          title: slot.label(flow.clinic!),
          subtitle: '${flow.service!.durationMinutes} минут · жишээ цаг',
          icon: Icons.schedule,
          selected: flow.slot?.id == slot.id,
          onTap: () => flow.selectSlot(slot.id),
        ),
    ],
  );
}
