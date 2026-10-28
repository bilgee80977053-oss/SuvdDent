import 'package:flutter/material.dart';

import '../live/live_patient_flow.dart';
import '../live/models.dart';
import 'app_theme.dart';
import 'patient_flow.dart';

class NexoraDentalApp extends StatelessWidget {
  final ApiSettings settings;
  const NexoraDentalApp({super.key, this.settings = const ApiSettings()});

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Nexora Dental',
    debugShowCheckedModeBanner: false,
    theme: AppTheme.light,
    home: !settings.isLive
        ? const PatientFlow()
        : settings.error != null
        ? LiveConfigurationError(settings.error!)
        : LivePatientFlow(settings: settings),
  );
}
