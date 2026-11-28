import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexora_dental_mobile/live/encounter_treatment_api.dart';
import 'package:nexora_dental_mobile/live/models.dart';
import 'package:nexora_dental_mobile/live/performed_procedure_panel.dart';

const _path = '/api/clinical/appointments/41/performed-procedures';
const _key = '6a8d6b33-d750-48c7-ae26-395323d28061';
const _label = 'Synthetic procedure';
const _record = 'Ажилбар бүртгэх';
const _confirm = 'Баталгаажуулж бүртгэх';
const _retry = 'Ижил хүсэлтийг давтах';
const _refresh = 'Түүх шинэчлэх';

http.Response _reply(Object value, [int status = 200]) =>
    http.Response(jsonEncode(value), status);

Map<String, Object?> _row({String? tooth}) => {
  'requestId': _key,
  'serviceId': 25,
  'serviceLabel': _label,
  'scope': tooth == null ? 'Visit' : 'SingleTooth',
  'toothCode': tooth,
  'signedUtc': '2026-11-27T12:00:00Z',
};

http.Response _page([List<Map<String, Object?>> rows = const []]) =>
    _reply({'page': 1, 'pageSize': 20, 'hasMore': false, 'items': rows});

Future<EncounterTreatmentApi> _auth(
  WidgetTester tester,
  FutureOr<http.Response> Function(http.Request) procedures,
) async {
  final api = EncounterTreatmentApi(
    const ApiSettings(baseUrl: 'https://clinical.invalid', clinicId: 7),
    client: MockClient(
      (request) async => switch (request.url.path) {
        '/api/Auth/login' => _reply({
          'token': 'synthetic-doctor-token',
          'userId': 15,
          'fullName': 'Synthetic Doctor',
          'role': 'Doctor',
          'clinicId': 7,
        }),
        '/api/Auth/me' => _reply({
          'role': 'Doctor',
          'userId': '15',
          'clinicId': '7',
        }),
        _ => await procedures(request),
      },
    ),
  );
  addTearDown(api.close);
  await tester.runAsync(
    () => api.login('synthetic@example.invalid', 'not-a-real-secret'),
  );
  return api;
}

Future<void> _mount(
  WidgetTester tester,
  EncounterTreatmentApi api, {
  int appointmentId = 41,
  String serviceLabel = _label,
  String Function()? keyFactory,
  bool settle = true,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: PerformedProcedurePanel(
          api: api,
          appointmentId: appointmentId,
          bookedServiceId: 25,
          bookedServiceLabel: serviceLabel,
          requestIdFactory: keyFactory ?? () => _key,
        ),
      ),
    ),
  );
  if (settle) await tester.pumpAndSettle();
}

