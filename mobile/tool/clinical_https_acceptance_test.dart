import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/io_client.dart';
import 'package:nexora_dental_mobile/live/dental_api.dart';
import 'package:nexora_dental_mobile/live/encounter_treatment_api.dart';
import 'package:nexora_dental_mobile/live/models.dart';

void main() {
  test(
    'assigned clinician signs, replays and reads a synthetic procedure',
    () async {
      final origin = Uri.parse(
        Platform.environment['SUVDDENT_LOOPBACK_ORIGIN']!,
      );
      expect(origin.scheme, 'https');
      expect(origin.host, '127.0.0.1');
      expect(origin.hasPort, isTrue);

      final trust = SecurityContext(withTrustedRoots: false)
        ..setTrustedCertificatesBytes(
          Platform.environment['SUVDDENT_LOOPBACK_CERT_PEM']!.codeUnits,
        );
      final password =
          Platform.environment['SUVDDENT_LOOPBACK_FIXTURE_PASSWORD']!;
      EncounterTreatmentApi client(int clinic) {
        final socket = HttpClient(context: trust)..findProxy = (_) => 'DIRECT';
        final api = EncounterTreatmentApi(
          ApiSettings(baseUrl: origin.toString(), clinicId: clinic),
          client: IOClient(socket),
        );
        addTearDown(api.close);
        return api;
      }

      final doctor = client(101);
      await doctor.login('user13@example.invalid', password);
      expect(doctor.authenticated, isTrue);
      final foreignDoctor = client(202);
      await foreignDoctor.login('user23@example.invalid', password);
      const key = '98fcbaca-e637-4dad-a413-7aa83da9c331';

      Future<PerformedProcedureSignResult> sign(
        EncounterTreatmentApi api,
        String tooth,
      ) => api.signPerformedProcedure(
        301,
        requestId: key,
        serviceId: 51,
        scope: PerformedProcedureScope.singleTooth,
        toothCode: tooth,
      );

      final signed = await sign(doctor, '85');
      expect(signed, isA<PerformedProcedureRecorded>());
      expect((signed as PerformedProcedureRecorded).procedure.requestId, key);
      expect(await sign(doctor, '85'), isA<PerformedProcedureReplayed>());
      await expectLater(
        sign(doctor, '84'),
        throwsA(
          isA<ApiFailure>().having((failure) => failure.status, 'status', 409),
        ),
      );
      await expectLater(
        foreignDoctor.listPerformedProcedures(301),
        throwsA(
          isA<ApiFailure>().having((failure) => failure.status, 'status', 404),
        ),
      );

      final page = await doctor.listPerformedProcedures(301);
      expect(page.items.single.requestId, key);
      expect(page.items.single.serviceLabel, 'Synthetic booked service');
      expect(page.items.single.toothCode, '85');
      doctor.logout();
      expect(doctor.authenticated, isFalse);
    },
  );
}
