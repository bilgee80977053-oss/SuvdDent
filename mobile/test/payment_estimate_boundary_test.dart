import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexora_dental_mobile/live/dental_api.dart';
import 'package:nexora_dental_mobile/live/models.dart';
import 'package:nexora_dental_mobile/live/payment_estimate_screen.dart';

Map<String, dynamic> quote({int appointment = 41, int service = 5}) => {
  'appointmentId': appointment,
  'serviceId': service,
  'serviceName': 'Synthetic treatment $appointment',
  'currency': 'MNT',
  'priceSource': 'ClinicServiceCatalog',
  'priceExplanation': 'Synthetic nonpayable estimate',
  'paymentState': 'NotTracked',
  'receiptState': 'NotIssued',
  'isFinalAmount': false,
  'minPriceMnt': 300,
  'maxPriceMnt': 500,
  'totalDueMnt': null,
};

http.Response jsonReply(Object body) => http.Response(
  jsonEncode(body),
  200,
  headers: {'content-type': 'application/json'},
);

Future<DentalApi> patient(
  FutureOr<http.Response> Function(http.Request) estimate,
) async {
  final api = DentalApi(
    const ApiSettings(baseUrl: 'https://pricing.example.invalid', clinicId: 7),
    client: MockClient(
      (request) async => request.url.path == '/api/Auth/login'
          ? jsonReply({
              'token': 'synthetic-session',
              'role': 'Patient',
              'fullName': 'Synthetic',
              'userId': 3,
              'clinicId': 7,
            })
          : await estimate(request),
    ),
  );
  addTearDown(api.close);
  await api.login('fixture@example.invalid', 'synthetic-password');
  return api;
}

Appointment appointment(int id, int service) => Appointment.fromJson({
  'id': id,
  'serviceId': service,
  'doctorProfileId': 9,
  'startUtc': '2026-11-28T01:00:00Z',
  'endUtc': '2026-11-28T01:30:00Z',
  'occupiedStartUtc': '2026-11-28T01:00:00Z',
  'occupiedEndUtc': '2026-11-28T01:30:00Z',
  'createdAtUtc': '2026-11-28T00:00:00Z',
  'clinicTimeZoneId': 'Asia/Ulaanbaatar',
  'status': 'Confirmed',
});

void main() {
  test('nonpayable estimate rejects payment, receipt and invalid ranges', () {
    for (final change in <Map<String, dynamic>>[
      {'isFinalAmount': true},
      {'totalDueMnt': 0},
      {'paymentState': 'Paid'},
      {'receiptState': 'Issued'},
      {'priceSource': 'Invoice'},
      {'maxPriceMnt': 200},
      {'minPriceMnt': null},
      {'minPriceMnt': 0},
    ]) {
      expect(
        () => PaymentEstimate.fromJson({...quote(), ...change}),
        throwsFormatException,
      );
    }
    final undisclosed = PaymentEstimate.fromJson({
      ...quote(),
      'minPriceMnt': null,
      'maxPriceMnt': null,
    });
    expect(undisclosed.minPriceMnt, isNull);
    expect(undisclosed.totalDueMnt, isNull);
  });

  test(
    'scoped estimate request is no-store and does not follow redirects',
    () async {
      final api = await patient((request) {
        expect(request.method, 'GET');
        expect(request.url.path, '/api/appointments/41/payment-estimate');
        expect(request.headers['authorization'], 'Bearer synthetic-session');
        expect(request.headers['cache-control'], 'no-store');
        expect(request.followRedirects, isFalse);
        return jsonReply(quote());
      });
      expect(
        (await api.paymentEstimate(41, expectedServiceId: 5)).serviceId,
        5,
      );
    },
  );

  test(
    'response for another appointment or service never becomes an estimate',
    () async {
      var value = quote(appointment: 42);
      final api = await patient((_) => jsonReply(value));
      await expectLater(api.paymentEstimate(41), throwsA(isA<ApiFailure>()));
      value = quote(service: 6);
      await expectLater(
        api.paymentEstimate(41, expectedServiceId: 5),
        throwsA(isA<ApiFailure>()),
      );
    },
  );

  testWidgets('switching appointments never displays a late old estimate', (
    tester,
  ) async {
    final late = Completer<http.Response>();
    final api = (await tester.runAsync(
      () => patient((request) {
        if (request.url.path == '/api/appointments/41/payment-estimate')
          return late.future;
        expect(request.url.path, '/api/appointments/42/payment-estimate');
        return jsonReply(quote(appointment: 42, service: 6));
      }),
    ))!;
    Future<void> show(int id, int service) => tester.pumpWidget(
      MaterialApp(
        home: PaymentEstimateScreen(
          api: api,
          appointment: appointment(id, service),
        ),
      ),
    );
    await show(41, 5);
    await show(42, 6);
    await tester.pumpAndSettle();
    expect(find.text('Synthetic treatment 42'), findsOneWidget);
    late.complete(jsonReply(quote()));
    await tester.pumpAndSettle();
    expect(find.text('Synthetic treatment 41'), findsNothing);
    expect(find.text('Synthetic treatment 42'), findsOneWidget);
  });
}
