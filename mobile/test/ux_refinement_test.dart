import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental_mobile/app/app_theme.dart';
import 'package:nexora_dental_mobile/app/nexora_app.dart';
import 'package:nexora_dental_mobile/live/live_patient_flow.dart';
import 'package:nexora_dental_mobile/live/live_presentation.dart';
import 'package:nexora_dental_mobile/live/models.dart';

import 'live_api_test.dart' as fixtures;

Future<void> press(WidgetTester tester, String label) async {
  final target = find.widgetWithText(FilledButton, label);
  await tester.ensureVisible(target);
  await tester.pumpAndSettle();
  await tester.tap(target);
  await tester.pumpAndSettle();
}

Future<void> login(WidgetTester tester) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: AppTheme.light,
      home: LivePatientFlow(
        settings: fixtures.settings,
        api: fixtures.makeApi(fixtures.standard),
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
}

String title(WidgetTester tester) =>
    tester.widget<Text>(find.byKey(const ValueKey('screen-title'))).data!;

void main() {
  for (final width in [320.0, 1280.0]) {
    testWidgets('DEMO complete walkthrough fits at $width', (tester) async {
      tester.view.physicalSize = Size(width, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(const NexoraDentalApp());
      Future<void> tapKey(String key) async {
        final target = find.byKey(ValueKey(key));
        await tester.ensureVisible(target);
        await tester.tap(target);
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
      }

      await tapKey('clinic-101');
      await tapKey('continue-button');
      await tapKey('continue-button');
      await tapKey('continue-button');
      await tapKey('continue-button');
      await tapKey('service-51');
      await tapKey('continue-button');
      await tapKey('doctor-31');
      await tapKey('continue-button');
      await tapKey('slot-1');
      await tapKey('continue-button');
      await tapKey('continue-button');
      expect(
        find.text('Цаг захиалга үүсгээгүй. Имэйл илгээгээгүй.'),
        findsOneWidget,
      );
    });
  }

  for (final width in [320.0, 390.0, 1280.0]) {
    testWidgets(
      'History is separate and back preserves booking stage at $width',
      (tester) async {
        tester.view.physicalSize = Size(width, 800);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        await login(tester);
        expect(title(tester), 'Нэвтрэлт амжилттай');
        await tester.tap(find.byTooltip('Миний захиалгууд'));
        await tester.pumpAndSettle();
        expect(title(tester), 'Миний захиалгууд');
        expect(find.textContaining('АЛХАМ'), findsNothing);
        expect(find.text('Имэйл баталгаажуулалт'), findsNothing);
        for (final label in [
          'Захиалгын дугаар: №41',
          'Захиалгын төлөв',
          'Эмч',
          'Үйлчилгээ',
          'Огноо',
          'Эхлэх цаг',
          'Дуусах цаг',
        ]) {
          expect(find.text(label), findsOneWidget);
        }
        expect(
          find.text('Эмчийн нэр ирээгүй. Эмнэлгээс лавлана уу.'),
          findsOneWidget,
        );
        expect(
          find.text('Үйлчилгээний нэр ирээгүй. Эмнэлгээс лавлана уу.'),
          findsOneWidget,
        );
        expect(find.text('09:00'), findsOneWidget);
        expect(find.text('09:30'), findsOneWidget);
        expect(find.textContaining('2030 он'), findsOneWidget);
        expect(find.textContaining('T01:00'), findsNothing);
        expect(tester.takeException(), isNull);
        await tester.tap(find.byTooltip('Буцах'));
        await tester.pumpAndSettle();
        expect(title(tester), 'Нэвтрэлт амжилттай');
        await press(tester, 'Миний профайлыг авах');
        await press(tester, 'Үйлчилгээ авах');
        await tester.tap(find.text('Тест үйлчилгээ'));
        await tester.pumpAndSettle();
        await tester.tap(find.byTooltip('Миний захиалгууд'));
        await tester.pumpAndSettle();
        expect(title(tester), 'Миний захиалгууд');
        expect(find.text('Үйлчилгээ сонгох'), findsNothing);
        expect(find.text('Тест үйлчилгээ'), findsOneWidget);
        await tester.binding.handlePopRoute();
        await tester.pumpAndSettle();
        expect(title(tester), 'Үйлчилгээ сонгох');
        expect(find.text('Сонгосон үйлчилгээ: Тест үйлчилгээ'), findsOneWidget);
        await tester.tap(find.byTooltip('Миний захиалгууд'));
        await tester.pumpAndSettle();
        await press(tester, 'Захиалгын алхам руу буцах');
        expect(title(tester), 'Үйлчилгээ сонгох');
        expect(tester.takeException(), isNull);
      },
    );
  }

  testWidgets(
    'History pagination keeps server page size and requests next/previous pages',
    (tester) async {
      final pages = <int>[];
      final api = fixtures.makeApi((request) {
        if (request.url.path == '/api/appointments/me') {
          final page = int.parse(request.url.queryParameters['page']!);
          pages.add(page);
          return fixtures.response({
            'page': page,
            'pageSize': 1,
            'items': [fixtures.appointmentJson],
          });
        }
        return fixtures.standard(request);
      });
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: LivePatientFlow(settings: fixtures.settings, api: api),
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
      expect(
        tester
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'Өмнөх хуудас'),
            )
            .onPressed,
        isNull,
      );
      await press(tester, 'Дараагийн хуудас');
      expect(find.text('Хуудас 2'), findsOneWidget);
      await press(tester, 'Өмнөх хуудас');
      expect(pages, [1, 2, 1]);
      expect(tester.takeException(), isNull);
    },
  );

  for (final width in [320.0, 1280.0]) {
    testWidgets('Large text cards and time summary fit at $width', (
      tester,
    ) async {
      tester.view.physicalSize = Size(width, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: MediaQuery(
            data: MediaQueryData(
              size: Size(width, 800),
              textScaler: const TextScaler.linear(1.6),
            ),
            child: Scaffold(
              body: SingleChildScrollView(
                child: Column(
                  children: [
                    AppointmentDetailsCard(
                      appointment: Appointment.fromJson(
                        fixtures.appointmentJson,
                      ),
                      doctorName: 'Туршилтын урт нэртэй эмч',
                      serviceName: 'Туршилтын урт нэртэй үйлчилгээ',
                    ),
                    BookingTimeSummary(
                      slot: BookingSlot.fromJson(fixtures.slotJson),
                      date: '2030-01-02',
                      timeZoneId: 'Asia/Ulaanbaatar',
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      );
      expect(find.text('Туршилтын урт нэртэй эмч'), findsOneWidget);
      expect(find.text('30 минут'), findsOneWidget);
      expect(find.text('Сонгосон огноо'), findsOneWidget);
      expect(find.text('09:00'), findsNWidgets(2));
      expect(find.text('09:30'), findsNWidgets(2));
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets(
    'Unknown timezone is explicitly UTC and unknown status never leaks raw values',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(
            body: SingleChildScrollView(
              child: AppointmentDetailsCard(
                appointment: Appointment.fromJson({
                  ...fixtures.appointmentJson,
                  'clinicTimeZoneId': 'Invalid/Zone',
                  'status': 'InternalValue',
                }),
              ),
            ),
          ),
        ),
      );
      expect(find.textContaining('UTC цагаар'), findsOneWidget);
      expect(find.text('01:00'), findsOneWidget);
      expect(find.text('InternalValue'), findsNothing);
      expect(
        find.text('Захиалгын төлөвийг эмнэлгээс лавлана уу'),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
    },
  );
}
