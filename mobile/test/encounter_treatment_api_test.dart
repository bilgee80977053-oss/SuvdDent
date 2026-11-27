import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexora_dental_mobile/live/dental_api.dart';
import 'package:nexora_dental_mobile/live/encounter_treatment_api.dart';
import 'package:nexora_dental_mobile/live/models.dart';

const _settings = ApiSettings(baseUrl: 'https://clinical.invalid', clinicId: 7);
const _root = '/api/clinical/appointments/41/treatment';
const _key = '6a8d6b33-d750-48c7-ae26-395323d28061';
const _otherKey = '5b8c1b6e-259f-4d70-8fce-b3a762276841';
const _signed = '2026-11-27T20:00:00+08:00';
const _synthetic = 'Synthetic test fixture only';
const _login = {
  'token': 'synthetic-doctor-token',
  'userId': 15,
  'fullName': 'Synthetic Doctor',
  'role': 'Doctor',
  'clinicId': 7,
};
const _identity = {'role': 'Doctor', 'userId': '15', 'clinicId': '7'};

http.Response _json(Object value, [int status = 200]) =>
    http.Response(jsonEncode(value), status);

http.Response _default(http.Request request) => switch (request.url.path) {
  '/api/Auth/login' => _json(_login),
  '/api/Auth/me' => _json(_identity),
  _ => throw StateError('Unexpected transport call to ${request.url.path}'),
};

EncounterTreatmentApi _client(
  FutureOr<http.Response> Function(http.Request) handler,
) {
  final api = EncounterTreatmentApi(
    _settings,
    client: MockClient((request) async => handler(request)),
  );
  addTearDown(api.close);
  return api;
}

Future<void> _authenticate(EncounterTreatmentApi api) =>
    api.login('synthetic@example.invalid', 'synthetic-password');

Matcher _failure(int? status) =>
    isA<ApiFailure>().having((error) => error.status, 'status', status);

