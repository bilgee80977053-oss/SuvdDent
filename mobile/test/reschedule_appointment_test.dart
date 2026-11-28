import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:nexora_dental_mobile/live/live_flow_controller.dart';
import 'package:nexora_dental_mobile/live/models.dart';

import 'cancellation_test.dart' show openHistory;
import 'live_api_test.dart' as fixture;

const firstKey = '10a0a1e8-8bb1-4c04-8d66-864a64a31d83';
const nextKey = 'f370df99-5564-49ab-b14c-c69dfb428481';
const replacementStart = '2030-01-02T01:30:00Z';
const replacementEnd = '2030-01-02T02:00:00Z';
const original = fixture.appointmentJson;
final replacement = {
  ...original,
  'id': 42,
  'startUtc': replacementStart,
  'endUtc': replacementEnd,
  'occupiedStartUtc': replacementStart,
  'occupiedEndUtc': replacementEnd,
};

http.Response options(http.Request r, {bool empty = false}) =>
    fixture.response({
      'date': r.url.queryParameters['date'],
      'timeZoneId': 'Asia/Ulaanbaatar',
      'slots': [
        if (!empty) {'startUtc': replacementStart, 'endUtc': replacementEnd},
      ],
    });

http.Response movedPage() => fixture.response({
  'page': 1,
  'pageSize': 20,
  'items': [
    replacement,
    {...original, 'status': 'Cancelled'},
  ],
});

Future<LiveFlowController> setup(
  FutureOr<http.Response> Function(http.Request) handler, {
  Duration timeout = const Duration(seconds: 1),
}) async {
  final keys = [firstKey, nextKey].iterator;
  final flow = LiveFlowController(
    fixture.makeApi(handler, timeout: timeout),
    requestIdFactory: () {
      expect(keys.moveNext(), true);
      return keys.current;
    },
  );
  addTearDown(flow.dispose);
  await flow.login('synthetic@example.invalid', 'synthetic-password');
  await flow.loadAppointments(1);
  expect(flow.beginReschedule(flow.history!.items.single), isTrue);
  await flow.loadRescheduleSlots(onDate: '2030-01-02');
  flow.selectRescheduleSlot(flow.reschedule!.availability!.slots.single);
  return flow;
}

Future<void> tap(WidgetTester tester, Finder target) async {
  await tester.ensureVisible(target);
  await tester.pumpAndSettle();
  await tester.ensureVisible(target);
  await tester.tap(target);
  await tester.pumpAndSettle();
}

