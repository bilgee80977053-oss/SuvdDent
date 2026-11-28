import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/io_client.dart';
import 'package:nexora_dental_mobile/live/dental_api.dart';
import 'package:nexora_dental_mobile/live/live_patient_flow.dart';
import 'package:nexora_dental_mobile/live/live_presentation.dart';
import 'package:nexora_dental_mobile/live/models.dart';

const _port = int.fromEnvironment('SUVDDENT_E2E_PORT');
const _day = String.fromEnvironment('SUVDDENT_E2E_DATE');
const _password = 'Synthetic-password-123!';
const _zone = 'Asia/Ulaanbaatar';

class _NativeBinding extends AutomatedTestWidgetsFlutterBinding {
  @override
  bool get overrideHttpClient => false;
}

// This is real HTTP (including the intentionally discarded 200), not a
// substitute response. The only permitted target is the test fixture socket.
class _LoopbackTransport extends http.BaseClient {
  final _io = IOClient(HttpClient()..findProxy = (_) => 'DIRECT');
  final moves = <({String payload, int status})>[];
  bool discardNextCommittedMove = false;

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (request.url.scheme != 'http' ||
        request.url.host != '127.0.0.1' ||
        request.url.port != _port ||
        request.url.userInfo.isNotEmpty) {
      throw StateError('Only the synthetic loopback fixture is permitted.');
    }
    request.followRedirects = false;
    // Run socket work outside fake time. Transfer errors as values because
    // asynchronous errors cannot cross different Dart error zones.
    final (response, failure) = await Zone.root.run(() async {
      try {
        final result = await _io.send(request);
        if (request.method == 'POST' &&
            request.url.path.endsWith('/reschedule')) {
          moves.add((
            payload: (request as http.Request).body,
            status: result.statusCode,
          ));
          if (discardNextCommittedMove && result.statusCode == 200) {
            discardNextCommittedMove = false;
            await result.stream.drain<void>();
            return (
              null,
              http.ClientException('Synthetic lost response after commit'),
            );
          }
        }
        return (result, null);
      } catch (error) {
        return (null, error);
      }
    });
    if (failure != null) throw failure;
    return response!;
  }

  @override
  void close() => _io.close();
}

DateTime _time(int hour, int minute) => DateTime.parse(
  '${_day}T${hour.toString().padLeft(2, '0')}:${minute.toString().padLeft(2, '0')}:00+08:00',
).toUtc();

Future<void> _visible(
  WidgetTester tester,
  Finder finder, {
  bool enabled = false,
}) async {
  bool ready() {
    final matches = finder.evaluate();
    if (matches.length != 1) return false;
    if (!enabled) return true;
    return switch (matches.single.widget) {
      ButtonStyleButton(:final onPressed) => onPressed != null,
      IconButton(:final onPressed) => onPressed != null,
      _ => true,
    };
  }

  // Real socket completions need a real event-loop yield, not fake-time settle.
  final until = DateTime.now().add(const Duration(seconds: 10));
  while (!ready() && DateTime.now().isBefore(until)) {
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 25)),
    );
    await tester.pump(const Duration(milliseconds: 30));
  }
  expect(finder, findsOneWidget);
  expect(ready(), isTrue, reason: 'Target did not become enabled: $finder');
}

Future<void> _tap(WidgetTester tester, Finder finder) async {
  await _visible(tester, finder, enabled: true);
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pump(const Duration(milliseconds: 350));
}

