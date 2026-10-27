import 'package:flutter/material.dart';

import '../../app/demo_flow_controller.dart';
import '../../core/widgets/flow_components.dart';

class DoctorSelectionScreen extends StatelessWidget {
  const DoctorSelectionScreen({super.key, required this.flow});
  final DemoFlowController flow;
  @override
  Widget build(BuildContext context) => Column(
    children: [
      for (final doctor in flow.doctors)
        SelectionCard(
          key: ValueKey('doctor-${doctor.id}'),
          title: doctor.name,
          subtitle:
              '${doctor.specialty}\n${flow.service!.name} · ${flow.clinic!.name}',
          icon: Icons.person_outline,
          selected: flow.doctor?.id == doctor.id,
          onTap: () => flow.selectDoctor(doctor.id),
        ),
      const Text(
        'Эмчийн танилцуулгын загвар. Бодит эмчийн мэдээлэл агуулаагүй.',
      ),
    ],
  );
}
