import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexora_dental_mobile/live/dental_api.dart';
import 'package:nexora_dental_mobile/live/director_catalog_api.dart';
import 'package:nexora_dental_mobile/live/director_catalog_screen.dart';
import 'package:nexora_dental_mobile/live/models.dart';

const settings = ApiSettings(
  baseUrl: 'https://development.invalid',
  clinicId: 7,
);

http.Response reply(Object content, [int status = 200]) => http.Response(
  jsonEncode(content),
  status,
  headers: {'content-type': 'application/json'},
);

const service = {
  'id': 3,
  'clinicId': 7,
  'name': 'Баталгаажсан үйлчилгээ',
  'description': 'Клиникийн тайлбар',
  'minPrice': 0,
  'maxPrice': null,
  'aftercareText': null,
  'isActive': true,
};

http.Response normal(http.Request request) {
  if (request.url.path == '/api/Auth/login') {
    return reply({
      'token': 'director-only-token',
      'userId': 9,
      'fullName': 'Захирал',
      'role': 'Director',
      'clinicId': 7,
    });
  }
  if (request.url.path == '/api/Auth/me') {
    return reply({
      'userId': '9',
      'clinicId': '7',
      'role': 'Director',
      'name': 'Захирал',
    });
  }
  if (request.url.path == '/api/admin/services') return reply([service]);
  return reply({'message': 'Not found'}, 404);
}

DirectorCatalogApi makeApi(
  http.Response Function(http.Request) handler,
) => DirectorCatalogApi(settings, client: MockClient((req) async => handler(req)));

