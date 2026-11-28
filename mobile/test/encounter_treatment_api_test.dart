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

TypeMatcher<ApiFailure> _failure(int? status) =>
    isA<ApiFailure>().having((error) => error.status, 'status', status);

void main() {
  const historyPath = '/api/clinical/appointments/41/patient-history';
  final historyFrom = DateTime.utc(2026, 11, 1);
  final historyUntil = DateTime.utc(2026, 12, 1);
  Future<PatientHistoryPage> history(
    EncounterTreatmentApi api, {
    int anchor = 41,
    DateTime? from,
    DateTime? until,
    int page = 1,
    int pageSize = 10,
  }) => api.listPatientPeriodHistory(
    anchor,
    fromUtc: from ?? historyFrom,
    untilUtc: until ?? historyUntil,
    page: page,
    pageSize: pageSize,
  );

  test(
    'anchor-bound history is read-only HTTPS UTC metadata, not cached',
    () async {
      var requests = 0;
      final api = _client((request) {
        if (request.url.path != historyPath) return _default(request);
        requests++;
        expect(request.method, 'GET');
        expect(request.body, isEmpty);
        expect(request.url.scheme, 'https');
        expect(request.url.host, 'clinical.invalid');
        expect(request.url.queryParameters, {
          'from': '2026-11-01',
          'until': '2026-12-01',
          'page': '1',
          'pageSize': '2',
        });
        expect(
          request.headers['Authorization'],
          'Bearer synthetic-doctor-token',
        );
        expect(request.headers['Cache-Control'], 'no-store');
        expect(request.followRedirects, isFalse);
        if (requests > 1) return _json({'detail': _synthetic}, 503);
        return _json({
          'page': 1,
          'pageSize': 2,
          'hasMore': true,
          'items': [
            {'appointmentId': 41, 'startedUtc': '2026-11-26T12:00:00+00:00'},
            {'appointmentId': 39, 'startedUtc': '2026-11-26T12:00:00Z'},
          ],
        });
      });
      await expectLater(history(api), throwsA(_failure(401)));
      await _authenticate(api);
      final page = await history(api, pageSize: 2);
      expect(page.hasMore, isTrue);
      expect(page.items.map((item) => item.appointmentId), [41, 39]);
      expect(page.items.first.startedUtc, DateTime.utc(2026, 11, 26, 12));
      expect(() => page.items.clear(), throwsUnsupportedError);
      await expectLater(history(api, pageSize: 2), throwsA(_failure(503)));
      expect(requests, 2);
    },
  );

  test(
    'history rejects bad ids, UTC dates and paging before transport',
    () async {
      final api = _client(_default);
      await _authenticate(api);
      final invalid = <Future<PatientHistoryPage> Function()>[
        () => history(api, anchor: 0),
        () => history(api, anchor: 2147483648),
        () => history(api, page: 0),
        () => history(api, page: 101),
        () => history(api, pageSize: 0),
        () => history(api, pageSize: 21),
        () => history(api, from: DateTime(2026, 11, 1)),
        () => history(api, from: DateTime.utc(2026, 11, 1, 1)),
        () => history(api, until: historyFrom),
        () => history(api, until: DateTime.utc(2026, 12, 3)),
      ];
      for (final attempt in invalid) {
        await expectLater(attempt(), throwsA(_failure(400)));
      }
    },
  );

  test(
    'history permits an exact empty page and rejects payload widening',
    () async {
      const valid = {'appointmentId': 41, 'startedUtc': '2026-11-26T12:00:00Z'};
      final badRows = <Object>[
        [valid, valid],
        [
          {...valid, 'patientId': 1},
        ],
        [
          {...valid, 'appointmentId': '41'},
        ],
        [
          {...valid, 'startedUtc': '2026-11-31T12:00:00Z'},
        ],
        [
          {...valid, 'startedUtc': '2026-11-26T12:00:00+08:00'},
        ],
        [
          {...valid, 'startedUtc': '2026-12-01T00:00:00+00:00'},
        ],
        [
          {...valid, 'appointmentId': 39},
          valid,
        ],
      ];
      var responseBody = <String, Object>{
        'page': 1,
        'pageSize': 2,
        'hasMore': false,
        'items': <Object>[],
      };
      final api = _client(
        (request) => request.url.path == historyPath
            ? _json(responseBody)
            : _default(request),
      );
      await _authenticate(api);
      expect((await history(api, pageSize: 2)).items, isEmpty);
      for (final rows in badRows) {
        responseBody = {...responseBody, 'items': rows};
        await expectLater(history(api, pageSize: 2), throwsA(_failure(null)));
      }
      responseBody = {
        ...responseBody,
        'items': [valid],
        'total': 1,
      };
      await expectLater(history(api, pageSize: 2), throwsA(_failure(null)));
    },
  );

  test(
    'history preserves server tick ordering when Dart UTC times are equal',
    () async {
      final api = _client(
        (request) => request.url.path == historyPath
            ? _json({
                'page': 1,
                'pageSize': 2,
                'hasMore': false,
                'items': [
                  {
                    'appointmentId': 39,
                    'startedUtc': '2026-11-26T12:00:00.1234567Z',
                  },
                  {
                    'appointmentId': 41,
                    'startedUtc': '2026-11-26T12:00:00.1234566+00:00',
                  },
                ],
              })
            : _default(request),
      );
      await _authenticate(api);
      final page = await history(api, pageSize: 2);
      expect(page.items.map((item) => item.appointmentId), [39, 41]);
      expect(page.items.first.startedUtc, page.items.last.startedUtc);
    },
  );

  test('history refuses oversized payloads and cross-host redirects', () async {
    var requests = 0;
    final api = _client((request) {
      if (request.url.path != historyPath) return _default(request);
      requests++;
      expect(request.url.host, 'clinical.invalid');
      expect(request.followRedirects, isFalse);
      if (requests == 1) {
        return http.Response(
          "${jsonEncode({'page': 1, 'pageSize': 10, 'hasMore': false, 'items': []})}${' ' * 8192}",
          200,
        );
      }
      return http.Response(
        '',
        302,
        headers: {'location': 'https://other.invalid/'},
      );
    });
    await _authenticate(api);
    await expectLater(history(api), throwsA(_failure(null)));
    await expectLater(history(api), throwsA(_failure(302)));
    expect(requests, 2);
  });

  test('history discarded after doctor session generation changes', () async {
    final sent = Completer<void>();
    final held = Completer<http.Response>();
    final api = _client((request) {
      if (request.url.path != historyPath) return _default(request);
      sent.complete();
      return held.future;
    });
    await _authenticate(api);
    final pending = history(api);
    final rejected = expectLater(pending, throwsA(_failure(401)));
    await sent.future;
    api.logout();
    held.complete(
      _json({'page': 1, 'pageSize': 10, 'hasMore': false, 'items': []}),
    );
    await rejected;
    expect(api.authenticated, isFalse);
  });

  const procedurePath = '/api/clinical/appointments/41/performed-procedures';
  const row = {
    'requestId': _key,
    'serviceId': 25,
    'serviceLabel': _synthetic,
    'scope': 'SingleTooth',
    'toothCode': '18',
    'signedUtc': _signed,
  };
  Future<PerformedProcedureSignResult> sign(EncounterTreatmentApi api) =>
      api.signPerformedProcedure(
        41,
        requestId: _key,
        serviceId: 25,
        scope: PerformedProcedureScope.singleTooth,
        toothCode: '18',
      );

  test('lists a typed, bounded audit page for a single encounter', () async {
    final api = _client((request) {
      if (request.url.path != procedurePath) return _default(request);
      expect(request.method, 'GET');
      expect(request.url.queryParameters, {'page': '2', 'pageSize': '1'});
      expect(request.headers['Authorization'], 'Bearer synthetic-doctor-token');
      expect(request.headers['Cache-Control'], 'no-store');
      expect(request.followRedirects, isFalse);
      return _json({
        'page': 2,
        'pageSize': 1,
        'hasMore': true,
        'items': [row],
      });
    });
    await expectLater(api.listPerformedProcedures(41), throwsA(_failure(401)));
    await _authenticate(api);
    final page = await api.listPerformedProcedures(41, page: 2, pageSize: 1);
    expect(page.page, 2);
    expect(page.hasMore, isTrue);
    expect(page.items.single.scope, PerformedProcedureScope.singleTooth);
    expect(page.items.single.toothCode, '18');
    expect(page.items.single.signedUtc, DateTime.utc(2026, 11, 27, 12));
    expect(() => page.items.clear(), throwsUnsupportedError);
  });

  test('empty audited page remains an exact 200 read result', () async {
    final api = _client(
      (request) => request.url.path == procedurePath
          ? _json({'page': 1, 'pageSize': 20, 'hasMore': false, 'items': []})
          : _default(request),
    );
    await _authenticate(api);
    expect((await api.listPerformedProcedures(41)).items, isEmpty);
  });

  test(
    'POST 201 and empty 204 use the exact caller UUID and payload',
    () async {
      var requests = 0;
      final api = _client((request) {
        if (request.url.path != procedurePath) return _default(request);
        expect(request.method, 'POST');
        expect(request.url.hasQuery, isFalse);
        expect(
          request.headers['Authorization'],
          'Bearer synthetic-doctor-token',
        );
        expect(request.headers['Content-Type'], contains('application/json'));
        expect(request.followRedirects, isFalse);
        expect(jsonDecode(request.body), {
          'requestId': _key,
          'serviceId': 25,
          'scope': 'SingleTooth',
          'toothCode': '18',
        });
        if (++requests == 1) return _json({...row, 'appointmentId': 41}, 201);
        return http.Response('', 204);
      });
      await _authenticate(api);
      final saved = (await sign(api)) as PerformedProcedureRecorded;
      expect(saved.appointmentId, 41);
      expect(saved.procedure.serviceId, 25);
      expect(saved.procedure.requestId, _key);
      expect(requests, 1);
      expect(await sign(api), isA<PerformedProcedureReplayed>());
      expect(requests, 2);
    },
  );

  test('explicit visit submits null tooth and accepts only a visit', () async {
    final api = _client((request) {
      if (request.url.path != procedurePath) return _default(request);
      expect(jsonDecode(request.body), {
        'requestId': _key,
        'serviceId': 25,
        'scope': 'Visit',
        'toothCode': null,
      });
      return _json({
        ...row,
        'appointmentId': 41,
        'scope': 'Visit',
        'toothCode': null,
      }, 201);
    });
    await _authenticate(api);
    final saved = await api.signPerformedProcedure(
      41,
      requestId: _key,
      serviceId: 25,
      scope: PerformedProcedureScope.visit,
    );
    expect((saved as PerformedProcedureRecorded).procedure.toothCode, isNull);
  });

  test(
    'local ID, page and routine FDI validation rejects invalid calls',
    () async {
      final api = _client(_default);
      await _authenticate(api);
      await expectLater(api.listPerformedProcedures(0), throwsA(_failure(400)));
      await expectLater(
        api.listPerformedProcedures(41, page: 1001),
        throwsA(_failure(400)),
      );
      await expectLater(
        api.listPerformedProcedures(41, pageSize: 51),
        throwsA(_failure(400)),
      );
      for (final (id, key, service, scope, tooth) in [
        (0, _key, 25, PerformedProcedureScope.visit, null),
        (41, 'invalid', 25, PerformedProcedureScope.visit, null),
        (41, _key, 0, PerformedProcedureScope.visit, null),
        (41, _key, 25, PerformedProcedureScope.visit, '18'),
        (41, _key, 25, PerformedProcedureScope.singleTooth, '56'),
        (41, _key, 25, PerformedProcedureScope.singleTooth, '49'),
        (41, _key, 25, PerformedProcedureScope.singleTooth, null),
      ]) {
        await expectLater(
          api.signPerformedProcedure(
            id,
            requestId: key,
            serviceId: service,
            scope: scope,
            toothCode: tooth,
          ),
          throwsA(_failure(400)),
        );
      }
    },
  );

  test(
    '503 and 409 never auto-retry; caller can explicitly retry the same UUID',
    () async {
      final submitted = <String>[];
      final api = _client((request) {
        if (request.url.path != procedurePath) return _default(request);
        submitted.add((jsonDecode(request.body) as Map)['requestId'] as String);
        return switch (submitted.length) {
          1 => _json({'message': _synthetic}, 503),
          2 => _json({'message': _synthetic}, 409),
          _ => http.Response('', 204),
        };
      });
      await _authenticate(api);
      await expectLater(sign(api), throwsA(_failure(503)));
      expect(submitted, [_key]);
      await expectLater(sign(api), throwsA(_failure(409)));
      expect(submitted, [_key, _key]);
      expect(await sign(api), isA<PerformedProcedureReplayed>());
      expect(submitted, [_key, _key, _key]);
    },
  );

  for (final mismatch in <Map<String, Object?>>[
    {'appointmentId': 42},
    {'requestId': _otherKey},
    {'serviceId': 26},
    {'scope': 'Visit', 'toothCode': null},
    {'toothCode': '56'},
    {'toothCode': '17'},
    {'signedUtc': '2026-11-27T20:00:00'},
    {'serviceLabel': ''},
  ]) {
    test('201 response mismatch is never recorded: $mismatch', () async {
      final api = _client(
        (request) => request.url.path == procedurePath
            ? _json({...row, 'appointmentId': 41, ...mismatch}, 201)
            : _default(request),
      );
      await _authenticate(api);
      await expectLater(sign(api), throwsA(_failure(null)));
    });
  }

  test('a 204 with content and a 200 are not valid POST outcomes', () async {
    var calls = 0;
    final api = _client(
      (request) => request.url.path == procedurePath
          ? (++calls == 1
                ? http.Response('not empty', 204)
                : _json({...row, 'appointmentId': 41}))
          : _default(request),
    );
    await _authenticate(api);
    await expectLater(sign(api), throwsA(isA<ApiFailure>()));
    await expectLater(sign(api), throwsA(isA<ApiFailure>()));
  });

  test(
    'list rejects wrong pagination metadata and out-of-contract rows',
    () async {
      var calls = 0;
      final api = _client((request) {
        if (request.url.path != procedurePath) return _default(request);
        return _json({
          'page': calls == 0 ? 2 : 1,
          'pageSize': 20,
          'hasMore': false,
          'items': switch (calls++) {
            0 => [row],
            1 => [row, row],
            2 => [
              {...row, 'scope': 'Unknown'},
            ],
            _ => [
              {...row, 'toothCode': 18},
            ],
          },
        });
      });
      await _authenticate(api);
      for (var i = 0; i < 4; i++) {
        await expectLater(
          api.listPerformedProcedures(41),
          throwsA(_failure(null)),
        );
      }
    },
  );

  test(
    'rejects oversize procedure response bodies without exposing content',
    () async {
      final oversized = List.filled(65537, 'x').join();
      final api = _client(
        (request) => request.url.path == procedurePath
            ? http.Response(oversized, request.method == 'GET' ? 200 : 201)
            : _default(request),
      );
      await _authenticate(api);
      final safeFailure = _failure(null).having(
        (ApiFailure error) => error.message,
        'safe message',
        isNot(contains(oversized.substring(0, 100))),
      );
      await expectLater(api.listPerformedProcedures(41), throwsA(safeFailure));
      await expectLater(sign(api), throwsA(safeFailure));
    },
  );

  test('logout discards an in-flight procedure response', () async {
    final entered = Completer<void>();
    final pending = Completer<http.Response>();
    final api = _client((request) {
      if (request.url.path != procedurePath) return _default(request);
      entered.complete();
      return pending.future;
    });
    await _authenticate(api);
    final request = sign(api);
    await entered.future;
    final fails = expectLater(request, throwsA(_failure(401)));
    api.logout();
    pending.complete(_json({...row, 'appointmentId': 41}, 201));
    await fails;
  });

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
