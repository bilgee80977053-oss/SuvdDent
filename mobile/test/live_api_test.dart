import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexora_dental_mobile/app/nexora_app.dart';
import 'package:nexora_dental_mobile/app/patient_flow.dart';
import 'package:nexora_dental_mobile/live/dental_api.dart';
import 'package:nexora_dental_mobile/live/live_flow_controller.dart';
import 'package:nexora_dental_mobile/live/live_patient_flow.dart';
import 'package:nexora_dental_mobile/live/models.dart';

const settings = ApiSettings(
  baseUrl: 'https://development.invalid',
  clinicId: 7,
);
const authJson = {
  'token': 'synthetic-test-token',
  'userId': 9,
  'fullName': 'Тест',
  'role': 'Patient',
  'clinicId': 7,
};
const profileJson = {
  'id': 11,
  'fullName': 'Тест',
  'phone': '00000000',
  'dateOfBirth': null,
  'address': null,
};
const serviceJson = {
  'id': 3,
  'name': 'Тест үйлчилгээ',
  'description': '',
  'imagePath': null,
  'minPrice': 0,
  'maxPrice': null,
};
const doctorJson = {
  'id': 5,
  'fullName': 'Тест эмч',
  'specialty': 'Тест',
  'practiceStartYear': 2020,
  'biography': null,
  'photoPath': null,
};
const slotJson = {
  'startUtc': '2030-01-02T01:00:00Z',
  'endUtc': '2030-01-02T01:30:00Z',
};
const appointmentJson = {
  'id': 41,
  'doctorProfileId': 5,
  'serviceId': 3,
  'startUtc': '2030-01-02T01:00:00Z',
  'endUtc': '2030-01-02T01:30:00Z',
  'occupiedStartUtc': '2030-01-02T01:00:00Z',
  'occupiedEndUtc': '2030-01-02T01:30:00Z',
  'createdAtUtc': '2030-01-01T01:00:00Z',
  'clinicTimeZoneId': 'Asia/Ulaanbaatar',
  'status': 'Confirmed',
};
http.Response response(Object value, [int status = 200]) => http.Response(
  jsonEncode(value),
  status,
  headers: {'content-type': 'application/json; charset=utf-8'},
);
http.Response standard(http.Request request) {
  return switch (request.url.path) {
    '/api/Auth/login' => response(authJson),
    '/api/Auth/me' => response({
      'userId': '9',
      'name': 'Тест',
      'role': 'Patient',
      'clinicId': '7',
    }),
    '/api/Patients/me' => response(
      profileJson,
      request.method == 'POST' ? 201 : 200,
    ),
    '/api/appointments/services' => response([serviceJson]),
    '/api/appointments/doctors' => response([doctorJson]),
    '/api/appointments/slots' => response({
      'date': '2030-01-02',
      'timeZoneId': 'Asia/Ulaanbaatar',
      'slots': [slotJson],
    }),
    '/api/appointments/me' => response({
      'page': int.parse(request.url.queryParameters['page']!),
      'pageSize': 20,
      'items': [appointmentJson],
    }),
    '/api/appointments' => response(appointmentJson, 201),
    _ => response({'message': 'Олдсонгүй.'}, 404),
  };
}

DentalApi makeApi(
  FutureOr<http.Response> Function(http.Request) handler, {
  Duration timeout = const Duration(seconds: 1),
}) => DentalApi(
  settings,
  client: MockClient((r) async => handler(r)),
  timeout: timeout,
);
Future<void> prepare(LiveFlowController f) async {
  f.enterClinic();
  await f.login('synthetic@example.invalid', 'synthetic-password');
  await f.loadProfile();
  await f.loadServices();
  f.selectService(f.services.single);
  await f.loadDoctors();
  f.selectDoctor(f.doctors.single);
  f.changeDate('2030-01-02');
  await f.loadSlots();
  f.selectSlot(f.availability!.slots.single);
  f.review();
}

