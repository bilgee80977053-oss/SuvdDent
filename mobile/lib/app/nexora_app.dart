import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';

import '../live/live_patient_flow.dart';
import '../live/director_catalog_screen.dart';
import '../live/models.dart';
import 'app_theme.dart';
import 'patient_flow.dart';

class NexoraDentalApp extends StatelessWidget {
  static const _appMode = String.fromEnvironment('NEXORA_APP_MODE');
  final ApiSettings settings;
  const NexoraDentalApp({super.key, this.settings = const ApiSettings()});

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Suvd Dent | NEXORA',
    debugShowCheckedModeBanner: false,
    theme: AppTheme.light,
    home: !settings.isLive
        ? kReleaseMode
            ? const LiveConfigurationError(
                'Suvd Dent-ийн LIVE API хаяг болон эмнэлгийн ID тохируулаагүй байна. '
                'Release апп DEMO өгөгдөл рүү шилжихгүй.',
              )
            : const PatientFlow()
        : settings.error != null
        ? LiveConfigurationError(settings.error!)
        : _appMode == 'director'
        ? DirectorCatalogScreen(settings: settings)
        : LivePatientFlow(settings: settings),
  );
}