void main() {
  _NativeBinding();
  testWidgets(
    'real HTTP login, conflict, atomic move and exact retry through patient UI',
    (tester) async {
      expect(_port, inInclusiveRange(1, 65535));
      expect(RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(_day), isTrue);
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(1280, 1280);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPhysicalSize);

      final settings = ApiSettings(
        baseUrl: 'http://127.0.0.1:$_port',
        clinicId: 101,
      );
      DentalApi newApi(_LoopbackTransport wire) =>
          DentalApi(settings, client: wire);
      final wire = _LoopbackTransport();
      final originalOwner = newApi(_LoopbackTransport());
      final competitor = newApi(_LoopbackTransport());
      addTearDown(originalOwner.close);
      addTearDown(competitor.close);

      final original = (await tester.runAsync(() async {
        await originalOwner.login('user11@example.invalid', _password);
        await competitor.login('user27@example.invalid', _password);
        return originalOwner.book(
          31,
          51,
          _time(9, 15),
          'b6ddb8e6-279a-467a-be1b-31ab2baebd84',
        );
      }))!;

      await tester.pumpWidget(
        MaterialApp(
          home: LivePatientFlow(settings: settings, api: newApi(wire)),
        ),
      );
      await _tap(tester, find.widgetWithText(FilledButton, 'Цаг сонгох'));
      await tester.enterText(
        find.byType(TextFormField).at(0),
        'user11@example.invalid',
      );
      await tester.enterText(find.byType(TextFormField).at(1), _password);
      await _tap(tester, find.widgetWithText(FilledButton, 'Нэвтрэх'));
      await _tap(
        tester,
        find.widgetWithIcon(IconButton, Icons.event_note_outlined),
      );
      await _tap(
        tester,
        find.widgetWithText(OutlinedButton, 'Захиалгын цаг шилжүүлэх'),
      );
      await _tap(
        tester,
        find.widgetWithText(OutlinedButton, 'Өөр огноо сонгох'),
      );
      await _tap(tester, find.byType(DropdownButtonFormField<DateTime>));
      final date = DateTime.parse(_day);
      final dateLabel = '${date.year} · ${LiveLabels.date(_day)}';
      await _tap(tester, find.text(dateLabel).last);
      await _tap(tester, find.widgetWithText(FilledButton, 'Сонгох'));

      await _tap(tester, find.widgetWithText(OutlinedButton, '10:45'));
      await tester.runAsync(
        () => competitor.book(
          31,
          51,
          _time(10, 45),
          'cb73c16e-9768-4e39-a247-321e4769243c',
        ),
      );
      await _tap(
        tester,
        find.widgetWithText(FilledButton, 'Шинэ цаг баталгаажуулах'),
      );
      await _visible(
        tester,
        find.text(
          'Сонгосон цаг боломжгүй болсон. Шинэ боломжит цагаас сонгоно уу.',
        ),
      );
      expect(wire.moves.map((entry) => entry.status), [409]);
      expect(find.widgetWithText(OutlinedButton, '10:45'), findsNothing);
      final afterConflict = (await tester.runAsync(
        () => originalOwner.appointment(original.id),
      ))!;
      expect(afterConflict.id, original.id);
      expect(afterConflict.startUtc, original.startUtc);
      expect(afterConflict.endUtc, original.endUtc);
      expect(afterConflict.status, 'Confirmed');
      final notDuplicated = (await tester.runAsync(
        () => originalOwner.appointments(1),
      ))!;
      expect(notDuplicated.items.map((appointment) => appointment.id), [
        original.id,
      ]);

      await _tap(tester, find.widgetWithText(OutlinedButton, '11:45'));
      wire.discardNextCommittedMove = true;
      await _tap(
        tester,
        find.widgetWithText(FilledButton, 'Шинэ цаг баталгаажуулах'),
      );
      final retry = find.widgetWithText(
        FilledButton,
        'Ижил хүсэлтээр дахин шалгах',
      );
      await _visible(
        tester,
        find.text(
          'Цаг шилжүүлэлтийн үр дүн тодорхойгүй байна. Ижил хүсэлтээр дахин шалгана уу.',
        ),
      );
      expect(find.text('Шинэ цаг баталгаажлаа.'), findsNothing);
      expect(wire.moves.map((entry) => entry.status), [409, 200]);

      await _tap(tester, retry);
      await _visible(tester, find.text('Шинэ цаг баталгаажлаа.'));
      expect(wire.moves.map((entry) => entry.status), [409, 200, 200]);
      expect(wire.moves[1].payload, wire.moves[2].payload);
      expect(
        jsonDecode(wire.moves[0].payload)['requestId'],
        isNot(jsonDecode(wire.moves[1].payload)['requestId']),
      );
      final history = (await tester.runAsync(
        () => originalOwner.appointments(1),
      ))!;
      expect(history.items, hasLength(2));
      final replacement = history.items.singleWhere(
        (item) => item.id != original.id,
      );
      expect(replacement.startUtc, _time(11, 45));
      expect(replacement.clinicTimeZoneId, _zone);
      expect(replacement.status, 'Confirmed');
      expect(
        history.items.singleWhere((item) => item.id == original.id).status,
        'Cancelled',
      );
      // The success notice appears before the widget's own history refresh
      // completes. Wait for that distinct real request to finish before
      // disposal, rather than leaving its timeout in the fake-time zone.
      final progress = find.byType(LinearProgressIndicator);
      final deadline = DateTime.now().add(const Duration(seconds: 10));
      while (progress.evaluate().isNotEmpty &&
          DateTime.now().isBefore(deadline)) {
        await tester.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 25)),
        );
        await tester.pump(const Duration(milliseconds: 30));
      }
      expect(
        progress,
        findsNothing,
        reason: 'Patient history refresh is pending',
      );
      expect(
        find.text(
          'Шинэ цаг баталгаажсан. Захиалгын жагсаалтыг дахин шинэчилнэ үү.',
        ),
        findsNothing,
      );
      await tester.pumpWidget(const SizedBox());
      // C# harness requires this sentinel; does not include credentials or data.
      debugPrint('loopback widget confirmed the appointment lifecycle');
    },
    skip: _port == 0,
  );
}
