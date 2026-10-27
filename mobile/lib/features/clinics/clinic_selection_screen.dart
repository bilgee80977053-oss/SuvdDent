import 'package:flutter/material.dart';

import '../../app/demo_flow_controller.dart';
import '../../core/widgets/flow_components.dart';
import '../../data/demo_catalog.dart';

class ClinicSelectionScreen extends StatelessWidget {
  const ClinicSelectionScreen({super.key, required this.flow});
  final DemoFlowController flow;
  @override
  Widget build(BuildContext context) => Column(
    children: [
      for (final clinic in DemoCatalog.clinics)
        SelectionCard(
          key: ValueKey('clinic-${clinic.id}'),
          title: clinic.name,
          subtitle: clinic.location,
          icon: Icons.local_hospital_outlined,
          selected: flow.clinic?.id == clinic.id,
          onTap: () => flow.selectClinic(clinic.id),
        ),
      const Text('Бүх салбар, эмч, үйлчилгээ нь сургалтын зохиомол өгөгдөл.'),
    ],
  );
}
