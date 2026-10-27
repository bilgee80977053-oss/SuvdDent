import 'package:flutter/material.dart';

import 'app_theme.dart';
import 'patient_flow.dart';

class NexoraDentalApp extends StatelessWidget {
  const NexoraDentalApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Nexora Dental',
    debugShowCheckedModeBanner: false,
    theme: AppTheme.light,
    home: const PatientFlow(),
  );
}
