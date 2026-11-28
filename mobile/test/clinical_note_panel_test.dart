import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexora_dental_mobile/live/clinical_note_panel.dart';
import 'package:nexora_dental_mobile/live/encounter_treatment_api.dart';
import 'package:nexora_dental_mobile/live/models.dart';

const root = '/api/clinical/appointments/41/treatment';
const keyId = '6a8d6b33-d750-48c7-ae26-395323d28061';
const timestamp = '2026-11-27T20:00:00+00:00';
const originalSaved = 'Анхны тэмдэглэл баталгаажлаа.';
const addendumSaved = 'Нэмэлт баталгаажлаа.';

Finder button(String key) => find.byKey(ValueKey(key));
http.Response json(Object value, [int status = 200]) => http.Response(
  jsonEncode(value),
  status,
  headers: {'content-type': 'application/json; charset=utf-8'},
);
http.Response note(String text, {int appointment = 41}) =>
    json({'appointmentId': appointment, 'note': text, 'signedUtc': timestamp});
http.Response addition(String text) =>
    json({'requestId': keyId, 'note': text, 'signedUtc': timestamp});

Future<EncounterTreatmentApi> authenticated(
  FutureOr<http.Response> Function(http.Request) operation,
) async {
  final api = EncounterTreatmentApi(
    const ApiSettings(baseUrl: 'https://clinical.invalid', clinicId: 7),
    client: MockClient((request) async {
      return switch (request.url.path) {
        '/api/Auth/login' => json({
          'token': 'synthetic-token',
          'userId': 13,
          'fullName': 'Synthetic clinician',
          'role': 'Doctor',
          'clinicId': 7,
        }),
        '/api/Auth/me' => json({
          'role': 'Doctor',
          'userId': '13',
          'clinicId': '7',
        }),
        _ => await operation(request),
      };
    }),
  );
  await api.login('synthetic@example.invalid', 'synthetic-password');
  addTearDown(api.close);
  return api;
}

Future<void> show(
  WidgetTester tester,
  EncounterTreatmentApi api, {
  int id = 41,
  String Function()? createRequestId,
}) => tester.pumpWidget(
  MaterialApp(
    home: Scaffold(
      body: SingleChildScrollView(
        child: ClinicalNotePanel(
          api: api,
          appointmentId: id,
          createAddendumRequestId: createRequestId ?? () => keyId,
        ),
      ),
    ),
  ),
);

Future<void> tap(WidgetTester tester, String key) async {
  final target = button(key);
  await tester.ensureVisible(target);
  await tester.tap(target);
  await tester.pumpAndSettle();
}

Future<void> enter(WidgetTester tester, String key, String value) async {
  final target = button(key);
  await tester.ensureVisible(target);
  await tester.enterText(target, value);
  await tester.pump();
}

