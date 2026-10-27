import 'package:flutter/material.dart';

import '../../app/demo_flow_controller.dart';
import '../../core/widgets/flow_components.dart';

class ServiceSelectionScreen extends StatelessWidget {
  const ServiceSelectionScreen({super.key, required this.flow});
  final DemoFlowController flow;
  @override
  Widget build(BuildContext context) => Column(
    children: [
      for (final service in flow.services)
        SelectionCard(
          key: ValueKey('service-${service.id}'),
          title: service.name,
          subtitle: '${service.description} · ${service.durationMinutes} мин',
          icon: Icons.health_and_safety_outlined,
          selected: flow.service?.id == service.id,
          onTap: () => flow.selectService(service.id),
        ),
    ],
  );
}
