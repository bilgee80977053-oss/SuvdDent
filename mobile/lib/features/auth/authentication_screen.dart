import 'package:flutter/material.dart';

import '../../app/demo_flow_controller.dart';
import '../../core/widgets/flow_components.dart';
import '../../data/demo_catalog.dart';

class AuthenticationScreen extends StatelessWidget {
  const AuthenticationScreen({super.key, required this.flow});
  final DemoFlowController flow;
  @override
  Widget build(BuildContext context) => InformationCard(
    children: [
      SegmentedButton<DemoAuthMode>(
        segments: const [
          ButtonSegment(value: DemoAuthMode.login, label: Text('Нэвтрэх')),
          ButtonSegment(
            value: DemoAuthMode.registration,
            label: Text('Бүртгүүлэх'),
          ),
        ],
        selected: {flow.authMode},
        onSelectionChanged: (values) => flow.setAuthMode(values.single),
      ),
      const SizedBox(height: 20),
      DetailRow('Сонгосон эмнэлэг', flow.clinic!.name),
      const DetailRow('Жишээ имэйл', DemoCatalog.email),
      const Divider(),
      Text(
        flow.authMode == DemoAuthMode.login
            ? 'Нэвтрэх дэлгэцийн загвар'
            : 'Шинэ бүртгэлийн дэлгэцийн загвар',
        style: Theme.of(context).textTheme.titleMedium,
      ),
      const SizedBox(height: 8),
      const Text(
        'Бодит нууц үг оруулах шаардлагагүй. Энэ хувилбар хэрэглэгч үүсгэхгүй, нэвтрэлт хийхгүй, токен хадгалахгүй.',
      ),
    ],
  );
}