void main() {
  test(
    'POST uses owned id, auth, selected UTC and stable request key',
    () async {
      int historyReads = 0;
      final flow = await setup((r) {
        if (r.url.path.endsWith('/reschedule-slots')) return options(r);
        if (r.url.path.endsWith('/reschedule')) {
          expect(r.method, 'POST');
          expect(r.url.path, '/api/appointments/41/reschedule');
          expect(r.headers['Authorization'], 'Bearer synthetic-test-token');
          expect(r.url.query, isEmpty);
          expect(jsonDecode(r.body), {
            'startUtc': DateTime.parse(replacementStart).toIso8601String(),
            'requestId': firstKey,
          });
          return fixture.response(replacement);
        }
        if (r.url.path == '/api/appointments/me' && ++historyReads == 2) {
          expect(r.url.queryParameters['page'], '1');
          return movedPage();
        }
        return fixture.standard(r);
      });

      expect(flow.history!.items.single.status, 'Confirmed');
      await flow.submitReschedule();
      expect(flow.reschedule!.replacement!.id, 42);
      expect(flow.history!.items.map((a) => a.status), [
        'Confirmed',
        'Cancelled',
      ]);
      expect(flow.notice, 'Шинэ цаг баталгаажлаа.');
      expect(historyReads, 2);
    },
  );

  test(
    'a conflict refreshes slots but never auto-submits or changes history',
    () async {
      var reads = 0;
      final postKeys = <String>[];
      final flow = await setup((r) {
        if (r.url.path.endsWith('/reschedule-slots')) {
          reads++;
          return options(r);
        }
        if (r.url.path.endsWith('/reschedule')) {
          postKeys.add(jsonDecode(r.body)['requestId'] as String);
          return postKeys.length == 1
              ? fixture.response({}, 409)
              : fixture.response(replacement);
        }
        return fixture.standard(r);
      });
      await flow.submitReschedule();
      expect(postKeys, [firstKey]);
      expect(reads, 2);
      expect(flow.reschedule!.selection, isNull);
      expect(flow.reschedule!.uncertain, isFalse);
      expect(flow.history!.items.single.status, 'Confirmed');
      expect(flow.reschedule!.replacement, isNull);
      flow.selectRescheduleSlot(flow.reschedule!.availability!.slots.single);
      await flow.submitReschedule();
      expect(postKeys, [firstKey, nextKey]);
    },
  );

  test(
    'lost response preserves source and retries exactly the same request',
    () async {
      final requests = <String>[];
      final never = Completer<http.Response>();
      final entered = Completer<void>();
      var reads = 0;
      final flow = await setup((r) {
        if (r.url.path.endsWith('/reschedule-slots')) {
          reads++;
          return options(r);
        }
        if (r.url.path.endsWith('/reschedule')) {
          requests.add(r.body);
          if (requests.length == 1) {
            entered.complete();
            return never.future;
          }
          return fixture.response(replacement);
        }
        return fixture.standard(r);
      }, timeout: const Duration(milliseconds: 50));

      final first = flow.submitReschedule();
      await entered.future;
      await flow.submitReschedule();
      expect(requests, hasLength(1));
      await first;
      expect(flow.reschedule!.uncertain, isTrue);
      expect(flow.canCancel(flow.reschedule!.source), isFalse);
      expect(flow.history!.items.single.status, 'Confirmed');
      expect(flow.reschedule!.replacement, isNull);
      await flow.loadRescheduleSlots(onDate: '2030-01-03');
      expect(reads, 1);
      expect(flow.reschedule!.date, '2030-01-02');
      await flow.submitReschedule();
      expect(requests, [requests.first, requests.first]);
      expect(flow.reschedule!.uncertain, isFalse);
      expect(flow.reschedule!.replacement!.id, 42);
      never.complete(fixture.response(replacement));
    },
  );

  test(
    'definitive conflict after an ambiguous retry permits a fresh choice',
    () async {
      final requests = <String>[];
      var reads = 0;
      final flow = await setup((r) {
        if (r.url.path.endsWith('/reschedule-slots')) {
          reads++;
          return options(r);
        }
        if (r.url.path.endsWith('/reschedule')) {
          requests.add(r.body);
          return switch (requests.length) {
            1 => fixture.response({}, 503),
            2 => fixture.response({}, 409),
            _ => fixture.response(replacement),
          };
        }
        return fixture.standard(r);
      });
      await flow.submitReschedule();
      expect(flow.reschedule!.uncertain, isTrue);
      await flow.submitReschedule();
      expect(requests, [requests.first, requests.first]);
      expect(flow.reschedule!.uncertain, isFalse);
      expect(flow.reschedule!.selection, isNull);
      expect(flow.canCancel(flow.reschedule!.source), isTrue);
      expect(reads, 2);
      flow.selectRescheduleSlot(flow.reschedule!.availability!.slots.single);
      await flow.submitReschedule();
      expect(jsonDecode(requests.last)['requestId'], nextKey);
      expect(flow.reschedule!.replacement!.id, 42);
    },
  );

  test(
    'cancelled replay reports inactive and preserves retry identity',
    () async {
      final requests = <String>[];
      final flow = await setup((r) {
        if (r.url.path.endsWith('/reschedule-slots')) return options(r);
        if (r.url.path.endsWith('/reschedule')) {
          requests.add(r.body);
          return requests.length == 1
              ? fixture.response({}, 503)
              : fixture.response({...replacement, 'status': 'Cancelled'});
        }
        return fixture.standard(r);
      });
      await flow.submitReschedule();
      expect(flow.reschedule!.uncertain, isTrue);
      await flow.submitReschedule();
      expect(requests, [requests.first, requests.first]);
      expect(flow.reschedule!.uncertain, isFalse);
      expect(flow.reschedule!.replacement!.status, 'Cancelled');
      expect(flow.notice, contains('идэвхгүй'));
    },
  );

  test(
    'a definitive rejection with unavailable slots requests history refresh',
    () async {
      var slotReads = 0;
      final flow = await setup((r) {
        if (r.url.path.endsWith('/reschedule-slots')) {
          return ++slotReads == 1 ? options(r) : fixture.response({}, 409);
        }
        if (r.url.path.endsWith('/reschedule')) {
          return fixture.response({}, 409);
        }
        return fixture.standard(r);
      });
      await flow.submitReschedule();
      expect(flow.reschedule!.uncertain, isFalse);
      expect(flow.reschedule!.availability, isNull);
      expect(flow.reschedule!.selection, isNull);
      expect(flow.error, contains('Захиалгын төлөвөө шинэчилж шалгана уу'));
      expect(flow.notice, isNull);
    },
  );

  test('malformed success does not report a completed move', () async {
    final flow = await setup((r) {
      if (r.url.path.endsWith('/reschedule-slots')) return options(r);
      if (r.url.path.endsWith('/reschedule')) {
        return fixture.response({...replacement, 'id': 41});
      }
      return fixture.standard(r);
    });
    await flow.submitReschedule();
    expect(flow.reschedule!.uncertain, isTrue);
    expect(flow.history!.items.single.id, 41);
    expect(flow.reschedule!.replacement, isNull);
    expect(flow.notice, isNull);
  });

  test('confirmed replacement survives a failed history refresh', () async {
    var reads = 0;
    final flow = await setup((r) {
      if (r.url.path.endsWith('/reschedule-slots')) return options(r);
      if (r.url.path.endsWith('/reschedule')) {
        return fixture.response(replacement);
      }
      if (r.url.path == '/api/appointments/me' && ++reads == 2) {
        return fixture.response({}, 503);
      }
      return fixture.standard(r);
    });
    await flow.submitReschedule();
    expect(flow.reschedule!.uncertain, isFalse);
    expect(flow.reschedule!.replacement!.id, 42);
    expect(flow.history, isNull);
    expect(flow.error, contains('Шинэ цаг баталгаажсан'));
  });

  test(
    'logout discards an in-flight replacement and clears patient data',
    () async {
      final pending = Completer<http.Response>();
      final entered = Completer<void>();
      final flow = await setup((r) {
        if (r.url.path.endsWith('/reschedule-slots')) return options(r);
        if (r.url.path.endsWith('/reschedule')) {
          entered.complete();
          return pending.future;
        }
        return fixture.standard(r);
      });
      final operation = flow.submitReschedule();
      await entered.future;
      flow.logout();
      pending.complete(fixture.response(replacement));
      await operation;
      expect(flow.reschedule, isNull);
      expect(flow.history, isNull);
      expect(flow.notice, isNull);
    },
  );

  test(
    'cancelled and started appointments cannot open the move flow',
    () async {
      final flow = LiveFlowController(fixture.makeApi(fixture.standard));
      addTearDown(flow.dispose);
      expect(flow.beginReschedule(Appointment.fromJson(original)), isFalse);
      await flow.login('synthetic@example.invalid', 'synthetic-password');
      for (final value in [
        {...original, 'status': 'Cancelled'},
        {...original, 'startUtc': '2000-01-01T00:00:00Z'},
      ]) {
        expect(flow.beginReschedule(Appointment.fromJson(value)), isFalse);
      }
      expect(flow.reschedule, isNull);
    },
  );

  test(
    'optional booking labels are trimmed and discard untrusted formatting',
    () {
      final named = Appointment.fromJson({
        ...original,
        'doctorName': '  Өлзий эмч  ',
        'serviceName': 'Шүдний үзлэг',
      });
      expect(named.doctorName, 'Өлзий эмч');
      expect(named.serviceName, 'Шүдний үзлэг');
      expect(Appointment.fromJson(original).doctorName, isNull);
      for (final malformed in <Object>[
        '',
        42,
        'Name\nSecond line',
        'Name\u202E',
        List.filled(201, 'X').join(),
      ]) {
        final a = Appointment.fromJson({
          ...original,
          'doctorName': malformed,
          'serviceName': malformed,
        });
        expect(a.doctorName, isNull);
        expect(a.serviceName, isNull);
      }
    },
  );

  testWidgets('LIVE history can select and confirm a real API slot', (
    tester,
  ) async {
    var moved = false;
    var posts = 0;
    await openHistory(tester, (r) {
      if (r.url.path.endsWith('/reschedule-slots')) {
        // Build a slot on the date requested by the automatically loaded
        // first screen and return the same instant when the POST is confirmed.
        final day = r.url.queryParameters['date']!;
        return fixture.response({
          'date': day,
          'timeZoneId': 'Asia/Ulaanbaatar',
          'slots': [
            {'startUtc': '${day}T01:30:00Z', 'endUtc': '${day}T02:00:00Z'},
          ],
        });
      }
      if (r.url.path.endsWith('/reschedule')) {
        posts++;
        moved = true;
        final start = DateTime.parse(jsonDecode(r.body)['startUtc'] as String);
        final end = start.add(const Duration(minutes: 30));
        return fixture.response({
          ...replacement,
          'startUtc': start.toIso8601String(),
          'endUtc': end.toIso8601String(),
          'occupiedStartUtc': start.toIso8601String(),
          'occupiedEndUtc': end.toIso8601String(),
        });
      }
      if (r.url.path == '/api/appointments/me') {
        if (moved) return movedPage();
        return fixture.response({
          'page': 1,
          'pageSize': 20,
          'items': [
            {
              ...original,
              'doctorName': 'Өлзий эмч',
              'serviceName': 'Шүдний үзлэг',
            },
          ],
        });
      }
      return fixture.standard(r);
    });
    expect(find.text('Өлзий эмч'), findsOneWidget);
    expect(find.text('Шүдний үзлэг'), findsOneWidget);
    await tap(
      tester,
      find.widgetWithText(OutlinedButton, 'Захиалгын цаг шилжүүлэх'),
    );
    expect(find.text('Анхны захиалгын мэдээлэл'), findsOneWidget);
    expect(find.text('Өлзий эмч'), findsOneWidget);
    expect(find.text('Шүдний үзлэг'), findsOneWidget);
    expect(posts, 0);
    final slots = find.widgetWithText(OutlinedButton, '09:30');
    await tester.scrollUntilVisible(slots, 180);
    await tap(tester, slots);
    final confirm = find.widgetWithText(
      FilledButton,
      'Шинэ цаг баталгаажуулах',
    );
    await tester.scrollUntilVisible(confirm, 180);
    await tap(tester, confirm);
    expect(posts, 1);
    expect(find.text('Шинэ цаг баталгаажлаа.'), findsOneWidget);
    expect(find.text('Өлзий эмч'), findsOneWidget);
    expect(find.text('Шүдний үзлэг'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
  });
}