void main() {
  test('director session uses clinic-scoped bearer token, list and logout', () async {
    final calls = <http.Request>[];
    final api = makeApi((r) {
      calls.add(r);
      return normal(r);
    });
    addTearDown(api.close);
    await api.login('99999999', 'local-test-password');
    expect(api.authenticated, isTrue);
    expect(jsonDecode(calls.first.body)['clinicId'], 7);
    expect(calls.first.headers.containsKey('authorization'), isFalse);
    expect(calls[1].headers['authorization'], 'Bearer director-only-token');
    final services = await api.services();
    expect(services.single.name, 'Баталгаажсан үйлчилгээ');
    expect(calls.last.headers['authorization'], 'Bearer director-only-token');
    api.logout();
    expect(api.authenticated, isFalse);
    await expectLater(api.services(), throwsA(isA<ApiFailure>()));
  });

  test('rejects patient roles and wrong clinics, stores no token', () async {
    for (final changed in [
      {'role': 'Patient'},
      {'clinicId': 9},
    ]) {
      final api = makeApi((r) {
        if (r.url.path == '/api/Auth/login') {
          return reply({
            'token': 'invalid',
            'userId': 9,
            'fullName': 'Test',
            'role': 'Director',
            'clinicId': 7,
            ...changed,
          });
        }
        return normal(r);
      });
      await expectLater(
        api.login('test', 'test'),
        throwsA(isA<ApiFailure>()),
      );
      expect(api.authenticated, isFalse);
      api.close();
    }
  });

  test('creates without fake prices and validates server returned clinic', () async {
    final requests = <http.Request>[];
    final api = makeApi((r) {
      requests.add(r);
      if (r.url.path == '/api/admin/services' && r.method == 'POST') {
        return reply({
          ...service,
          'name': 'Хүлээн зөвшөөрсөн үйлчилгээ',
          'id': 10,
        }, 201);
      }
      return normal(r);
    });
    addTearDown(api.close);
    await api.login('test', 'test');
    final created = await api.saveService(
      name: 'Хүлээн зөвшөөрсөн үйлчилгээ',
      description: 'Тайлбар',
      minPrice: 0,
    );
    expect(created.id, 10);
    final body = jsonDecode(requests.last.body);
    expect(body['name'], 'Хүлээн зөвшөөрсөн үйлчилгээ');
    expect(body['minPrice'], 0);
    expect(body.containsKey('clinicId'), isFalse);
    expect(requests.last.headers['authorization'], 'Bearer director-only-token');
  });

  testWidgets('Director screen displays only separate authorized catalog', (tester) async {
    final api = makeApi(normal);
    await tester.pumpWidget(MaterialApp(
      home: DirectorCatalogScreen(settings: settings, api: api),
    ));
    expect(find.text('Захирлаар нэвтрэх'), findsOneWidget);
    expect(find.text('Шинэ үйлчилгээ бүртгэх'), findsNothing);
    await tester.enterText(find.byType(TextFormField).at(0), '99999999');
    await tester.enterText(find.byType(TextFormField).at(1), 'test-password');
    await tester.tap(find.text('Захирлаар нэвтрэх'));
    await tester.pumpAndSettle();
    expect(find.text('Шинэ үйлчилгээ бүртгэх'), findsOneWidget);
    expect(find.text('Баталгаажсан үйлчилгээ'), findsOneWidget);
    await tester.tap(find.byTooltip('Гарах'));
    await tester.pumpAndSettle();
    expect(find.text('Захирлаар нэвтрэх'), findsOneWidget);
    expect(find.text('Баталгаажсан үйлчилгээ'), findsNothing);
    await tester.pumpWidget(const SizedBox());
  });

  test('director reads doctor roster and verified working hours without writes', () async {
    final requests = <http.Request>[];
    final api = makeApi((r) {
      requests.add(r);
      if (r.url.path == '/api/admin/Doctors') {
        return reply([
          {
            'id': 20,
            'fullName': 'Бүртгэлтэй эмч',
            'specialty': 'Шүдний эмчилгээ',
            'practiceStartYear': 2019,
            'version': '12345678-1234-1234-1234-123456789abc',
            'biography': null,
            'photoPath': null,
            'isPublished': true,
            'isActive': true,
            'workingHours': [
              {'dayOfWeek': 1, 'startMinute': 540, 'endMinute': 1080},
            ],
          },
        ]);
      }
      return normal(r);
    });
    addTearDown(api.close);
    await api.login('test', 'test');
    final doctors = await api.doctors();
    expect(doctors.single.fullName, 'Бүртгэлтэй эмч');
    expect(doctors.single.workingHours.single.startMinute, 540);
    expect(requests.last.method, 'GET');
    expect(requests.last.headers['authorization'], 'Bearer director-only-token');
  });

  test('editing service uses PUT and no caller-supplied clinic identity', () async {
    http.Request? write;
    final api = makeApi((r) {
      if (r.url.path == '/api/admin/services/3' && r.method == 'PUT') {
        write = r;
        return reply({...service, 'description': 'Шинэ баталгаажсан тайлбар'});
      }
      return normal(r);
    });
    addTearDown(api.close);
    await api.login('test', 'test');
    final updated = await api.saveService(
      id: 3,
      name: 'Баталгаажсан үйлчилгээ',
      description: 'Шинэ баталгаажсан тайлбар',
      minPrice: 0,
    );
    expect(updated.description, 'Шинэ баталгаажсан тайлбар');
    expect(write?.method, 'PUT');
    expect(jsonDecode(write!.body).containsKey('clinicId'), isFalse);
  });

  test('activating and deactivating service checks resulting state', () async {
    final paths = <String>[];
    final api = makeApi((r) {
      if (r.url.path.contains('/activate') ||
          r.url.path.contains('/deactivate')) {
        paths.add(r.url.path);
        final active = r.url.path.endsWith('/activate');
        return reply({...service, 'isActive': active});
      }
      return normal(r);
    });
    addTearDown(api.close);
    await api.login('test', 'test');
    final item = (await api.services()).single;
    final disabled = await api.setActive(item, false);
    expect(disabled.isActive, isFalse);
    final enabled = await api.setActive(disabled, true);
    expect(enabled.isActive, isTrue);
    expect(paths, [
      '/api/admin/services/3/deactivate',
      '/api/admin/services/3/activate',
    ]);
  });

  test('rejects a cross-clinic service write response', () async {
    final api = makeApi((r) {
      if (r.url.path == '/api/admin/services' && r.method == 'POST') {
        return reply({...service, 'clinicId': 99}, 201);
      }
      return normal(r);
    });
    addTearDown(api.close);
    await api.login('test', 'test');
    await expectLater(
      api.saveService(
        name: 'Баталгаажсан үйлчилгээ',
        description: 'Тайлбар',
        minPrice: 0,
      ),
      throwsA(isA<ApiFailure>()),
    );
  });

  testWidgets('doctor roster shows API results only on Director request', (tester) async {
    final api = makeApi((r) {
      if (r.url.path == '/api/admin/Doctors') {
        return reply([
          {
            'id': 20,
            'fullName': 'Бүртгэлтэй эмч',
            'specialty': 'Шүдний эмчилгээ',
            'practiceStartYear': 2019,
            'version': '12345678-1234-1234-1234-123456789abc',
            'biography': null,
            'photoPath': null,
            'isPublished': false,
            'isActive': true,
            'workingHours': [
              {'dayOfWeek': 1, 'startMinute': 540, 'endMinute': 1080},
            ],
          },
        ]);
      }
      return normal(r);
    });
    await tester.pumpWidget(MaterialApp(
      home: DirectorCatalogScreen(settings: settings, api: api),
    ));
    await tester.enterText(find.byType(TextFormField).at(0), '99999999');
    await tester.enterText(find.byType(TextFormField).at(1), 'test-password');
    await tester.tap(find.text('Захирлаар нэвтрэх'));
    await tester.pumpAndSettle();
    expect(find.text('Бүртгэлтэй эмч'), findsNothing);
    final load = find.text('Эмчийн жагсаалт харах');
    await tester.ensureVisible(load);
    await tester.pumpAndSettle();
    await tester.tap(load);
    await tester.pumpAndSettle();
    expect(find.text('Бүртгэлтэй эмч'), findsOneWidget);
    expect(find.text('Даваа 09:00–18:00'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });

  test('Doctor profile edits include original version and do not change tenant', () async {
    http.Request? update;
    final doctorJson = {
      'id': 20,
      'fullName': 'Бүртгэлтэй эмч',
      'specialty': 'Шүдний эмчилгээ',
      'practiceStartYear': 2019,
      'biography': null,
      'photoPath': null,
      'isPublished': false,
      'isActive': true,
      'version': '12345678-1234-1234-1234-123456789abc',
      'workingHours': <Map<String, int>>[],
    };
    final api = makeApi((request) {
      if (request.url.path == '/api/admin/Doctors/20' &&
          request.method == 'PUT') {
        update = request;
        return reply(doctorJson);
      }
      return normal(request);
    });
    addTearDown(api.close);
    await api.login('test', 'test');
    final doctor = DirectorDoctor.fromJson(doctorJson);
    await api.updateDoctorProfile(
      doctor,
      specialty: 'Шүдний эмчилгээ',
      practiceStartYear: 2019,
      isPublished: false,
      isActive: true,
    );
    expect(update?.method, 'PUT');
    final body = jsonDecode(update!.body);
    expect(body['version'], doctor.version);
    expect(body.containsKey('clinicId'), isFalse);
    expect(update!.headers['authorization'], 'Bearer director-only-token');
  });

  test('Doctor hours edit rejects invalid intervals before contacting backend', () async {
    var hourWrites = 0;
    final api = makeApi((request) {
      if (request.url.path.endsWith('/working-hours')) hourWrites++;
      return normal(request);
    });
    addTearDown(api.close);
    await api.login('test', 'test');
    final doctor = DirectorDoctor.fromJson({
      'id': 20,
      'fullName': 'Бүртгэлтэй эмч',
      'specialty': 'Шүдний эмчилгээ',
      'practiceStartYear': 2019,
      'biography': null,
      'photoPath': null,
      'isPublished': false,
      'isActive': true,
      'version': '12345678-1234-1234-1234-123456789abc',
      'workingHours': <Map<String, int>>[],
    });
    await expectLater(
      api.updateDoctorHours(doctor, [
        const DirectorWorkingHour(
          dayOfWeek: 1,
          startMinute: 700,
          endMinute: 600,
        ),
      ]),
      throwsA(isA<ApiFailure>()),
    );
    expect(hourWrites, 0);
  });
}