Future<void> _tap(WidgetTester tester, String label) async {
  await tester.tap(find.text(label));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('invalid scope and abandoned confirmation send no POST', (
    tester,
  ) async {
    final methods = <String>[];
    var generated = 0;
    final api = await _auth(tester, (request) {
      expect(request.url.path, _path);
      expect(request.headers['Authorization'], 'Bearer synthetic-doctor-token');
      methods.add(request.method);
      return _page();
    });
    await _mount(
      tester,
      api,
      keyFactory: () {
        generated++;
        return _key;
      },
    );
    await _tap(tester, _record);
    expect(find.textContaining('FDI дугаар'), findsOneWidget);
    await _tap(tester, 'Нэг шүд');
    await tester.enterText(find.byType(TextField), '56');
    await _tap(tester, _record);
    expect(find.text(_confirm), findsNothing);
    await tester.enterText(find.byType(TextField), '55');
    await _tap(tester, _record);
    expect(find.textContaining('Шүд: 55'), findsOneWidget);
    await _tap(tester, 'Буцах');
    expect(generated, 0);
    expect(methods, ['GET']);
  });

  for (final String? tooth in [null, '18']) {
    testWidgets('explicit confirmation signs $tooth once and refreshes', (
      tester,
    ) async {
      final bodies = <Object?>[];
      var reads = 0;
      final api = await _auth(tester, (request) {
        expect(request.url.path, _path);
        if (request.method == 'GET') {
          reads++;
          return _page(bodies.isEmpty ? [] : [_row(tooth: tooth)]);
        }
        bodies.add(jsonDecode(request.body));
        return _reply({..._row(tooth: tooth), 'appointmentId': 41}, 201);
      });
      await _mount(tester, api);
      await _tap(tester, tooth == null ? 'Үзлэг' : 'Нэг шүд');
      if (tooth != null) await tester.enterText(find.byType(TextField), tooth);
      await _tap(tester, _record);
      expect(bodies, isEmpty);
      await _tap(tester, _confirm);
      expect(bodies, [
        {
          'requestId': _key,
          'serviceId': 25,
          'scope': tooth == null ? 'Visit' : 'SingleTooth',
          'toothCode': tooth,
        },
      ]);
      expect(reads, 2);
      expect(find.text('Ажилбар бүртгэгдлээ.'), findsOneWidget);
      expect(find.textContaining('$_label ·'), findsOneWidget);
      expect(find.text(_record), findsNothing);
      expect(find.text(_retry), findsNothing);
    });
  }

  testWidgets('uncertain POST keeps exact intent for one explicit retry', (
    tester,
  ) async {
    final bodies = <String>[];
    var reads = 0, generated = 0;
    final api = await _auth(tester, (request) {
      if (request.method == 'GET') {
        reads++;
        return _page(bodies.length == 2 ? [_row()] : []);
      }
      bodies.add(request.body);
      return bodies.length == 1
          ? _reply({'message': 'private response content'}, 503)
          : http.Response('', 204);
    });
    await _mount(
      tester,
      api,
      keyFactory: () {
        generated++;
        return _key;
      },
    );
    await _tap(tester, 'Үзлэг');
    await _tap(tester, _record);
    await _tap(tester, _confirm);
    expect(find.textContaining('Үр дүн тодорхойгүй'), findsOneWidget);
    expect(find.textContaining('private response content'), findsNothing);
    expect(find.text(_record), findsNothing);
    expect(bodies, hasLength(1));
    // A display-only update cannot erase an unresolved request.
    await _mount(
      tester,
      api,
      serviceLabel: 'Updated label',
      keyFactory: () {
        generated++;
        return _key;
      },
    );
    expect(find.text(_record), findsNothing);
    await _tap(tester, _retry);
    expect(bodies, hasLength(2));
    expect(bodies.first, bodies.last);
    expect(generated, 1);
    expect(reads, 2);
    expect(find.textContaining('Өмнөх хүсэлт бүртгэгдсэн'), findsOneWidget);
    expect(find.text(_retry), findsNothing);
  });

  testWidgets(
    'read refresh can reconcile an exact pending item without a POST',
    (tester) async {
      var writes = 0;
      final api = await _auth(tester, (request) {
        if (request.method == 'GET') return _page(writes > 0 ? [_row()] : []);
        writes++;
        return _reply({}, 503);
      });
      await _mount(tester, api);
      await _tap(tester, 'Үзлэг');
      await _tap(tester, _record);
      await _tap(tester, _confirm);
      expect(find.text(_retry), findsOneWidget);
      await _tap(tester, _refresh);
      expect(writes, 1);
      expect(find.textContaining('түүхээр баталгаажлаа'), findsOneWidget);
      expect(find.text(_retry), findsNothing);
    },
  );

  testWidgets('conflict is not retried or replaced with a fresh UUID', (
    tester,
  ) async {
    var writes = 0;
    final api = await _auth(tester, (request) {
      if (request.method == 'GET') return _page(writes > 0 ? [_row()] : []);
      writes++;
      return _reply({}, 409);
    });
    await _mount(tester, api);
    await _tap(tester, 'Үзлэг');
    await _tap(tester, _record);
    await _tap(tester, _confirm);
    await _tap(tester, _refresh);
    expect(writes, 1);
    expect(
      find.textContaining('Хүсэлт зөрчилтэй'),
      findsOneWidget,
      reason: tester
          .widgetList<Text>(find.byType(Text))
          .map((t) => t.data)
          .join(' | '),
    );
    expect(find.text(_record), findsNothing);
    expect(find.text(_retry), findsNothing);
  });

  testWidgets('read denial hides controls and redacts remote content', (
    tester,
  ) async {
    final methods = <String>[];
    final api = await _auth(tester, (request) {
      methods.add(request.method);
      return _reply({'message': 'sensitive'}, 403);
    });
    await _mount(tester, api);
    expect(
      find.textContaining('хандах эрхгүй'),
      findsOneWidget,
      reason: tester
          .widgetList<Text>(find.byType(Text))
          .map((t) => t.data)
          .join(' | '),
    );
    expect(find.textContaining('sensitive'), findsNothing);
    expect(find.text(_record), findsNothing);
    expect(methods, ['GET']);
  });

  testWidgets(
    'loading blocks choices; leaving during signing ignores receipt',
    (tester) async {
      final initial = Completer<http.Response>();
      final signing = Completer<http.Response>();
      final methods = <String>[];
      final api = await _auth(tester, (request) {
        methods.add(request.method);
        return request.method == 'GET' ? initial.future : signing.future;
      });
      await _mount(tester, api, settle: false);
      expect(find.byType(LinearProgressIndicator), findsOneWidget);
      expect(
        tester
            .widget<FilledButton>(find.widgetWithText(FilledButton, _record))
            .onPressed,
        isNull,
      );
      initial.complete(_page());
      await tester.pumpAndSettle();
      await _tap(tester, 'Үзлэг');
      await _tap(tester, _record);
      await tester.tap(find.text(_confirm));
      await tester.pump(const Duration(milliseconds: 300));
      expect(methods, ['GET', 'POST']);
      expect(find.byType(LinearProgressIndicator), findsOneWidget);
      await tester.pumpWidget(const MaterialApp(home: Text('Left')));
      signing.complete(_reply({..._row(), 'appointmentId': 41}, 201));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(methods, ['GET', 'POST']);
    },
  );
}