void main() {
  test('only authenticated Doctor calls request a single encounter', () async {
    final paths = <String>[];
    final api = _client((r) {
      paths.add(r.url.path);
      expect(r.followRedirects, isFalse);
      expect(r.headers['Cache-Control'], 'no-store');
      if (r.url.path == '/api/Auth/login') {
        expect(r.method, 'POST');
        expect(r.headers['Authorization'], isNull);
        expect(jsonDecode(r.body), {
          'clinicId': 7,
          'identifier': 'synthetic@example.invalid',
          'password': 'synthetic-password',
        });
        return _default(r);
      }
      expect(r.method, 'GET');
      expect(r.headers['Authorization'], 'Bearer synthetic-doctor-token');
      expect(r.body, isEmpty);
      return switch (r.url.path) {
        _root => _json({
          'appointmentId': 41,
          'note': _synthetic,
          'signedUtc': _signed,
        }),
        '$_root/addenda' => _json({
          'page': 2,
          'pageSize': 1,
          'hasMore': true,
          'items': [
            {'requestId': _key, 'signedUtc': _signed},
          ],
        }),
        '$_root/addenda/$_key' => _json({
          'requestId': _key,
          'note': _synthetic,
          'signedUtc': _signed,
        }),
        _ => _default(r),
      };
    });

    await expectLater(api.readTreatment(41), throwsA(_failure(401)));
    expect(paths, isEmpty);
    await _authenticate(api);

    final note = await api.readTreatment(41);
    expect(note.appointmentId, 41);
    expect(note.note, _synthetic);
    expect(note.signedUtc, DateTime.utc(2026, 11, 27, 12));

    final page = await api.listAddenda(41, page: 2, pageSize: 1);
    expect(page.page, 2);
    expect(page.pageSize, 1);
    expect(page.hasMore, isTrue);
    expect(page.items.single.requestId, _key);
    expect(page.items.single.signedUtc, note.signedUtc);
    expect(() => page.items.clear(), throwsUnsupportedError);

    final detail = await api.readAddendum(41, _key.toUpperCase());
    expect(detail.requestId, _key);
    expect(detail.note, _synthetic);
    expect(detail.signedUtc, note.signedUtc);
    expect(paths, [
      '/api/Auth/login',
      '/api/Auth/me',
      _root,
      '$_root/addenda',
      '$_root/addenda/$_key',
    ]);
  });

  for (final override in [
    {'role': 'Patient'},
    {'role': 'Director'},
    {'clinicId': 8},
  ]) {
    test('rejects mismatched login claims: $override', () async {
      final api = _client((r) {
        expect(r.url.path, '/api/Auth/login');
        return _json({..._login, ...override});
      });
      await expectLater(_authenticate(api), throwsA(_failure(403)));
      expect(api.authenticated, isFalse);
      await expectLater(api.readTreatment(41), throwsA(_failure(401)));
    });
  }

  for (final override in [
    {'role': 'Director'},
    {'userId': '16'},
    {'clinicId': '8'},
  ]) {
    test('rejects mismatched server identity: $override', () async {
      final api = _client((r) {
        if (r.url.path == '/api/Auth/me') {
          return _json({..._identity, ...override});
        }
        return _default(r);
      });
      await expectLater(_authenticate(api), throwsA(_failure(403)));
      expect(api.authenticated, isFalse);
      await expectLater(api.listAddenda(41), throwsA(_failure(401)));
    });
  }

  test('validates scope and query bounds before transport', () async {
    final api = _client(_default);
    await _authenticate(api);
    await expectLater(api.readTreatment(0), throwsA(_failure(400)));
    await expectLater(api.readTreatment(2147483648), throwsA(_failure(400)));
    await expectLater(api.listAddenda(41, page: 1001), throwsA(_failure(400)));
    await expectLater(
      api.listAddenda(41, pageSize: 51),
      throwsA(_failure(400)),
    );
    await expectLater(
      api.readAddendum(41, '../out-of-scope'),
      throwsA(_failure(400)),
    );
  });

  test(
    'exact pagination query; rejects a payload for a different page',
    () async {
      final api = _client((r) {
        if (r.url.path == '$_root/addenda') {
          expect(r.url.queryParameters, {'page': '2', 'pageSize': '3'});
          return _json({
            'page': 1,
            'pageSize': 3,
            'hasMore': false,
            'items': [],
          });
        }
        return _default(r);
      });
      await _authenticate(api);
      await expectLater(
        api.listAddenda(41, page: 2, pageSize: 3),
        throwsA(_failure(null)),
      );
    },
  );

  test('rejects mismatched note and addendum identities', () async {
    final api = _client(
      (r) => switch (r.url.path) {
        _root => _json({
          'appointmentId': 42,
          'note': _synthetic,
          'signedUtc': _signed,
        }),
        '$_root/addenda/$_key' => _json({
          'requestId': _otherKey,
          'note': _synthetic,
          'signedUtc': _signed,
        }),
        _ => _default(r),
      },
    );
    await _authenticate(api);
    await expectLater(api.readTreatment(41), throwsA(_failure(null)));
    await expectLater(api.readAddendum(41, _key), throwsA(_failure(null)));
  });

  for (final status in [302, 401, 403, 404, 503]) {
    test(
      'HTTP $status never exposes server-provided clinical content',
      () async {
        final api = _client((r) {
          if (r.url.path == _root) {
            expect(r.followRedirects, isFalse);
            return _json({'message': _synthetic}, status);
          }
          return _default(r);
        });
        await _authenticate(api);
        await expectLater(
          api.readTreatment(41),
          throwsA(
            isA<ApiFailure>()
                .having((e) => e.status, 'status', status)
                .having(
                  (e) => e.message,
                  'message',
                  isNot(contains(_synthetic)),
                ),
          ),
        );
        expect(api.authenticated, status != 401);
      },
    );
  }

  test('logout discards an in-flight clinical response', () async {
    final sent = Completer<void>();
    final response = Completer<http.Response>();
    final api = _client((r) {
      if (r.url.path == _root) {
        sent.complete();
        return response.future;
      }
      return _default(r);
    });
    await _authenticate(api);
    final read = api.readTreatment(41);
    final checked = expectLater(read, throwsA(_failure(401)));
    await sent.future;
    api.logout();
    response.complete(
      _json({'appointmentId': 41, 'note': _synthetic, 'signedUtc': _signed}),
    );
    await checked;
    expect(api.authenticated, isFalse);
  });
}
