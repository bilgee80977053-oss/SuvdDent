import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexora_dental_mobile/live/clinician_review_entry.dart';
import 'package:nexora_dental_mobile/live/models.dart';

const _assigned = '/api/clinical/appointments/assigned';
const _treatment = '/api/clinical/appointments/86/treatment';
const _procedure = '/api/clinical/appointments/86/performed-procedures';
const _service = 'Synthetic selected service';
const _login = 'Эмчээр нэвтрэх';
const _goBack = 'Хуваарилсан үзлэг рүү буцах';
const _record = 'Ажилбар бүртгэх';
const _confirm = 'Баталгаажуулж бүртгэх';

http.Response _json(Object body, [int status = 200]) =>
    http.Response(jsonEncode(body), status);

Map<String, Object> _encounter([int id = 86]) => {
  'appointmentId': id,
  'startUtc': '2026-11-27T12:00:00Z',
  'endUtc': '2026-11-27T12:30:00Z',
  'serviceId': 25,
  'serviceLabel': _service,
};

http.Response _page([List<Object>? items, int page = 1]) => _json({
  'page': page,
  'pageSize': 10,
  'items': items ?? [_encounter()],
});

http.Response _signedHistory([List<Object> items = const []]) =>
    _json({'page': 1, 'pageSize': 20, 'hasMore': false, 'items': items});

http.Response _session(
  http.Request request, {
  int clinic = 7,
  String role = 'Doctor',
  String? identityRole,
}) => switch (request.url.path) {
  '/api/Auth/login' => _json({
    'token': 'synthetic-doctor-token-$clinic',
    'userId': 15,
    'fullName': 'Synthetic Doctor',
    'role': role,
    'clinicId': clinic,
  }),
  '/api/Auth/me' => _json({
    'role': identityRole ?? role,
    'userId': '15',
    'clinicId': '$clinic',
  }),
  _ => throw StateError('Unexpected fixture endpoint: ${request.url.path}'),
};

Future<void> _mount(
  WidgetTester tester,
  http.Client Function() client, {
  int clinic = 7,
  bool? enabled = true,
  String url = 'https://review.invalid',
  int from = 26,
  int until = 28,
  bool settle = true,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      home: enabled == null
          ? ClinicianReviewEntry(
              settings: ApiSettings(baseUrl: url, clinicId: clinic),
              clientFactory: client,
              fromUtc: DateTime.utc(2026, 11, from),
              untilUtc: DateTime.utc(2026, 11, until),
            )
          : ClinicianReviewEntry(
              settings: ApiSettings(baseUrl: url, clinicId: clinic),
              clientFactory: client,
              fromUtc: DateTime.utc(2026, 11, from),
              untilUtc: DateTime.utc(2026, 11, until),
              enabled: enabled,
            ),
    ),
  );
  if (settle) await tester.pumpAndSettle();
}

Future<void> _tap(
  WidgetTester tester,
  String label, {
  bool settle = true,
}) async {
  await tester.tap(find.text(label));
  if (settle) {
    await tester.pumpAndSettle();
  } else {
    await tester.pump();
  }
}

Future<void> _fill(WidgetTester tester, {bool settle = true}) async {
  await tester.enterText(
    find.byType(TextField).at(0),
    'review@example.invalid',
  );
  await tester.enterText(find.byType(TextField).at(1), 'synthetic-password');
  await _tap(tester, _login, settle: settle);
}

class _TrackingClient extends MockClient {
  _TrackingClient(super.handler);

  bool wasClosed = false;

  @override
  void close() {
    wasClosed = true;
    super.close();
  }
}