void main() {
  testWidgets(
    'original sign requires explicit confirmation and never reopens',
    (tester) async {
      var posts = 0;
      var reads = 0;
      final api = await authenticated((request) {
        expect(request.url.path, root);
        if (request.method == 'POST') {
          posts++;
          expect(jsonDecode(request.body), {'note': 'Synthetic original'});
          return http.Response('', 201, headers: {'location': root});
        }
        reads++;
        return note('Synthetic original');
      });
      await show(tester, api);
      await enter(tester, 'clinical-original-draft', 'Synthetic original');
      await tap(tester, 'clinical-sign-original');
      expect(posts, 0);
      expect(
        find.text('Баталгаажсаны дараа эх хувийг солих боломжгүй.'),
        findsOneWidget,
      );
      await tester.tap(find.text('Буцах'));
      await tester.pumpAndSettle();
      expect(posts, 0);
      await tap(tester, 'clinical-sign-original');
      await tap(tester, 'clinical-confirm-original');
      expect(posts, 1);
      expect(reads, 0);
      expect(find.text(originalSaved), findsOneWidget);
      expect(button('clinical-sign-original'), findsNothing);
      await tap(tester, 'clinical-read-original');
      expect(reads, 1);
      expect(button('clinical-original-content'), findsOneWidget);
    },
  );

  testWidgets('different audited original is not success after lost response', (
    tester,
  ) async {
    final methods = <String>[];
    final api = await authenticated((request) {
      methods.add(request.method);
      if (request.method == 'POST') {
        throw http.ClientException('Synthetic failure');
      }
      // .NET trim does not remove a BOM, so this is not the submitted value.
      return note('Synthetic candidate\uFEFF');
    });
    await show(tester, api);
    await enter(tester, 'clinical-original-draft', 'Synthetic candidate');
    await tap(tester, 'clinical-sign-original');
    await tap(tester, 'clinical-confirm-original');
    expect(methods, ['POST', 'GET']);
    expect(find.text(originalSaved), findsNothing);
    expect(find.textContaining('Өөр анхны тэмдэглэл'), findsOneWidget);
    expect(button('clinical-sign-original'), findsNothing);
  });

  testWidgets(
    'matching audited original reports existence, not a new POST success',
    (tester) async {
      final methods = <String>[];
      final api = await authenticated((request) {
        methods.add(request.method);
        if (request.method == 'POST') {
          return json({'private': 'Synthetic details'}, 503);
        }
        return note('Line A\nLine B');
      });
      await show(tester, api);
      await enter(tester, 'clinical-original-draft', '  Line A\r\nLine B  ');
      await tap(tester, 'clinical-sign-original');
      await tap(tester, 'clinical-confirm-original');
      expect(methods, ['POST', 'GET']);
      expect(find.text(originalSaved), findsNothing);
      expect(find.textContaining('Ижил тэмдэглэл бүртгэлтэй'), findsOneWidget);
      expect(find.textContaining('Synthetic details'), findsNothing);
    },
  );

  testWidgets('unresolved original is locked against a second POST', (
    tester,
  ) async {
    var writes = 0;
    var reads = 0;
    final api = await authenticated((request) {
      if (request.method == 'POST') {
        writes++;
        return json({'private': 'Synthetic details'}, 503);
      }
      reads++;
      return json({'private': 'Synthetic audit failure'}, 503);
    });
    await show(tester, api);
    await enter(
      tester,
      'clinical-original-draft',
      'Synthetic uncertain original',
    );
    await tap(tester, 'clinical-sign-original');
    await tap(tester, 'clinical-confirm-original');
    expect((writes, reads), (1, 1));
    expect(
      tester.widget<FilledButton>(button('clinical-sign-original')).onPressed,
      isNull,
    );
    expect(find.textContaining('Дахин илгээхгүй'), findsOneWidget);
    await tap(tester, 'clinical-read-original');
    expect((writes, reads), (1, 2));
    expect(find.text(originalSaved), findsNothing);
  });

  testWidgets('explicit addendum retry reuses the UUID and never auto posts', (
    tester,
  ) async {
    var keys = 0;
    var posts = 0;
    var reads = 0;
    final api = await authenticated((request) {
      if (request.url.path == root) return note('Synthetic original');
      if (request.method == 'GET') {
        reads++;
        expect(request.url.path, '$root/addenda/$keyId');
        return json({'note': 'Private synthetic error'}, 503);
      }
      expect(request.url.path, '$root/addenda');
      expect(jsonDecode(request.body), {
        'requestId': keyId,
        'note': 'Synthetic addendum',
      });
      posts++;
      if (posts == 1) {
        throw http.ClientException('Synthetic missing acknowledgement');
      }
      return http.Response('', 204);
    });
    await show(
      tester,
      api,
      createRequestId: () {
        keys++;
        return keyId;
      },
    );
    await tap(tester, 'clinical-read-original');
    await enter(tester, 'clinical-addendum-draft', 'Synthetic addendum');
    await tap(tester, 'clinical-sign-addendum');
    expect((keys, posts, reads), (1, 1, 1));
    expect(find.text(addendumSaved), findsNothing);
    expect(find.textContaining('Private synthetic error'), findsNothing);
    await tap(tester, 'clinical-sign-addendum');
    expect((keys, posts, reads), (1, 2, 1));
    expect(find.textContaining('өмнө нь баталгаажсан'), findsOneWidget);
  });

  testWidgets(
    'different keyed addendum is shown as conflict, never written again',
    (tester) async {
      final requests = <String>[];
      final api = await authenticated((request) {
        requests.add('${request.method} ${request.url.path}');
        if (request.url.path == root) return note('Synthetic original');
        if (request.method == 'POST') {
          return json({'note': 'Private synthetic error'}, 409);
        }
        return addition('Synthetic stored addendum');
      });
      await show(tester, api);
      await tap(tester, 'clinical-read-original');
      await enter(
        tester,
        'clinical-addendum-draft',
        'Synthetic different attempt',
      );
      await tap(tester, 'clinical-sign-addendum');
      expect(requests, [
        'GET $root',
        'POST $root/addenda',
        'GET $root/addenda/$keyId',
      ]);
      expect(find.text(addendumSaved), findsNothing);
      expect(find.textContaining('өөр нэмэлт байна'), findsOneWidget);
      expect(button('clinical-addendum-content'), findsOneWidget);
    },
  );

  testWidgets(
    'selected encounter changes clear results/draft and ignore late data',
    (tester) async {
      final lateRead = Completer<http.Response>();
      final delivered = Completer<void>();
      var calls = 0;
      final api = await authenticated((request) {
        calls++;
        if (calls == 1) return note('Synthetic first result');
        delivered.complete();
        return lateRead.future;
      });
      await show(tester, api);
      await tap(tester, 'clinical-read-original');
      await enter(tester, 'clinical-addendum-draft', 'Synthetic must clear');
      await tester.tap(button('clinical-read-original'));
      await tester.pump();
      await delivered.future;
      await show(tester, api, id: 42);
      lateRead.complete(note('Synthetic stale response'));
      await tester.pumpAndSettle();
      expect(find.text('Synthetic first result'), findsNothing);
      expect(find.text('Synthetic must clear'), findsNothing);
      expect(find.text('Synthetic stale response'), findsNothing);
      final field = tester.widget<TextField>(button('clinical-original-draft'));
      expect(field.controller!.text, isEmpty);
    },
  );

  testWidgets('dispose ignores an in-flight clinical response', (tester) async {
    final lateRead = Completer<http.Response>();
    final sent = Completer<void>();
    final api = await authenticated((_) {
      sent.complete();
      return lateRead.future;
    });
    await show(tester, api);
    await tester.tap(button('clinical-read-original'));
    await tester.pump();
    await sent.future;
    await tester.pumpWidget(const SizedBox());
    lateRead.complete(note('Synthetic discarded after disposal'));
    await tester.pumpAndSettle();
    expect(find.text('Synthetic discarded after disposal'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets(
    'session change clears protected result and draft in same panel',
    (tester) async {
      final api = await authenticated(
        (_) => note('Synthetic sensitive result'),
      );
      await show(tester, api);
      await tap(tester, 'clinical-read-original');
      await enter(
        tester,
        'clinical-addendum-draft',
        'Synthetic unfinished draft',
      );
      api.logout();
      await tester.pump();
      expect(find.text('Synthetic sensitive result'), findsNothing);
      expect(find.text('Synthetic unfinished draft'), findsNothing);
      await api.login('synthetic@example.invalid', 'synthetic-password');
      await tester.pump();
      expect(
        tester
            .widget<TextField>(button('clinical-original-draft'))
            .controller!
            .text,
        isEmpty,
      );
      expect(button('clinical-original-content'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      expect(tester.takeException(), isNull);
    },
  );
}
