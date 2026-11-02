import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:nexora_dental_mobile/live/live_flow_controller.dart';
import 'package:nexora_dental_mobile/live/live_patient_flow.dart';
import 'package:nexora_dental_mobile/live/models.dart';

import 'live_api_test.dart' as fixture;

Map<String, dynamic> booking({String status = 'Confirmed'}) => {
  ...fixture.appointmentJson,
  'status': status,
};
http.Response page({String status = 'Confirmed', int number = 1}) =>
    fixture.response({
      'page': number,
      'pageSize': 20,
      'items': [booking(status: status)],
    });
Appointment value({String status = 'Confirmed'}) =>
    Appointment.fromJson(booking(status: status));

Future<LiveFlowController> controller(
  FutureOr<http.Response> Function(http.Request) handler, {
  Duration timeout = const Duration(seconds: 1),
}) async {
  final flow = LiveFlowController(fixture.makeApi(handler, timeout: timeout));
  await flow.login('synthetic@example.invalid', 'synthetic-password');
  await flow.loadAppointments(2);
  return flow;
}

Future<void> press(WidgetTester tester, String label) async {
  final target = find.widgetWithText(FilledButton, label);
  await tester.ensureVisible(target);
  await tester.pumpAndSettle();
  await tester.ensureVisible(target);
  await tester.tap(target);
  await tester.pumpAndSettle();
}

Future<void> openHistory(
  WidgetTester tester,
  FutureOr<http.Response> Function(http.Request) handler,
) async {
  await tester.pumpWidget(
    MaterialApp(
      home: LivePatientFlow(
        settings: fixture.settings,
        api: fixture.makeApi(handler),
      ),
    ),
  );
  await press(tester, 'Энэ эмнэлгээр үргэлжлүүлэх');
  await tester.enterText(
    find.byType(TextFormField).at(0),
    'synthetic@example.invalid',
  );
  await tester.enterText(
    find.byType(TextFormField).at(1),
    'synthetic-password',
  );
  await press(tester, 'Нэвтрэх');
  await tester.tap(find.byTooltip('Миний захиалгууд'));
  await tester.pumpAndSettle();
}