void main() {
  testWidgets('inactive or invalid review settings never allocate a client', (
    tester,
  ) async {
    http.Client forbidden() => throw StateError('Client must not be allocated');
    await _mount(tester, forbidden, enabled: null);
    expect(find.textContaining('горим идэвхгүй'), findsOneWidget);
    expect(find.text(_login), findsNothing);
    await _mount(tester, forbidden, url: 'http://review.invalid');
    expect(find.textContaining('HTTPS'), findsOneWidget);
    await _mount(tester, forbidden, from: 28, until: 28);
    expect(find.textContaining('1–31'), findsOneWidget);
    expect(find.byType(TextField), findsNothing);
  });

  testWidgets('rejects the wrong role before requesting assignments', (
    tester,
  ) async {
    final requests = <String>[];
    await _mount(
      tester,
      () => MockClient((request) async {
        requests.add(request.url.path);
        return _session(request, identityRole: 'Patient');
      }),
    );
    await _fill(tester);
    expect(requests, ['/api/Auth/login', '/api/Auth/me']);
    expect(find.textContaining('эрхийг баталгаажуулж'), findsOneWidget);
    expect(find.text(_service), findsNothing);
    expect(
      tester.widget<TextField>(find.byType(TextField).at(1)).controller!.text,
      isEmpty,
    );
  });

  testWidgets(
    'bounds UTC request and opens panel from server-selected service',
    (tester) async {
      final procedureMethods = <String>[];
      Map<String, Object?>? signed;
      await _mount(
        tester,
        () => MockClient((request) async {
          expect(request.followRedirects, isFalse);
          if (request.url.path == _assigned) {
            expect(
              request.headers['Authorization'],
              'Bearer synthetic-doctor-token-7',
            );
            expect(request.url.queryParameters, {
              'from': '2026-11-26',
              'until': '2026-11-28',
              'page': '1',
              'pageSize': '10',
            });
            return _page();
          }
          if (request.url.path == _procedure) {
            procedureMethods.add(request.method);
            if (request.method == 'GET') {
              final row = signed;
              return _signedHistory(row == null ? [] : [row]);
            }
            final data = jsonDecode(request.body) as Map<String, Object?>;
            expect(data.keys.toSet(), {
              'serviceId',
              'requestId',
              'scope',
              'toothCode',
            });
            expect(data['serviceId'], 25);
            expect(data['scope'], 'Visit');
            expect(data['toothCode'], isNull);
            expect(data['requestId'], isA<String>());
            signed = {
              ...data,
              'serviceLabel': _service,
              'signedUtc': '2026-11-27T12:00:00Z',
            };
            return _json({...signed!, 'appointmentId': 86}, 201);
          }
          return _session(request);
        }),
      );
      await _fill(tester);
      expect(find.text(_service), findsOneWidget);
      expect(procedureMethods, isEmpty);
      await _tap(tester, _service);
      expect(find.text('Бүртгэл хараахан байхгүй.'), findsOneWidget);
      await _tap(tester, 'Үзлэг');
      await _tap(tester, _record);
      expect(procedureMethods, ['GET']);
      await _tap(tester, _confirm);
      expect(procedureMethods, ['GET', 'POST', 'GET']);
      expect(find.textContaining('$_service · Үзлэг'), findsOneWidget);
      expect(
        tester
            .widget<TextButton>(find.widgetWithText(TextButton, _goBack))
            .onPressed,
        isNotNull,
      );
      await _tap(tester, _goBack);
      expect(find.text('Хуваарилсан үзлэг'), findsOneWidget);
    },
  );

  testWidgets('keeps review selection while signing is uncertain', (
    tester,
  ) async {
    final bodies = <String>[];
    await _mount(
      tester,
      () => MockClient((request) async {
        switch (request.url.path) {
          case _assigned:
            return _page();
          case _procedure:
            if (request.method == 'GET') return _signedHistory();
            bodies.add(request.body);
            return bodies.length == 1 ? _json({}, 503) : http.Response('', 204);
          default:
            return _session(request);
        }
      }),
    );
    await _fill(tester);
    await _tap(tester, _service);
    await _tap(tester, 'Үзлэг');
    await _tap(tester, _record);
    await _tap(tester, _confirm);
    expect(bodies, hasLength(1));
    expect(find.textContaining('Үзлэг солихын өмнө'), findsOneWidget);
    expect(
      tester
          .widget<TextButton>(find.widgetWithText(TextButton, _goBack))
          .onPressed,
      isNull,
    );
    await _tap(tester, 'Тэмдэглэл');
    expect(
      find.byKey(const ValueKey('clinical-original-draft')),
      findsOneWidget,
    );
    expect(find.text('Ижил хүсэлтийг давтах'), findsNothing);
    expect(bodies, hasLength(1));
    await _tap(tester, 'Ажилбар');
    await _tap(tester, 'Ижил хүсэлтийг давтах');
    expect(bodies, hasLength(2));
    expect(bodies.first, bodies.last);
    expect(
      tester
          .widget<TextButton>(find.widgetWithText(TextButton, _goBack))
          .onPressed,
      isNotNull,
    );
  });

  testWidgets(
    'shares note scope and retains unresolved first sign across tabs',
    (tester) async {
      final calls = <String>[];
      var reveal = false;
      await _mount(
        tester,
        () => MockClient((request) async {
          switch (request.url.path) {
            case _assigned:
              return _page();
            case _procedure:
              return _signedHistory();
            case _treatment:
              expect(
                request.headers['Authorization'],
                'Bearer synthetic-doctor-token-7',
              );
              calls.add(request.method);
              if (request.method == 'POST') {
                expect(jsonDecode(request.body), {
                  'note': 'Synthetic unsigned note',
                });
                return _json({}, 503);
              }
              return reveal
                  ? _json({
                      'appointmentId': 86,
                      'note': 'Different synthetic saved original',
                      'signedUtc': '2026-11-27T12:00:00Z',
                    })
                  : _json({}, 404);
            default:
              return _session(request);
          }
        }),
      );
      final back = find.widgetWithText(TextButton, _goBack);
      final read = find.byKey(const ValueKey('clinical-read-original'));
      final draft = find.byKey(const ValueKey('clinical-original-draft'));
      await _fill(tester);
      await _tap(tester, _service);
      expect(calls, isEmpty);
      await _tap(tester, 'Тэмдэглэл');
      await tester.enterText(draft, 'Synthetic unsigned note');
      await tester.tap(find.byKey(const ValueKey('clinical-sign-original')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('clinical-confirm-original')));
      await tester.pumpAndSettle();
      expect(calls, ['POST', 'GET']);
      expect(tester.widget<TextButton>(back).onPressed, isNull);
      await _tap(tester, 'Ажилбар');
      expect(find.text('Synthetic unsigned note'), findsNothing);
      expect(tester.widget<TextButton>(back).onPressed, isNull);
      await _tap(tester, 'Тэмдэглэл');
      expect(
        tester.widget<TextField>(draft).controller!.text,
        'Synthetic unsigned note',
      );
      expect(calls, ['POST', 'GET']);

      reveal = true;
      await tester.tap(read);
      await tester.pumpAndSettle();
      expect(calls, ['POST', 'GET', 'GET']);
      expect(find.text('Different synthetic saved original'), findsOneWidget);
      expect(find.textContaining('хадгалагдсан гэж үзэхгүй'), findsOneWidget);
      expect(tester.widget<TextButton>(back).onPressed, isNotNull);
    },
  );

  testWidgets(
    'new selection and logout clear protected drafts and reviewed text',
    (tester) async {
      final reads = <String>[];
      http.Client client() => MockClient((request) async {
        switch (request.url.path) {
          case _assigned:
            return _page([
              _encounter(),
              {..._encounter(85), 'serviceLabel': 'Other synthetic service'},
            ]);
          case _procedure:
          case '/api/clinical/appointments/85/performed-procedures':
            return _signedHistory();
          case '/api/clinical/appointments/85/treatment':
            reads.add(request.url.path);
            return _json({
              'appointmentId': 85,
              'note': 'Other synthetic signed note',
              'signedUtc': '2026-11-27T12:00:00Z',
            });
          default:
            return _session(request);
        }
      });
      final original = find.byKey(const ValueKey('clinical-original-draft'));
      final addendum = find.byKey(const ValueKey('clinical-addendum-draft'));
      await _mount(tester, client);
      await _fill(tester);
      await _tap(tester, _service);
      await _tap(tester, 'Тэмдэглэл');
      await tester.enterText(original, 'Do not carry the old draft');
      await _tap(tester, _goBack);
      await _tap(tester, 'Other synthetic service');
      await _tap(tester, 'Тэмдэглэл');
      expect(tester.widget<TextField>(original).controller!.text, isEmpty);
      await tester.tap(find.byKey(const ValueKey('clinical-read-original')));
      await tester.pumpAndSettle();
      expect(reads, ['/api/clinical/appointments/85/treatment']);
      expect(find.text('Other synthetic signed note'), findsOneWidget);
      await tester.enterText(addendum, 'Do not carry the addendum draft');
      await _tap(tester, 'Гарах');
      expect(find.text('Other synthetic signed note'), findsNothing);
      expect(find.text('Do not carry the addendum draft'), findsNothing);
      await _fill(tester);
      await _tap(tester, 'Other synthetic service');
      await _tap(tester, 'Тэмдэглэл');
      expect(tester.widget<TextField>(original).controller!.text, isEmpty);
      expect(reads, hasLength(1));
    },
  );

  testWidgets('denied assignments are not selectable and hide remote details', (
    tester,
  ) async {
    await _mount(
      tester,
      () => MockClient(
        (request) async => request.url.path == _assigned
            ? _json({'message': 'must-not-leak'}, 404)
            : _session(request),
      ),
    );
    await _fill(tester);
    expect(find.textContaining('хандах эрхгүй'), findsOneWidget);
    expect(find.textContaining('must-not-leak'), findsNothing);
    expect(find.byType(ListTile), findsNothing);
  });

  testWidgets('logout closes the transport and removes selected data', (
    tester,
  ) async {
    final client = _TrackingClient(
      (request) async => switch (request.url.path) {
        _assigned => _page(),
        _procedure => _signedHistory(),
        _ => _session(request),
      },
    );
    await _mount(tester, () => client);
    await _fill(tester);
    await _tap(tester, _service);
    await _tap(tester, 'Гарах');
    expect(client.wasClosed, isTrue);
    expect(find.text(_service), findsNothing);
    expect(find.text(_record), findsNothing);
    expect(find.text(_login), findsOneWidget);
  });

  testWidgets('clinic change closes active transport and ignores old results', (
    tester,
  ) async {
    final pending = Completer<http.Response>();
    final first = _TrackingClient(
      (request) async =>
          request.url.path == _assigned ? pending.future : _session(request),
    );
    final second = _TrackingClient((request) async {
      if (request.url.path == _assigned) {
        expect(
          request.headers['Authorization'],
          'Bearer synthetic-doctor-token-8',
        );
        return _page([]);
      }
      return _session(request, clinic: 8);
    });
    await _mount(tester, () => first);
    await _fill(tester, settle: false);
    await tester.pump();
    expect(find.byType(LinearProgressIndicator), findsOneWidget);
    await _mount(tester, () => second, clinic: 8);
    expect(first.wasClosed, isTrue);
    pending.complete(_page());
    // Allow the discarded old mock response's timer to reach its limit.
    await tester.pump(const Duration(seconds: 21));
    await tester.pumpAndSettle();
    expect(find.text(_service), findsNothing);
    expect(find.text(_login), findsOneWidget);
    await _fill(tester);
    expect(find.textContaining('хуваарилсан үзлэг алга'), findsOneWidget);
    expect(find.text(_service), findsNothing);
  });

  testWidgets('full assignment page permits bounded next and previous reads', (
    tester,
  ) async {
    final pages = <String?>[];
    await _mount(
      tester,
      () => MockClient((request) async {
        if (request.url.path != _assigned) return _session(request);
        final page = request.url.queryParameters['page'];
        pages.add(page);
        expect(request.url.queryParameters['pageSize'], '10');
        return page == '2'
            ? _page([], 2)
            : _page([for (var id = 95; id >= 86; id--) _encounter(id)]);
      }),
    );
    await _fill(tester);
    await tester.ensureVisible(find.text('Дараах хуудас'));
    await tester.pumpAndSettle();
    await _tap(tester, 'Дараах хуудас');
    expect(find.textContaining('хуваарилсан үзлэг алга'), findsOneWidget);
    expect(
      tester
          .widget<TextButton>(find.widgetWithText(TextButton, 'Дараах хуудас'))
          .onPressed,
      isNull,
    );
    await _tap(tester, 'Өмнөх хуудас');
    expect(pages, ['1', '2', '1']);
  });
}
