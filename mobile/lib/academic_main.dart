import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import 'app/nexora_app.dart';
import 'live/models.dart';

void main() {
  const settings = AcademicApiSettings();
  if (settings.error case final error?) {
    runApp(
      MaterialApp(
        home: Scaffold(body: Center(child: Text(error))),
      ),
    );
    return;
  }
  runApp(const NexoraDentalApp(settings: settings));
}

/// This entry point alone supports the disposable academic HTTP API. The
/// ordinary release entry point and ApiSettings retain their HTTPS rules.
class AcademicApiSettings extends ApiSettings {
  const AcademicApiSettings()
    : super(baseUrl: 'http://127.0.0.1:5018', clinicId: 1);

  static bool acceptsBrowserOrigin(Uri origin) =>
      origin.scheme == 'http' &&
      (origin.host == '127.0.0.1' || origin.host == 'localhost') &&
      origin.port == 7357 &&
      origin.userInfo.isEmpty;

  // Also overrides the shared UI academic marker when the presentation
  // overlay is composed; this entry point works on its own without it.
  @override
  bool get isAcademic => true;

  @override
  String? get error => kIsWeb && acceptsBrowserOrigin(Uri.base)
      ? null
      : 'Сургалтын хувилбарыг http://localhost:7357 хаягаас нээнэ үү. '
            'Энэ хувилбар зөвхөн тухайн компьютерийн түр серверт холбогдоно.';
}