void main() {
  test('busy guard prevents duplicate taps and explicit uncertain retry uses same id', () async {
    var posts = 0;
    final pending = Completer<http.Response>();
    final entered = Completer<void>();
    final flow = await controller((r) {
      if (r.url.path.endsWith('/cancel')) {
        posts++;
        if (posts == 1) {
          entered.complete();
          return pending.future;
        }
        expect(r.url.path, '/api/appointments/41/cancel');
        return fixture.response(booking(status: 'Cancelled'));
      }
      if (r.url.path == '/api/appointments/41') {
        return fixture.response(booking());
      }
      return fixture.standard(r);
    });
    addTearDown(flow.dispose);
    final first = flow.cancelAppointment(value());
    await entered.future;
    await flow.cancelAppointment(value());
    expect(posts, 1);
    pending.completeError(http.ClientException('synthetic'));
    await first;
    expect(flow.uncertainCancellations, contains(41));
    await flow.cancelAppointment(value());
    expect(posts, 2);
    expect(flow.uncertainCancellations, isEmpty);
    expect(flow.history!.items.single.status, 'Cancelled');
  });

  testWidgets(
    'uncertain cancellation offers GET reconciliation without automatic repost',
    (tester) async {
      var posts = 0, gets = 0;
      var accepted = false;
      await openHistory(tester, (r) {
        if (r.url.path.endsWith('/cancel')) {
          posts++;
          return fixture.response({}, 500);
        }
        if (r.url.path == '/api/appointments/41') {
          gets++;
          return fixture.response(
            booking(status: accepted ? 'Cancelled' : 'Confirmed'),
          );
        }
        return fixture.standard(r);
      });
      await press(tester, 'Захиалга цуцлах');
      await press(tester, 'Тийм, цуцлах');
      expect(posts, 1);
      expect(gets, 1);
      expect(find.text('Цуцлахыг дахин оролдох'), findsOneWidget);
      accepted = true;
      await press(tester, 'Төлөв шалгах');
      expect(posts, 1);
      expect(gets, 2);
      expect(find.text('Цуцалсан захиалга'), findsOneWidget);
      expect(find.text('Төлөв шалгах'), findsNothing);
      await tester.pumpWidget(const SizedBox());
    },
  );

  test('cancellation posts only the appointment id and refreshes current history page', () async {
    var posts = 0, reads = 0;
    var cancelled = false;
    final flow = await controller((r) {
      if (r.url.path.endsWith('/cancel')) {
        posts++;
        expect(r.method, 'POST');
        expect(r.url.path, '/api/appointments/41/cancel');
        expect(r.url.queryParameters, isEmpty);
        expect(r.body, isEmpty);
        expect(r.headers['Authorization'], 'Bearer synthetic-test-token');
        cancelled = true;
        return fixture.response(booking(status: 'Cancelled'));
      }
      if (r.url.path == '/api/appointments/me') {
        reads++;
        expect(r.url.queryParameters['page'], '2');
        return page(status: cancelled ? 'Cancelled' : 'Confirmed', number: 2);
      }
      return fixture.standard(r);
    });
    addTearDown(flow.dispose);
    final operation = flow.cancelAppointment(value());
    expect(flow.busy, isTrue);
    await operation;
    expect(flow.busy, isFalse);
    expect(posts, 1);
    expect(reads, 2);
    expect(flow.history!.items.single.status, 'Cancelled');
    expect(flow.notice, contains('амжилттай цуцлагдлаа'));
    expect(flow.error, isNull);
  });

  test(
    'past, started and cancelled appointments never submit cancellation',
    () async {
      var posts = 0;
      final flow = await controller((r) {
        if (r.url.path.endsWith('/cancel')) posts++;
        return fixture.standard(r);
      });
      addTearDown(flow.dispose);
      for (final row in [
        booking(status: 'Cancelled'),
        {
          ...booking(),
          'startUtc': DateTime.now()
              .toUtc()
              .subtract(const Duration(days: 1))
              .toIso8601String(),
        },
        {...booking(), 'startUtc': DateTime.now().toUtc().toIso8601String()},
      ]) {
        final appointment = Appointment.fromJson(row);
        expect(flow.canCancel(appointment), isFalse);
        await flow.cancelAppointment(appointment);
      }
      expect(posts, 0);
    },
  );

  for (final code in [400, 404, 409]) {
    test(
      'HTTP $code reports failure without claiming cancellation or retrying',
      () async {
        var posts = 0;
        final flow = await controller((r) {
          if (r.url.path.endsWith('/cancel')) {
            posts++;
            return fixture.response({'message': 'Цуцлах боломжгүй.'}, code);
          }
          return fixture.standard(r);
        });
        addTearDown(flow.dispose);
        await flow.cancelAppointment(value());
        expect(posts, 1);
        expect(flow.error, isNotNull);
        expect(flow.history!.items.single.status, 'Confirmed');
        expect(flow.uncertainCancellations, isEmpty);
        expect(flow.notice, isNull);
      },
    );
  }

  test(
    'timeout reconciles accepted cancellation using GET without another POST',
    () async {
      var posts = 0, gets = 0;
      final pending = Completer<http.Response>();
      final flow = await controller((r) {
        if (r.url.path.endsWith('/cancel')) {
          posts++;
          return pending.future;
        }
        if (r.url.path == '/api/appointments/41') {
          gets++;
          expect(r.method, 'GET');
          return fixture.response(booking(status: 'Cancelled'));
        }
        return fixture.standard(r);
      }, timeout: const Duration(milliseconds: 20));
      addTearDown(flow.dispose);
      await flow.cancelAppointment(value());
      pending.complete(fixture.response(booking(status: 'Cancelled')));
      expect(posts, 1);
      expect(gets, 1);
      expect(flow.uncertainCancellations, isEmpty);
      expect(flow.history!.items.single.status, 'Cancelled');
      expect(flow.notice, contains('амжилттай'));
    },
  );

  test('confirmed reconciliation stays uncertain until later read; no automatic repost', () async {
    var posts = 0, gets = 0;
    var accepted = false;
    final pending = Completer<http.Response>();
    final flow = await controller((r) {
      if (r.url.path.endsWith('/cancel')) {
        posts++;
        return pending.future;
      }
      if (r.url.path == '/api/appointments/41') {
        gets++;
        return fixture.response(
          booking(status: accepted ? 'Cancelled' : 'Confirmed'),
        );
      }
      return fixture.standard(r);
    }, timeout: const Duration(milliseconds: 20));
    addTearDown(flow.dispose);
    await flow.cancelAppointment(value());
    expect(flow.uncertainCancellations, contains(41));
    expect(flow.notice, contains('баталгаажаагүй'));
    accepted = true;
    pending.complete(fixture.response(booking(status: 'Cancelled')));
    await flow.reconcileCancellation(41);
    expect(posts, 1);
    expect(gets, 2);
    expect(flow.history!.items.single.status, 'Cancelled');
    expect(flow.uncertainCancellations, isEmpty);
  });

  test(
    'lost connection and failed reconciliation retain check-state action',
    () async {
      var posts = 0;
      final flow = await controller((r) {
        if (r.url.path.endsWith('/cancel')) {
          posts++;
          throw http.ClientException('synthetic');
        }
        if (r.url.path == '/api/appointments/41') {
          throw http.ClientException('synthetic');
        }
        return fixture.standard(r);
      });
      addTearDown(flow.dispose);
      await flow.cancelAppointment(value());
      expect(posts, 1);
      expect(flow.uncertainCancellations, contains(41));
      expect(flow.error, contains('үр дүн тодорхойгүй'));
      flow.logout();
      expect(flow.uncertainCancellations, isEmpty);
    },
  );

  test('confirmed cancellation survives failed history refresh', () async {
    var refresh = false;
    final flow = await controller((r) {
      if (r.url.path.endsWith('/cancel')) {
        refresh = true;
        return fixture.response(booking(status: 'Cancelled'));
      }
      if (refresh && r.url.path == '/api/appointments/me') {
        return fixture.response({}, 500);
      }
      return fixture.standard(r);
    });
    addTearDown(flow.dispose);
    await flow.cancelAppointment(value());
    expect(flow.history!.items.single.status, 'Cancelled');
    expect(flow.notice, contains('амжилттай'));
    expect(flow.error, contains('Жагсаалтыг шинэчилж чадсангүй'));
    expect(flow.uncertainCancellations, isEmpty);
  });

  test('logout discards an old cancellation response', () async {
    final pending = Completer<http.Response>();
    final flow = await controller((r) {
      if (r.url.path.endsWith('/cancel')) return pending.future;
      return fixture.standard(r);
    });
    addTearDown(flow.dispose);
    final operation = flow.cancelAppointment(value());
    flow.logout();
    pending.complete(fixture.response(booking(status: 'Cancelled')));
    await operation;
    expect(flow.history, isNull);
    expect(flow.notice, isNull);
    expect(flow.uncertainCancellations, isEmpty);
  });

  testWidgets(
    'LIVE history dialog can decline, then confirm, show loading and refresh',
    (tester) async {
      var posts = 0, reads = 0;
      var cancelled = false;
      final pending = Completer<http.Response>();
      await openHistory(tester, (r) {
        if (r.url.path.endsWith('/cancel')) {
          posts++;
          return pending.future;
        }
        if (r.url.path == '/api/appointments/me') {
          reads++;
          return page(status: cancelled ? 'Cancelled' : 'Confirmed');
        }
        return fixture.standard(r);
      });
      await press(tester, 'Захиалга цуцлах');
      expect(find.text('Захиалга цуцлах уу?'), findsOneWidget);
      expect(posts, 0);
      await tester.tap(find.widgetWithText(TextButton, 'Буцах'));
      await tester.pumpAndSettle();
      expect(posts, 0);
      await press(tester, 'Захиалга цуцлах');
      await tester.tap(find.widgetWithText(FilledButton, 'Тийм, цуцлах'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));
      expect(posts, 1);
      expect(find.text('Мэдээллийг ачаалж байна…'), findsOneWidget);
      final cancelButton = tester.widget<FilledButton>(
        find.widgetWithText(FilledButton, 'Захиалга цуцлах'),
      );
      expect(cancelButton.onPressed, isNull);
      cancelled = true;
      pending.complete(fixture.response(booking(status: 'Cancelled')));
      await tester.pumpAndSettle();
      expect(reads, 2);
      expect(find.text('Цуцалсан захиалга'), findsOneWidget);
      expect(find.text('Захиалга цуцлах'), findsNothing);
      expect(find.text('Таны захиалга амжилттай цуцлагдлаа.'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets(
    'LIVE history hides cancellation for past and cancelled appointments',
    (tester) async {
      await openHistory(tester, (r) {
        if (r.url.path == '/api/appointments/me') {
          return fixture.response({
            'page': 1,
            'pageSize': 20,
            'items': [
              {...booking(), 'startUtc': '2020-01-01T01:00:00Z'},
              {...booking(status: 'Cancelled'), 'id': 42},
            ],
          });
        }
        return fixture.standard(r);
      });
      expect(find.text('Захиалга цуцлах'), findsNothing);
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets('LIVE history displays cancellation errors without success', (
    tester,
  ) async {
    await openHistory(tester, (r) {
      if (r.url.path.endsWith('/cancel')) {
        return fixture.response({'message': 'Цуцлах боломжгүй.'}, 409);
      }
      return fixture.standard(r);
    });
    await press(tester, 'Захиалга цуцлах');
    await press(tester, 'Тийм, цуцлах');
    expect(
      find.textContaining('Захиалгыг цуцлах боломжгүй болсон'),
      findsOneWidget,
    );
    expect(find.text('Таны захиалга амжилттай цуцлагдлаа.'), findsNothing);
    await tester.pumpWidget(const SizedBox());
  });
}
