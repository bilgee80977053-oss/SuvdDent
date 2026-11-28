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
  const assignedPath = '/api/clinical/appointments/assigned';
  final assignedFrom = DateTime.utc(2026, 11, 1);
  final assignedUntil = DateTime.utc(2026, 12, 1);
  const assignedRow = {
    'appointmentId': 41,
    'startUtc': '2026-11-30T23:30:00+00:00',
    'endUtc': '2026-12-01T00:15:00Z',
    'serviceId': 12,
    'serviceLabel': 'Synthetic booked service',
  };

  Future<AssignedEncounterPage> assigned(
    EncounterTreatmentApi api, {
    DateTime? from,
    DateTime? until,
    int page = 1,
    int pageSize = 10,
  }) => api.listAssignedEncounters(
    fromUtc: from ?? assignedFrom,
    untilUtc: until ?? assignedUntil,
    page: page,
    pageSize: pageSize,
  );

  test('lists only typed assigned encounter metadata, by HTTPS GET without a cache', () async {
    var requests = 0;
    final api = _client((request) {
      if (request.url.path != assignedPath) return _default(request);
      requests++;
      expect(request.method, 'GET');
      expect(request.body, isEmpty);
      expect(request.url.origin, 'https://clinical.invalid');
      expect(request.url.queryParameters, {
        'from': '2026-11-01',
        'until': '2026-12-01',
        'page': '2',
        'pageSize': '1',
      });
      expect(request.followRedirects, isFalse);
      expect(request.headers['Cache-Control'], 'no-store');
      expect(request.headers['Authorization'], 'Bearer synthetic-doctor-token');
      if (requests > 1) return _json({'patientName': _synthetic}, 503);
      return _json({
        'page': 2,
        'pageSize': 1,
        'items': [assignedRow],
      });
    });
    await expectLater(assigned(api), throwsA(_failure(401)));
    await _authenticate(api);
    final page = await assigned(api, page: 2, pageSize: 1);
    expect((page.page, page.pageSize), (2, 1));
    final row = page.items.single;
    expect(
      (row.appointmentId, row.serviceId, row.serviceLabel),
      (41, 12, 'Synthetic booked service'),
    );
    expect(row.startUtc, DateTime.utc(2026, 11, 30, 23, 30));
    // Only the start is bounded by the period; this visit can finish next day.
    expect(row.endUtc, DateTime.utc(2026, 12, 1, 0, 15));
    expect(() => page.items.clear(), throwsUnsupportedError);
    await expectLater(
      assigned(api, page: 2, pageSize: 1),
      throwsA(
        _failure(503).having(
          (failure) => failure.message,
          'sanitized',
          isNot(contains(_synthetic)),
        ),
      ),
    );
    expect(requests, 2);
  });

  test(
    'local UTC and page limits reject assigned list before transport',
    () async {
      final api = _client(_default);
      await _authenticate(api);
      final invalid = <Future<AssignedEncounterPage> Function()>[
        () => assigned(api, page: 0),
        () => assigned(api, page: 101),
        () => assigned(api, pageSize: 0),
        () => assigned(api, pageSize: 21),
        () => assigned(api, from: DateTime(2026, 11, 1)),
        () => assigned(api, from: DateTime.utc(2026, 11, 1, 1)),
        () => assigned(api, until: assignedFrom),
        () => assigned(api, until: DateTime.utc(2026, 12, 3)),
      ];
      for (final attempt in invalid) {
        await expectLater(attempt(), throwsA(_failure(400)));
      }
    },
  );

  test('assigned list accepts empty pages and rejects added data and malformed rows', () async {
    var result = <String, Object>{
      'page': 1,
      'pageSize': 2,
      'items': <Object>[],
    };
    final api = _client(
      (request) =>
          request.url.path == assignedPath ? _json(result) : _default(request),
    );
    await _authenticate(api);
    expect((await assigned(api, pageSize: 2)).items, isEmpty);

    final invalid = <Map<String, Object>>[
      {...result, 'hasMore': true},
      {...result, 'total': 5},
      {...result, 'page': 2},
      {...result, 'pageSize': 20},
      {
        ...result,
        'items': [assignedRow, assignedRow],
      },
      {
        ...result,
        'items': [
          for (var i = 1; i <= 3; i++) {...assignedRow, 'appointmentId': i},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'patientId': 1},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'serviceId': 0},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'serviceLabel': 'x' * 161},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'serviceLabel': 'internal\ncontrol'},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'startUtc': '2026-11-31T23:30:00Z'},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'startUtc': '2026-12-01T00:00:00Z'},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'endUtc': '2026-11-30T23:30:00Z'},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'endUtc': '2026-12-01T08:15:00+08:00'},
        ],
      },
      {
        ...result,
        'items': [
          {...assignedRow, 'appointmentId': 39},
          assignedRow,
        ],
      },
    ];
    for (final bad in invalid) {
      result = bad;
      await expectLater(assigned(api, pageSize: 2), throwsA(_failure(null)));
    }
  });

  test('assigned sorting preserves seventh UTC fractional position', () async {
    final api = _client(
      (request) => request.url.path == assignedPath
          ? _json({
              'page': 1,
              'pageSize': 2,
              'items': [
                {
                  ...assignedRow,
                  'appointmentId': 39,
                  'startUtc': '2026-11-30T23:30:00.1234567Z',
                },
                {
                  ...assignedRow,
                  'startUtc': '2026-11-30T23:30:00.1234566+00:00',
                },
              ],
            })
          : _default(request),
    );
    await _authenticate(api);
    final page = await assigned(api, pageSize: 2);
    expect(page.items.map((item) => item.appointmentId), [39, 41]);
    expect(page.items.first.startUtc, page.items.last.startUtc);
  });

  test('assigned list refuses oversized responses and cross-origin redirects', () async {
    var calls = 0;
    final api = _client((request) {
      if (request.url.path != assignedPath) return _default(request);
      calls++;
      expect(request.followRedirects, isFalse);
      if (calls == 1) {
        return http.Response(
          "${jsonEncode({'page': 1, 'pageSize': 10, 'items': []})}${' ' * 32768}",
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
    await expectLater(assigned(api), throwsA(_failure(null)));
    await expectLater(assigned(api), throwsA(_failure(302)));
    expect(calls, 2);
  });

  test('assigned list discards late results after session changes', () async {
    final sent = Completer<void>();
    final delayed = Completer<http.Response>();
    final api = _client((request) {
      if (request.url.path != assignedPath) return _default(request);
      sent.complete();
      return delayed.future;
    });
    await _authenticate(api);
    final result = assigned(api);
    final denied = expectLater(result, throwsA(_failure(401)));
    await sent.future;
    api.logout();
    delayed.complete(
      _json({
        'page': 1,
        'pageSize': 10,
        'items': [assignedRow],
      }),
    );
    await denied;
    expect(api.authenticated, isFalse);
  });

  test(
    'signs an immutable note with an empty same-encounter 201 only',
    () async {
      var writes = 0;
      final api = _client((request) {
        if (request.url.path != _root) return _default(request);
        writes++;
        expect(request.method, 'POST');
        expect(request.url.scheme, 'https');
        expect(request.url.host, 'clinical.invalid');
        expect(request.followRedirects, isFalse);
        expect(request.headers['Cache-Control'], 'no-store');
        expect(
          request.headers['Authorization'],
          'Bearer synthetic-doctor-token',
        );
        expect(jsonDecode(request.body), {'note': ' Synthetic note '});
        return http.Response('', 201, headers: {'Location': _root});
      });
      await expectLater(
        api.signTreatment(41, note: _synthetic),
        throwsA(_failure(401)),
      );
      await _authenticate(api);
      await api.signTreatment(41, note: ' Synthetic note ');
      expect(writes, 1);
    },
  );

  test('initial write ambiguity requires an explicit audited GET, never POST retry', () async {
    var posts = 0;
    var reads = 0;
    final api = _client((request) {
      if (request.url.path != _root) return _default(request);
      if (request.method == 'POST') {
        posts++;
        throw http.ClientException(_synthetic);
      }
      reads++;
      expect(request.method, 'GET');
      return _json({
        'appointmentId': 41,
        'note': _synthetic,
        'signedUtc': _signed,
      });
    });
    await _authenticate(api);
    await expectLater(
      api.signTreatment(41, note: _synthetic),
      throwsA(
        isA<EncounterWriteOutcomeUncertain>()
            .having(
              (failure) => failure.reconciliation,
              'read',
              EncounterWriteReconciliation.originalNote,
            )
            .having(
              (failure) => failure.message,
              'sanitized message',
              isNot(contains(_synthetic)),
            ),
      ),
    );
    expect(posts, 1);
    expect(reads, 0);
    expect((await api.readTreatment(41)).note, _synthetic);
    expect(reads, 1);
    expect(posts, 1);
  });

  test('original 409 is authoritative, not a successful replay', () async {
    var sends = 0;
    final api = _client((request) {
      if (request.url.path != _root) return _default(request);
      sends++;
      expect(request.method, 'POST');
      return _json({'message': _synthetic, 'note': _synthetic}, 409);
    });
    await _authenticate(api);
    await expectLater(
      api.signTreatment(41, note: _synthetic),
      throwsA(
        _failure(409)
            .having(
              (failure) => failure is EncounterWriteOutcomeUncertain,
              'uncertain',
              isFalse,
            )
            .having(
              (failure) => failure.message,
              'message',
              isNot(contains(_synthetic)),
            ),
      ),
    );
    expect(sends, 1);
  });

  test(
    'malformed initial success and 503 never reveal or imply a note',
    () async {
      final responses = <http.Response>[
        http.Response('null', 201, headers: {'location': _root}),
        http.Response(_synthetic, 201, headers: {'location': _root}),
        http.Response('', 201),
        http.Response(
          '',
          201,
          headers: {'location': 'https://other.invalid$_root'},
        ),
        http.Response('', 201, headers: {'location': '$_root?secret=1'}),
        http.Response('', 204),
        _json({'detail': _synthetic}, 503),
      ];
      var posts = 0;
      final api = _client((request) {
        if (request.url.path != _root) return _default(request);
        expect(request.method, 'POST');
        return responses[posts++];
      });
      await _authenticate(api);
      for (var i = 0; i < responses.length; i++) {
        await expectLater(
          api.signTreatment(41, note: _synthetic),
          throwsA(
            isA<EncounterWriteOutcomeUncertain>().having(
              (failure) => failure.message,
              'message',
              isNot(contains(_synthetic)),
            ),
          ),
        );
      }
      expect(posts, responses.length);
    },
  );

  test(
    'addendum writes use the caller UUID and accept only empty 201 or 204',
    () async {
      var posts = 0;
      final path = '$_root/addenda';
      final api = _client((request) {
        if (request.url.path != path) return _default(request);
        posts++;
        expect(request.method, 'POST');
        expect(request.followRedirects, isFalse);
        expect(request.headers['Cache-Control'], 'no-store');
        expect(
          request.headers['Authorization'],
          'Bearer synthetic-doctor-token',
        );
        expect(jsonDecode(request.body), {
          'requestId': _key,
          'note': _synthetic,
        });
        if (posts == 1) {
          return http.Response(
            '',
            201,
            headers: {'Location': 'https://clinical.invalid$path/$_key'},
          );
        }
        if (posts == 2) return http.Response('', 204);
        if (posts == 3) return _json({'note': _synthetic}, 409);
        return http.Response(_synthetic, 204);
      });
      await _authenticate(api);
      Future<EncounterAddendumSignResult> sign() =>
          api.signAddendum(41, requestId: _key.toUpperCase(), note: _synthetic);
      expect(await sign(), EncounterAddendumSignResult.recorded);
      expect(await sign(), EncounterAddendumSignResult.replayed);
      await expectLater(sign(), throwsA(_failure(409)));
      await expectLater(
        sign(),
        throwsA(
          isA<EncounterWriteOutcomeUncertain>().having(
            (failure) => failure.reconciliation,
            'read',
            EncounterWriteReconciliation.keyedAddendum,
          ),
        ),
      );
      expect(posts, 4);
    },
  );

  test('invalid writes and identifiers never reach transport', () async {
    final api = _client(_default);
    await _authenticate(api);
    final attempts = <Future<void> Function()>[
      () => api.signTreatment(0, note: _synthetic),
      () => api.signTreatment(41, note: '   '),
      () => api.signTreatment(41, note: 'x' * 2001),
      () => api.signTreatment(41, note: 'text\u0000text'),
      () => api.signAddendum(41, requestId: 'wrong', note: _synthetic),
      () => api.signAddendum(41, requestId: _key, note: 'text\ttext'),
    ];
    for (final attempt in attempts) {
      await expectLater(attempt(), throwsA(_failure(400)));
    }
  });

  test(
    'late signed acknowledgement after logout does not reach old session',
    () async {
      final sent = Completer<void>();
      final delayed = Completer<http.Response>();
      final api = _client((request) {
        if (request.url.path != _root) return _default(request);
        expect(request.method, 'POST');
        sent.complete();
        return delayed.future;
      });
      await _authenticate(api);
      final result = api.signTreatment(41, note: _synthetic);
      final denied = expectLater(result, throwsA(_failure(401)));
      await sent.future;
      api.logout();
      delayed.complete(http.Response('', 201, headers: {'Location': _root}));
      await denied;
      expect(api.authenticated, isFalse);
    },
  );

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