void main() {
  test('LIVE settings require a real configured clinic and a server root', () {
    expect(const ApiSettings().isLive, isFalse);
    for (final s in [
      const ApiSettings(baseUrl: 'bad', clinicId: 7),
      const ApiSettings(baseUrl: 'https://development.invalid'),
      const ApiSettings(
        baseUrl: 'https://development.invalid/api',
        clinicId: 7,
      ),
      const ApiSettings(
        baseUrl: 'https://name:secret@development.invalid',
        clinicId: 7,
      ),
    ]) {
      expect(s.error, isNotNull);
    }
    expect(() => DentalApi(const ApiSettings()), throwsArgumentError);
    expect(settings.error, isNull);
  });
  testWidgets(
    'DEMO has no LIVE client or requests; invalid LIVE has no demo fallback',
    (tester) async {
      var requests = 0;
      final api = makeApi((r) {
        requests++;
        return standard(r);
      });
      addTearDown(api.close);
      await tester.pumpWidget(const NexoraDentalApp(settings: ApiSettings()));
      expect(find.byType(PatientFlow), findsOneWidget);
      expect(find.byType(LivePatientFlow), findsNothing);
      expect(requests, 0);
      await tester.pumpWidget(
        const NexoraDentalApp(
          settings: ApiSettings(baseUrl: 'https://development.invalid'),
        ),
      );
      expect(find.byType(LiveConfigurationError), findsOneWidget);
      expect(find.byType(PatientFlow), findsNothing);
      expect(requests, 0);
    },
  );
  test(
    'Login sends exact clinic contract and bearer auth; logout removes it',
    () async {
      final requests = <http.Request>[];
      final api = makeApi((r) {
        requests.add(r);
        return standard(r);
      });
      addTearDown(api.close);
      final session = await api.login(
        'synthetic@example.invalid',
        'synthetic-password',
      );
      expect(session.clinicId, 7);
      expect(api.authenticated, isTrue);
      expect(jsonDecode(requests.single.body), {
        'identifier': 'synthetic@example.invalid',
        'password': 'synthetic-password',
        'clinicId': 7,
      });
      expect(requests.single.headers.containsKey('authorization'), isFalse);
      await api.me();
      expect(
        requests.last.headers['authorization'],
        'Bearer synthetic-test-token',
      );
      api.clearSession();
      expect(api.authenticated, isFalse);
      await expectLater(
        api.services(),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 401)),
      );
    },
  );
  test('Login failure never creates an authenticated session', () async {
    final api = makeApi(
      (r) => response({'error': 'Нэвтрэх мэдээлэл буруу'}, 401),
    );
    addTearDown(api.close);
    await expectLater(api.login('test', 'wrong'), throwsA(isA<ApiFailure>()));
    expect(api.authenticated, isFalse);
  });
  test('Wrong clinic or role is rejected without retaining JWT', () async {
    for (final changes in [
      {'clinicId': 8},
      {'role': 'Director'},
    ]) {
      final api = makeApi((r) => response({...authJson, ...changes}));
      await expectLater(api.login('test', 'test'), throwsA(isA<ApiFailure>()));
      expect(api.authenticated, isFalse);
      api.close();
    }
  });
  test('Revoked authentication clears all patient and booking state', () async {
    var revoked = false;
    final f = LiveFlowController(
      makeApi((r) => revoked ? response({}, 401) : standard(r)),
    );
    addTearDown(f.dispose);
    await prepare(f);
    revoked = true;
    await f.loadAppointments(1);
    expect(f.session, isNull);
    expect(f.patient, isNull);
    expect(f.slot, isNull);
    expect(f.step, 0);
    expect(f.api.authenticated, isFalse);
  });
  test(
    'Clinic switching clears authentication, profile, booking and history',
    () async {
      final f = LiveFlowController(makeApi(standard));
      addTearDown(f.dispose);
      await prepare(f);
      await f.loadAppointments(1);
      f.changeClinic(
        DentalApi(
          const ApiSettings(
            baseUrl: 'https://development.invalid',
            clinicId: 8,
          ),
          client: MockClient((r) async => standard(r)),
        ),
      );
      expect(f.session, isNull);
      expect(f.patient, isNull);
      expect(f.service, isNull);
      expect(f.doctor, isNull);
      expect(f.slot, isNull);
      expect(f.history, isNull);
      expect(f.api.authenticated, isFalse);
    },
  );
  test(
    'Registration needs real verification response and subsequent login',
    () async {
      final requests = <http.Request>[];
      final f = LiveFlowController(
        makeApi((r) {
          requests.add(r);
          return response({'message': 'Баталгаажуулна уу.', 'userId': 9});
        }),
      );
      addTearDown(f.dispose);
      await f.register(
        'Тест',
        'synthetic@example.invalid',
        'synthetic-password',
        null,
      );
      expect(f.step, 2);
      expect(f.registration!.userId, 9);
      expect(f.session, isNull);
      expect(jsonDecode(requests.first.body)['clinicId'], 7);
      await f.verify('123456');
      expect(f.step, 1);
      expect(f.registration, isNull);
      expect(f.session, isNull);
      expect(jsonDecode(requests.last.body), {'userId': 9, 'code': '123456'});
    },
  );
  test(
    'SMTP and verification failures do not advance or bypass verification',
    () async {
      var status = 503;
      final f = LiveFlowController(
        makeApi(
          (r) => response({'error': 'Имэйл илгээхэд алдаа гарлаа.'}, status),
        ),
      );
      addTearDown(f.dispose);
      f.enterClinic();
      await f.register(
        'Тест',
        'synthetic@example.invalid',
        'synthetic-password',
        null,
      );
      expect(f.step, 1);
      expect(f.error, contains('Имэйл'));
      expect(f.registration, isNull);
      f.registration = VerificationReceipt.fromJson({
        'message': 'Тест',
        'userId': 9,
      });
      f.step = 2;
      status = 400;
      await f.verify('000000');
      expect(f.step, 2);
      expect(f.registration, isNotNull);
      expect(f.session, isNull);
    },
  );
  test(
    'Profile missing opens creation; creation sends no tenant or patient IDs',
    () async {
      http.Request? creation;
      final f = LiveFlowController(
        makeApi((r) {
          if (r.url.path == '/api/Patients/me') {
            if (r.method == 'GET') return response({}, 404);
            creation = r;
            return response({
              'id': 11,
              'fullName': 'Тест',
              'phone': '00000000',
            }, 201);
          }
          return standard(r);
        }),
      );
      addTearDown(f.dispose);
      await f.login('test', 'test');
      await f.loadProfile();
      expect(f.profileMissing, isTrue);
      expect(f.patient, isNull);
      await f.createProfile('Тест', '00000000');
      expect(f.patient!.id, 11);
      expect(f.profileMissing, isFalse);
      expect(jsonDecode(creation!.body), {
        'fullName': 'Тест',
        'phone': '00000000',
        'dateOfBirth': null,
        'address': null,
      });
    },
  );
  test('Real profile and catalogs parse; doctor filtering and slot query use exact IDs', () async {
    final requests = <http.Request>[];
    final f = LiveFlowController(
      makeApi((r) {
        requests.add(r);
        return standard(r);
      }),
    );
    addTearDown(f.dispose);
    await prepare(f);
    expect(f.patient!.fullName, 'Тест');
    expect(f.services.single.minPrice, 0);
    expect(
      requests
          .firstWhere((r) => r.url.path.endsWith('/doctors'))
          .url
          .queryParameters,
      {'serviceId': '3'},
    );
    expect(
      requests
          .firstWhere((r) => r.url.path.endsWith('/slots'))
          .url
          .queryParameters,
      {'doctorProfileId': '5', 'serviceId': '3', 'date': '2030-01-02'},
    );
    expect(f.slot!.startUtc.isUtc, isTrue);
    expect(
      ClinicClock.label(f.slot!.startUtc, f.availability!.timeZoneId),
      contains('09:00'),
    );
  });
  test(
    'Empty slots remain empty; unknown timezone never guesses an offset',
    () async {
      var zone = 'Asia/Ulaanbaatar';
      final f = LiveFlowController(
        makeApi(
          (r) => r.url.path.endsWith('/slots')
              ? response({
                  'date': '2030-01-02',
                  'timeZoneId': zone,
                  'slots': [],
                })
              : standard(r),
        ),
      );
      addTearDown(f.dispose);
      await f.login('test', 'test');
      await f.loadServices();
      f.selectService(f.services.single);
      await f.loadDoctors();
      f.selectDoctor(f.doctors.single);
      await f.loadSlots();
      expect(f.availability!.slots, isEmpty);
      expect(f.slot, isNull);
      zone = 'Unknown/Zone';
      await f.loadSlots();
      expect(f.availability, isNull);
      expect(f.error, isNotNull);
    },
  );
  test(
    'Changing service, doctor and date clears dependent choices and intent',
    () async {
      final f = LiveFlowController(makeApi(standard));
      addTearDown(f.dispose);
      await prepare(f);
      f.changeDate('2030-01-03');
      expect(f.slot, isNull);
      expect(f.requestId, isNull);
      expect(f.availability, isNull);
      await f.loadSlots();
      f.selectSlot(f.availability!.slots.single);
      final otherDoctor = BookingDoctor.fromJson({...doctorJson, 'id': 6});
      f.doctors.add(otherDoctor);
      f.selectDoctor(otherDoctor);
      expect(f.slot, isNull);
      expect(f.availability, isNull);
      expect(f.requestId, isNull);
      final otherService = BookingService.fromJson({...serviceJson, 'id': 4});
      f.services.add(otherService);
      f.selectService(otherService);
      expect(f.doctor, isNull);
      expect(f.doctors, isEmpty);
      expect(f.slot, isNull);
    },
  );
  for (final status in [201, 200]) {
    test(
      'Booking HTTP $status displays only server returned details and exact request',
      () async {
        http.Request? booking;
        final f = LiveFlowController(
          makeApi((r) {
            if (r.url.path == '/api/appointments') {
              booking = r;
              return response(appointmentJson, status);
            }
            return standard(r);
          }),
        );
        addTearDown(f.dispose);
        await prepare(f);
        final id = f.requestId!;
        expect(
          id,
          matches(
            RegExp(
              r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
            ),
          ),
        );
        await f.book();
        expect(f.appointment!.id, 41);
        expect(f.appointment!.status, 'Confirmed');
        expect(jsonDecode(booking!.body), {
          'doctorProfileId': 5,
          'serviceId': 3,
          'startUtc': '2030-01-02T01:00:00.000Z',
          'requestId': id,
        });
      },
    );
  }
  test(
    '409 clears invalid slot; explicit refresh returns to server availability',
    () async {
      var slotRequests = 0;
      final f = LiveFlowController(
        makeApi((r) {
          if (r.url.path == '/api/appointments') return response({}, 409);
          if (r.url.path.endsWith('/slots')) slotRequests++;
          return standard(r);
        }),
      );
      addTearDown(f.dispose);
      await prepare(f);
      await f.book();
      expect(f.appointment, isNull);
      expect(f.conflict, isTrue);
      expect(f.slot, isNull);
      expect(f.requestId, isNull);
      expect(f.service, isNotNull);
      expect(f.doctor, isNotNull);
      await f.loadSlots();
      expect(slotRequests, 2);
      expect(f.step, 6);
      expect(f.conflict, isFalse);
      expect(f.availability, isNotNull);
    },
  );
  test(
    'Timeout preserves intent, blocks selection changes, retry reuses UUID',
    () async {
      final ids = <String>[];
      var attempts = 0;
      final api = makeApi((r) async {
        if (r.url.path == '/api/appointments') {
          ids.add(jsonDecode(r.body)['requestId'] as String);
          attempts++;
          if (attempts == 1) throw TimeoutException('synthetic');
          return response(appointmentJson, 200);
        }
        return standard(r);
      });
      final f = LiveFlowController(api);
      addTearDown(f.dispose);
      await prepare(f);
      final date = f.date;
      await f.book();
      expect(f.appointment, isNull);
      expect(f.uncertainBooking, isTrue);
      expect(f.error, contains('хугацаа'));
      f.changeDate('2030-01-03');
      expect(f.date, date);
      await f.book();
      expect(ids.length, 2);
      expect(ids.first, ids.last);
      expect(f.appointment!.id, 41);
    },
  );
  test(
    'Actual timeout is bounded and late response cannot create UI success',
    () async {
      final pending = Completer<http.Response>();
      final api = makeApi(
        (r) => r.url.path == '/api/appointments' ? pending.future : standard(r),
        timeout: const Duration(milliseconds: 20),
      );
      final f = LiveFlowController(api);
      addTearDown(f.dispose);
      await prepare(f);
      final id = f.requestId;
      await f.book();
      expect(f.uncertainBooking, isTrue);
      expect(f.appointment, isNull);
      expect(f.requestId, id);
      pending.complete(response(appointmentJson, 201));
      await Future<void>.delayed(Duration.zero);
      expect(f.appointment, isNull);
    },
  );
  test(
    'Rapid double taps send one booking; repeated success does not send again',
    () async {
      final pending = Completer<http.Response>();
      var count = 0;
      final f = LiveFlowController(
        makeApi((r) {
          if (r.url.path == '/api/appointments') {
            count++;
            return pending.future;
          }
          return standard(r);
        }),
      );
      addTearDown(f.dispose);
      await prepare(f);
      final first = f.book();
      await f.book();
      await Future<void>.delayed(Duration.zero);
      expect(count, 1);
      expect(f.busy, isTrue);
      pending.complete(response(appointmentJson, 201));
      await first;
      await f.book();
      expect(count, 1);
    },
  );
  test(
    'Logout while a request is pending cannot restore old clinic data',
    () async {
      final pending = Completer<http.Response>();
      final f = LiveFlowController(
        makeApi(
          (r) =>
              r.url.path.endsWith('/services') ? pending.future : standard(r),
        ),
      );
      addTearDown(f.dispose);
      await f.login('test', 'test');
      final loading = f.loadServices();
      f.logout();
      pending.complete(response([serviceJson]));
      await loading;
      expect(f.services, isEmpty);
      expect(f.session, isNull);
      expect(f.step, 0);
    },
  );
  test('Own appointments uses backend page/pageSize and parses all appointment fields', () async {
    final f = LiveFlowController(makeApi(standard));
    addTearDown(f.dispose);
    await f.login('test', 'test');
    await f.loadAppointments(2);
    expect(f.history!.page, 2);
    expect(f.history!.pageSize, 20);
    expect(f.history!.items.single.id, 41);
    expect(f.history!.items.single.occupiedEndUtc.isUtc, isTrue);
  });
  for (final status in [400, 401, 403, 404, 409, 429, 503, 500]) {
    test(
      'HTTP $status has safe Mongolian message and never exposes server secrets',
      () async {
        final api = makeApi(
          (r) => r.url.path == '/api/Auth/login'
              ? standard(r)
              : http.Response(
                  'StackTrace token=secret password=secret code=123456',
                  status,
                ),
        );
        addTearDown(api.close);
        await api.login('test', 'test');
        await expectLater(
          api.services(),
          throwsA(
            isA<ApiFailure>()
                .having((e) => e.status, 'status', status)
                .having(
                  (e) => e.message.contains('secret'),
                  'no secrets',
                  isFalse,
                ),
          ),
        );
      },
    );
  }
  test(
    'Malformed JSON and network failures are safe, with no synthetic fallback',
    () async {
      for (final result in [
        http.Response('not-json secret', 200),
        http.Response('{}', 200),
      ]) {
        final api = makeApi(
          (r) => r.url.path == '/api/Auth/login' ? standard(r) : result,
        );
        await api.login('test', 'test');
        await expectLater(
          api.services(),
          throwsA(
            isA<ApiFailure>().having(
              (e) => e.message,
              'message',
              contains('хариу буруу'),
            ),
          ),
        );
        api.close();
      }
      final api = makeApi((r) => throw http.ClientException('secret'));
      addTearDown(api.close);
      await expectLater(
        api.login('test', 'test'),
        throwsA(
          isA<ApiFailure>().having(
            (e) => e.message,
            'safe',
            isNot(contains('secret')),
          ),
        ),
      );
    },
  );
  test(
    'Credentials, JWT and verification codes produce no print output',
    () async {
      final logs = <String>[];
      await runZoned(
        () async {
          final api = makeApi(standard);
          await api.login('synthetic@example.invalid', 'synthetic-password');
          await api.me();
          api.close();
          final verification = makeApi(
            (r) => response({'message': 'Баталгаажлаа.', 'userId': 9}),
          );
          await verification.register(
            'Тест',
            'synthetic@example.invalid',
            'synthetic-password',
            null,
          );
          await verification.verify(9, '123456');
          verification.close();
        },
        zoneSpecification: ZoneSpecification(
          print: (_, parent, zone, line) => logs.add(line),
        ),
      );
      expect(logs, isEmpty);
    },
  );
  testWidgets(
    'LIVE login renders on a narrow screen and never reports fake booking success',
    (tester) async {
      tester.view.physicalSize = const Size(360, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = makeApi(standard);
      await tester.pumpWidget(
        MaterialApp(
          home: LivePatientFlow(settings: settings, api: api),
        ),
      );
      await tester.tap(find.text('Энэ эмнэлгээр үргэлжлүүлэх'));
      await tester.pumpAndSettle();
      expect(find.byType(TextFormField), findsNWidgets(2));
      await tester.enterText(
        find.byType(TextFormField).at(0),
        'synthetic@example.invalid',
      );
      await tester.enterText(
        find.byType(TextFormField).at(1),
        'synthetic-password',
      );
      final login = find.widgetWithText(FilledButton, 'Нэвтрэх');
      await tester.ensureVisible(login);
      await tester.tap(login);
      await tester.pumpAndSettle();
      expect(find.text('Миний профайлыг авах'), findsOneWidget);
      expect(find.text('Захиалга амжилттай үүслээ.'), findsNothing);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
    },
  );
  test(
    'A second login clears previous patient selections before any response',
    () async {
      final f = LiveFlowController(makeApi(standard));
      addTearDown(f.dispose);
      await prepare(f);
      final login = f.login(
        'another-synthetic@example.invalid',
        'synthetic-password',
      );
      expect(f.patient, isNull);
      expect(f.service, isNull);
      expect(f.doctor, isNull);
      expect(f.slot, isNull);
      expect(f.requestId, isNull);
      await login;
    },
  );
  test(
    'Invalid successful booking response preserves the same uncertain intent',
    () async {
      final f = LiveFlowController(
        makeApi(
          (r) => r.url.path == '/api/appointments'
              ? response({}, 201)
              : standard(r),
        ),
      );
      addTearDown(f.dispose);
      await prepare(f);
      final id = f.requestId;
      await f.book();
      expect(f.appointment, isNull);
      expect(f.requestId, id);
      expect(f.uncertainBooking, isTrue);
    },
  );
  test(
    'Slot UTC instants use the returned Hovd timezone, not the device clock',
    () {
      expect(
        ClinicClock.label(DateTime.utc(2030, 1, 2, 1), 'Asia/Hovd'),
        contains('08:00'),
      );
    },
  );
  testWidgets(
    'LIVE eight-step login booking uses mocked API responses and paginated history',
    (tester) async {
      var bookings = 0;
      final api = makeApi((r) {
        if (r.url.path == '/api/appointments') bookings++;
        return standard(r);
      });
      await tester.pumpWidget(
        MaterialApp(
          home: LivePatientFlow(settings: settings, api: api),
        ),
      );
      Future<void> press(String text) async {
        final target = find.widgetWithText(FilledButton, text);
        await tester.ensureVisible(target);
        await tester.pumpAndSettle();
        await tester.ensureVisible(target);
        await tester.pumpAndSettle();
        await tester.tap(target);
        await tester.pumpAndSettle();
      }

      await press('Энэ эмнэлгээр үргэлжлүүлэх');
      await tester.enterText(
        find.byType(TextFormField).at(0),
        'synthetic@example.invalid',
      );
      await tester.enterText(
        find.byType(TextFormField).at(1),
        'synthetic-password',
      );
      await press('Нэвтрэх');
      await press('Миний профайлыг авах');
      await press('Үйлчилгээ авах');
      await tester.tap(find.text('Тест үйлчилгээ'));
      await tester.pumpAndSettle();
      await press('Эмч сонгох');
      await tester.tap(find.text('Тест эмч'));
      await tester.pumpAndSettle();
      await press('Огноо, цаг сонгох');
      await press('Боломжит цагийг шинэчлэх');
      final slot = find.text('09:00');
      await tester.ensureVisible(slot);
      await tester.tap(slot);
      await tester.pumpAndSettle();
      expect(find.text('Улаанбаатарын цагаар'), findsOneWidget);
      expect(find.text('30 минут'), findsOneWidget);
      await press('Захиалгыг хянах');
      expect(find.text('Захиалга амжилттай үүслээ.'), findsNothing);
      await press('Захиалга баталгаажуулах');
      expect(bookings, 1);
      expect(find.text('Захиалга амжилттай үүслээ.'), findsOneWidget);
      expect(find.text('Баталгаажсан захиалга'), findsOneWidget);
      expect(find.text('Захиалгын дугаар: №41'), findsOneWidget);
      await tester.tap(find.byTooltip('Миний захиалгууд'));
      await tester.pumpAndSettle();
      expect(find.text('Хуудас 1'), findsOneWidget);
      expect(find.text('Баталгаажсан захиалга'), findsOneWidget);
      expect(find.text('Захиалгын дугаар: №41'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
    },
  );
  testWidgets(
    'LIVE registration validates fields, waits for email code response and returns to login',
    (tester) async {
      var registrations = 0, verifications = 0;
      final api = makeApi((r) {
        if (r.url.path.endsWith('/register')) {
          registrations++;
          return response({'message': 'Код илгээлээ.', 'userId': 9});
        }
        if (r.url.path.endsWith('/verify')) {
          verifications++;
          if (jsonDecode(r.body)['code'] == '000000') {
            return response({'error': 'Код буруу'}, 400);
          }
          return response({'message': 'Бүртгэл баталгаажлаа.', 'userId': 9});
        }
        return standard(r);
      });
      await tester.pumpWidget(
        MaterialApp(
          home: LivePatientFlow(settings: settings, api: api),
        ),
      );
      Future<void> press(String text) async {
        final target = find.widgetWithText(FilledButton, text);
        await tester.ensureVisible(target);
        await tester.pumpAndSettle();
        await tester.ensureVisible(target);
        await tester.pumpAndSettle();
        await tester.tap(target);
        await tester.pumpAndSettle();
      }

      await press('Энэ эмнэлгээр үргэлжлүүлэх');
      await tester.tap(find.text('Бүртгүүлэх'));
      await tester.pumpAndSettle();
      await press('Бүртгэл үүсгэх');
      expect(registrations, 0);
      expect(find.text('Энэ талбарыг бөглөнө үү.'), findsWidgets);
      final fields = find.byType(TextFormField);
      await tester.enterText(fields.at(0), 'Тест');
      await tester.enterText(fields.at(1), 'synthetic@example.invalid');
      await tester.enterText(fields.at(3), 'synthetic-password');
      await press('Бүртгэл үүсгэх');
      expect(registrations, 1);
      expect(verifications, 0);
      expect(api.authenticated, isFalse);
      await tester.enterText(find.byType(TextFormField), '000000');
      await press('Код шалгах');
      expect(verifications, 1);
      expect(find.text('Код буруу'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Код шалгах'), findsOneWidget);
      expect(api.authenticated, isFalse);
      await tester.enterText(find.byType(TextFormField), '123456');
      await press('Код шалгах');
      expect(verifications, 2);
      expect(find.widgetWithText(FilledButton, 'Нэвтрэх'), findsOneWidget);
      expect(find.byType(TextFormField), findsNWidgets(2));
      expect(api.authenticated, isFalse);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
    },
  );
  testWidgets('Reconfiguring the LIVE widget clears the old clinic session', (
    tester,
  ) async {
    final first = makeApi(standard);
    await first.login('synthetic@example.invalid', 'synthetic-password');
    await tester.pumpWidget(
      MaterialApp(
        home: LivePatientFlow(settings: settings, api: first),
      ),
    );
    const otherSettings = ApiSettings(
      baseUrl: 'https://development.invalid',
      clinicId: 8,
    );
    var requests = 0;
    final second = DentalApi(
      otherSettings,
      client: MockClient((r) async {
        requests++;
        return standard(r);
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: LivePatientFlow(settings: otherSettings, api: second),
      ),
    );
    expect(first.authenticated, isFalse);
    expect(second.authenticated, isFalse);
    expect(requests, 0);
    expect(find.text('Сонгосон туршилтын эмнэлэг'), findsOneWidget);
    expect(find.text('АЛХАМ 1 / 8'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
  });
  test('A failed conflict refresh keeps the slot screen available for another retry', () async {
    var failRefresh = false;
    final f = LiveFlowController(
      makeApi((r) {
        if (r.url.path == '/api/appointments') return response({}, 409);
        if (r.url.path.endsWith('/slots') && failRefresh) {
          throw http.ClientException('synthetic');
        }
        return standard(r);
      }),
    );
    addTearDown(f.dispose);
    await prepare(f);
    await f.book();
    failRefresh = true;
    await f.loadSlots();
    expect(f.step, 6);
    expect(f.error, isNotNull);
    expect(f.slot, isNull);
    expect(f.availability, isNull);
    failRefresh = false;
    await f.loadSlots();
    expect(f.availability, isNotNull);
    expect(f.error, isNull);
  });
}
