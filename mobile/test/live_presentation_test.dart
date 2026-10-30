import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental_mobile/live/dental_api.dart';
import 'package:nexora_dental_mobile/live/live_presentation.dart';
import 'package:nexora_dental_mobile/live/models.dart';

void main() {
  test('Exact fixture placeholders preserve real doctor fields', () {
    BookingDoctor doctor(String n, String s) => BookingDoctor.fromJson({
      'id': 1,
      'fullName': n,
      'specialty': s,
      'practiceStartYear': 2020,
    });
    final fixture = doctor(
      'SYNTHETIC SCHOOL Doctor',
      'SYNTHETIC SCHOOL DENTISTRY',
    );
    expect(LiveLabels.doctorName(fixture), 'Туршилтын эмч');
    expect(LiveLabels.specialty(fixture), 'Шүдний эмч');
    expect(LiveLabels.syntheticDoctor(fixture), isTrue);
    final real = doctor('Д. Болд', 'Нүүр амны мэс засал');
    expect(LiveLabels.doctorName(real), real.fullName);
    expect(LiveLabels.specialty(real), real.specialty);
    expect(LiveLabels.syntheticDoctor(real), isFalse);
  });
  test('IANA timezone rollover and invalid zone validation', () {
    final utc = DateTime.utc(2026, 10, 30, 17);
    expect(LiveLabels.time(utc, 'Asia/Ulaanbaatar'), '01:00');
    expect(LiveLabels.time(utc, 'Asia/Hovd'), '00:00');
    expect(
      LiveLabels.instant(utc, 'Asia/Ulaanbaatar'),
      contains('10-р сарын 31, Бямба'),
    );
    expect(
      () => LiveLabels.time(utc, 'Invalid/Zone'),
      throwsA(isA<ApiFailure>()),
    );
    final overnight = BookingSlot.fromJson({
      'startUtc': '2026-10-30T15:45:00Z',
      'endUtc': '2026-10-30T16:15:00Z',
    });
    expect(
      LiveLabels.interval(overnight, 'Asia/Ulaanbaatar'),
      '23:45–10-р сарын 31, Бямба 00:15',
    );
  });
  test('Booking window includes today and day 30, excludes adjacent dates', () {
    final now = DateTime.utc(2026, 12, 31, 17);
    final dates = BookingDateWindow.dates(now, 'Asia/Ulaanbaatar');
    expect(dates.length, 31);
    expect(dates.first, DateTime.utc(2027, 1, 1));
    expect(dates.last, DateTime.utc(2027, 1, 31));
    expect(
      BookingDateWindow.contains(dates.first, now, 'Asia/Ulaanbaatar'),
      isTrue,
    );
    expect(
      BookingDateWindow.contains(dates.last, now, 'Asia/Ulaanbaatar'),
      isTrue,
    );
    expect(
      BookingDateWindow.contains(
        DateTime.utc(2026, 12, 31),
        now,
        'Asia/Ulaanbaatar',
      ),
      isFalse,
    );
    expect(
      BookingDateWindow.contains(
        DateTime.utc(2027, 2, 1),
        now,
        'Asia/Ulaanbaatar',
      ),
      isFalse,
    );
    expect(
      BookingDateWindow.dates(
        DateTime.utc(2026, 12, 20),
        'Asia/Ulaanbaatar',
      ).last,
      DateTime.utc(2027, 1, 19),
    );
    expect(
      BookingDateWindow.dates(
        DateTime.utc(2028, 2, 20),
        'Asia/Ulaanbaatar',
      ).last,
      DateTime.utc(2028, 3, 21),
    );
    expect(
      BookingDateWindow.dates(
        DateTime.utc(2026, 10, 30, 16, 30),
        'Asia/Hovd',
      ).first,
      DateTime.utc(2026, 10, 30),
    );
    expect(
      BookingDateWindow.dates(
        DateTime.utc(2026, 10, 30, 16, 30),
        'Asia/Ulaanbaatar',
      ).first,
      DateTime.utc(2026, 10, 31),
    );
    expect(
      () => BookingDateWindow.dates(now, 'Invalid/Zone'),
      throwsA(isA<ApiFailure>()),
    );
  });
  for (final value in [
    '2026-12-20',
    '2027-01-19',
    '2026-12-19',
    '2027-01-20',
  ]) {
    testWidgets(
      'Picker enforces boundary for $value without silent substitution',
      (tester) async {
        DateTime? result;
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(
              body: Builder(
                builder: (context) => FilledButton(
                  onPressed: () async {
                    result = await showMongolianDatePicker(
                      context,
                      value,
                      timeZoneId: 'Asia/Ulaanbaatar',
                      now: () => DateTime.utc(2026, 12, 20),
                    );
                  },
                  child: const Text('Нээх'),
                ),
              ),
            ),
          ),
        );
        await tester.tap(find.text('Нээх'));
        await tester.pumpAndSettle();
        final picker = tester.widget<DropdownButtonFormField<DateTime>>(
          find.byType(DropdownButtonFormField<DateTime>),
        );
        final field = tester.state<FormFieldState<DateTime>>(
          find.byType(DropdownButtonFormField<DateTime>),
        );
        final valid = value == '2026-12-20' || value == '2027-01-19';
        expect(field.value != null, valid);
        final confirm = find.widgetWithText(FilledButton, 'Сонгох');
        expect(tester.widget<FilledButton>(confirm).onPressed != null, valid);
        if (valid) {
          await tester.tap(confirm);
          await tester.pumpAndSettle();
          expect(result, DateTime.parse('${value}T00:00:00Z'));
        } else {
          expect(find.textContaining('Өмнөх огноо'), findsOneWidget);
          expect(result, isNull);
          // Explicitly selecting a permitted date re-enables confirmation.
          picker.onChanged!(DateTime.utc(2027, 1, 19));
          await tester.pumpAndSettle();
          await tester.tap(confirm);
          await tester.pumpAndSettle();
          expect(result, DateTime.utc(2027, 1, 19));
        }
      },
    );
  }
  testWidgets('Picker rejects an expired date if clinic midnight passes', (
    tester,
  ) async {
    var now = DateTime.utc(2026, 12, 20, 15, 59);
    DateTime? result;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => FilledButton(
              onPressed: () async {
                result = await showMongolianDatePicker(
                  context,
                  '2026-12-20',
                  timeZoneId: 'Asia/Ulaanbaatar',
                  now: () => now,
                );
              },
              child: const Text('Нээх'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Нээх'));
    await tester.pumpAndSettle();
    now = DateTime.utc(2026, 12, 20, 16);
    await tester.tap(find.widgetWithText(FilledButton, 'Сонгох'));
    await tester.pumpAndSettle();
    expect(result, isNull);
    expect(
      find.textContaining('Захиалгын хугацаа өөрчлөгдсөн'),
      findsOneWidget,
    );
    expect(
      tester
          .widget<FilledButton>(find.widgetWithText(FilledButton, 'Сонгох'))
          .onPressed,
      isNull,
    );
    expect(tester.takeException(), isNull);
  });
  testWidgets('Unknown clinic timezone never offers guessed date choices', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => FilledButton(
              onPressed: () => showMongolianDatePicker(
                context,
                '2026-12-20',
                timeZoneId: null,
              ),
              child: const Text('Нээх'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Нээх'));
    await tester.pumpAndSettle();
    expect(find.byType(DropdownButtonFormField<DateTime>), findsNothing);
    expect(find.text('Сонгох'), findsNothing);
    expect(find.textContaining('эхлээд шинэчилнэ'), findsOneWidget);
  });
  for (final width in [320.0, 1000.0]) {
    testWidgets('All starts remain selectable in backend order at $width', (
      tester,
    ) async {
      tester.view.physicalSize = Size(width, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final page = SlotPage.fromJson({
        'date': '2026-10-31',
        'timeZoneId': 'Asia/Ulaanbaatar',
        'slots': [
          {
            'startUtc': '2026-10-31T01:00:00Z',
            'endUtc': '2026-10-31T01:30:00Z',
          },
          {
            'startUtc': '2026-10-31T01:15:00Z',
            'endUtc': '2026-10-31T01:45:00Z',
          },
          {
            'startUtc': '2026-10-31T02:00:00Z',
            'endUtc': '2026-10-31T02:45:00Z',
          },
        ],
      });
      BookingSlot? selected;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: StatefulBuilder(
              builder: (context, update) => LiveSlotChoices(
                page: page,
                selected: selected,
                enabled: true,
                onSelected: (s) => update(() => selected = s),
              ),
            ),
          ),
        ),
      );
      expect(find.byType(OutlinedButton), findsNWidgets(3));
      expect(tester.widgetList<Text>(find.byType(Text)).map((t) => t.data), [
        '09:00',
        '09:15',
        '10:00',
      ]);
      expect(
        tester.getSize(find.byType(OutlinedButton).first).height,
        greaterThanOrEqualTo(48),
      );
      await tester.tap(find.text('09:15'));
      await tester.pumpAndSettle();
      expect(identical(selected, page.slots[1]), isTrue);
      expect(find.text('Сонгосон'), findsOneWidget);
      expect(LiveLabels.duration(selected!), '30 минут');
      expect(LiveLabels.interval(selected!, page.timeZoneId), '09:15–09:45');
      expect(LiveLabels.duration(page.slots.last), '45 минут');
      expect(tester.takeException(), isNull);
    });
  }
  testWidgets(
    'Date picker exposes Mongolian labels and returns calendar date',
    (tester) async {
      DateTime? result;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: Builder(
              builder: (context) => FilledButton(
                onPressed: () async {
                  result = await showMongolianDatePicker(
                    context,
                    '2026-10-31',
                    timeZoneId: 'Asia/Ulaanbaatar',
                    now: () => DateTime.utc(2026, 10, 30, 17),
                  );
                },
                child: const Text('Нээх'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('Нээх'));
      await tester.pumpAndSettle();
      for (final label in ['Огноо', 'Болих', 'Сонгох']) {
        expect(find.text(label), findsOneWidget);
      }
      await tester.tap(find.text('Сонгох'));
      await tester.pumpAndSettle();
      expect(result, DateTime.utc(2026, 10, 31));
    },
  );
}
