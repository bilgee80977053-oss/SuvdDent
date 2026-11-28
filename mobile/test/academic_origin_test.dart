import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental_mobile/academic_main.dart';

void main() {
  test('academic web entry is limited to the exact local web server', () {
    for (final value in ['http://localhost:7357/', 'http://127.0.0.1:7357/']) {
      expect(
        AcademicApiSettings.acceptsBrowserOrigin(Uri.parse(value)),
        isTrue,
      );
    }
    for (final value in [
      'https://localhost:7357/',
      'http://localhost/',
      'http://localhost:7358/',
      'http://admin@localhost:7357/',
      'http://127.0.0.2:7357/',
      'http://localhost.example.invalid:7357/',
      'file:///tmp/academic.html',
    ]) {
      expect(
        AcademicApiSettings.acceptsBrowserOrigin(Uri.parse(value)),
        isFalse,
      );
    }
    expect(const AcademicApiSettings().clinicId, 1);
    expect(const AcademicApiSettings().baseUrl, 'http://127.0.0.1:5018');
  });
}
