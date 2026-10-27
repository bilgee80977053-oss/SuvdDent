import 'package:flutter/material.dart';

import '../../app/demo_flow_controller.dart';
import '../../core/widgets/flow_components.dart';
import '../../data/demo_catalog.dart';

class PatientProfileScreen extends StatelessWidget {
  const PatientProfileScreen({super.key, required this.flow});
  final DemoFlowController flow;
  @override
  Widget build(BuildContext context) => InformationCard(
    children: [
      SegmentedButton<DemoProfileMode>(
        segments: const [
          ButtonSegment(
            value: DemoProfileMode.create,
            label: Text('Шинэ профайл'),
          ),
          ButtonSegment(
            value: DemoProfileMode.retrieve,
            label: Text('Миний профайл'),
          ),
        ],
        selected: {flow.profileMode},
        onSelectionChanged: (values) => flow.setProfileMode(values.single),
      ),
      const SizedBox(height: 16),
      const DetailRow('Нэр', DemoCatalog.patientName),
      const DetailRow('Утас · жишээ', '00000000'),
      const DetailRow('Төрсөн огноо · жишээ', '2000.02.03'),
      DetailRow('Эмнэлэг', flow.clinic!.name),
      const Divider(),
      Text(
        flow.profileMode == DemoProfileMode.create
            ? 'Профайл үүсгэх үйлдлийн загвар. Өгөгдөл хадгалахгүй.'
            : 'Профайл авах үйлдлийн загвар. Серверээс мэдээлэл татахгүй.',
      ),
    ],
  );
}
