import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental_mobile/live/live_patient_flow.dart';
import 'package:nexora_dental_mobile/live/patient_invoice_review.dart';

import 'live_api_test.dart' as fixtures;

const reviewChoice = 'Туршилтын задаргаа харах';
const nonpayable = 'Төлөх боломжгүй ноорог';
const invalidReview = 'Энэ захиалгын задаргааг харуулах боломжгүй.';

Map<String, dynamic> line(
  int sequence,
  String charge,
  String discount,
  String tax,
  String total,
) => {
  'sequence': sequence,
  'labelKey': 'invoice.review.charge',
  'chargeUnits': charge,
  'discountUnits': discount,
  'taxUnits': tax,
  'reviewTotalUnits': total,
};

Map<String, dynamic> review({
  String subtotal = '375',
  String discount = '65',
  String tax = '11',
  String total = '321',
  int scale = 2,
  List<Map<String, dynamic>>? lines,
}) => {
  'status': 'draftReview',
  'payable': false,
  'currency': 'XTS',
  'scale': scale,
  'subtotalUnits': subtotal,
  'discountUnits': discount,
  'taxUnits': tax,
  'reviewTotalUnits': total,
  'lines':
      lines ??
      [
        line(1, '200', '15', '8', '193'),
        line(2, '100', '50', '3', '53'),
        line(3, '75', '0', '0', '75'),
      ],
};

InvoiceReviewRead oneAmount(int booking, String value) => InvoiceReviewRead(
  booking,
  InvoiceReviewBreakdown.fromJson(
    review(
      subtotal: value,
      discount: '0',
      tax: '0',
      total: value,
      lines: [line(1, value, '0', '0', value)],
    ),
  ),
);

Future<void> tap(WidgetTester tester, Finder finder) async {
  await tester.ensureVisible(finder);
  await tester.pumpAndSettle();
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> openHistory(
  WidgetTester tester, {
  InvoiceReviewReader? reader,
}) async {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = const Size(1000, 1700);
  addTearDown(tester.view.resetDevicePixelRatio);
  addTearDown(tester.view.resetPhysicalSize);
  final api = fixtures.makeApi((request) {
    if (request.url.path == '/api/appointments/me') {
      return fixtures.response({
        'page': 1,
        'pageSize': 20,
        'items': [
          {...fixtures.appointmentJson, 'id': 41},
          {...fixtures.appointmentJson, 'id': 42},
        ],
      });
    }
    expect(request.url.path, isNot(contains('invoice')));
    expect(request.url.path, isNot(contains('payment')));
    return fixtures.standard(request);
  });
  await tester.pumpWidget(
    MaterialApp(
      home: LivePatientFlow(
        settings: fixtures.settings,
        api: api,
        invoiceReviewReader: reader,
      ),
    ),
  );
  await tap(tester, find.widgetWithText(FilledButton, 'Цаг сонгох'));
  await tester.enterText(
    find.byType(TextFormField).at(0),
    'synthetic@example.invalid',
  );
  await tester.enterText(
    find.byType(TextFormField).at(1),
    'synthetic-password',
  );
  await tap(tester, find.widgetWithText(FilledButton, 'Нэвтрэх'));
  await tap(tester, find.byTooltip('Миний захиалгууд'));
}

Finder get choices => find.widgetWithText(OutlinedButton, reviewChoice);

void main() {
  test('models exact backend line order and fixed decimal scale', () {
    final result = InvoiceReviewBreakdown.fromJson(review());
    expect(result.payable, isFalse);
    expect(result.lines.map((row) => row.sequence), [1, 2, 3]);
    expect(result.lines.map((row) => result.format(row.total)), [
      '1.93 XTS',
      '0.53 XTS',
      '0.75 XTS',
    ]);
    expect(result.format(result.subtotal), '3.75 XTS');
    expect(result.format(result.discount), '0.65 XTS');
    expect(result.format(result.tax), '0.11 XTS');
    expect(result.format(result.total), '3.21 XTS');
    expect(result.lines.clear, throwsUnsupportedError);
  });

  test('preserves signed-64-bit units without floating point or rounding', () {
    final amount = oneAmount(41, '9223372036854775807').breakdown;
    expect(amount.format(amount.total), '92233720368547758.07 XTS');
    final hundredth = InvoiceReviewBreakdown.fromJson(
      review(
        subtotal: '1',
        discount: '0',
        tax: '0',
        total: '1',
        scale: 28,
        lines: [line(1, '1', '0', '0', '1')],
      ),
    );
    expect(
      hundredth.format(hundredth.total),
      '0.0000000000000000000000000001 XTS',
    );
    final zero = InvoiceReviewBreakdown.fromJson(
      review(
        subtotal: '150',
        discount: '150',
        tax: '0',
        total: '0',
        scale: 0,
        lines: [line(1, '150', '150', '0', '0')],
      ),
    );
    expect(zero.format(zero.total), '0 XTS');
    expect(zero.payable, isFalse);
  });

  test(
    'rejects payable, PII, freeform labels and malformed or inconsistent money',
    () {
      final fixtures = <Map<String, dynamic>>[
        {...review(), 'payable': true},
        {...review(), 'status': 'issued'},
        {...review(), 'currency': 'mnt'},
        {...review(), 'scale': 29},
        {...review(), 'clinicId': 10},
        {...review(), 'subtotalUnits': 375},
        {...review(), 'subtotalUnits': '9223372036854775808'},
        {...review(), 'subtotalUnits': '-375'},
        {...review(), 'subtotalUnits': '0375'},
        {...review(), 'subtotalUnits': '375\\n'},
        review(total: '320'),
        review(lines: []),
        review(lines: [line(2, '375', '65', '11', '321')]),
        review(lines: [line(1, '375', '65', '11', '320')]),
        review(lines: [line(1, '375', '376', '322', '321')]),
        review(
          lines: [
            {...line(1, '375', '65', '11', '321'), 'labelKey': 'clinical data'},
          ],
        ),
        review(
          lines: [
            {
              ...line(1, '375', '65', '11', '321'),
              'note': 'synthetic private data',
            },
          ],
        ),
        review(
          lines: [for (var i = 1; i <= 101; i++) line(i, '0', '0', '0', '0')],
        ),
      ];
      for (final malformed in fixtures) {
        expect(
          () => InvoiceReviewBreakdown.fromJson(malformed),
          throwsFormatException,
        );
      }
    },
  );

  testWidgets('normal My Bookings has no review entry or preview fixture', (
    tester,
  ) async {
    await openHistory(tester);
    expect(choices, findsNothing);
    expect(find.byType(PatientInvoiceReviewPanel), findsNothing);
    expect(find.text('Төлбөр / үнийн мэдээлэл'), findsNWidgets(2));
  });

  testWidgets(
    'review-only My Bookings shows exact, ordered, nonpayable amounts',
    (tester) async {
      final reads = <int>[];
      await openHistory(
        tester,
        reader: (id) async {
          reads.add(id);
          return InvoiceReviewRead(
            id,
            InvoiceReviewBreakdown.fromJson(review()),
          );
        },
      );
      await tap(tester, choices.last);
      expect(reads, [42]);
      final panel = find.byType(PatientInvoiceReviewPanel);
      expect(panel, findsOneWidget);
      expect(find.text(nonpayable), findsOneWidget);
      expect(find.text('Төлбөр / үнийн мэдээлэл'), findsNothing);
      final ordered = tester
          .widgetList<Text>(
            find.descendant(of: panel, matching: find.byType(Text)),
          )
          .map((text) => text.data)
          .where((data) => data != null)
          .toList();
      expect(ordered.where((text) => text!.startsWith('Мөрийн нийлбэр:')), [
        'Мөрийн нийлбэр: 1.93 XTS',
        'Мөрийн нийлбэр: 0.53 XTS',
        'Мөрийн нийлбэр: 0.75 XTS',
      ]);
      expect(find.text('Нийт хөнгөлөлт: 0.65 XTS'), findsOneWidget);
      expect(find.text('Нийт татвар: 0.11 XTS'), findsOneWidget);
      expect(find.text('Нооргийн нийлбэр: 3.21 XTS'), findsOneWidget);
      expect(
        find.descendant(
          of: panel,
          matching: find.bySubtype<ButtonStyleButton>(),
        ),
        findsNothing,
      );
    },
  );

  testWidgets('moving to another booking ignores the late prior response', (
    tester,
  ) async {
    final reads = <int>[];
    final first = Completer<InvoiceReviewRead?>();
    final second = Completer<InvoiceReviewRead?>();
    await openHistory(
      tester,
      reader: (id) {
        reads.add(id);
        return id == 41 ? first.future : second.future;
      },
    );
    await tap(tester, choices.first);
    await tap(tester, choices.last);
    expect(reads, [41, 42]);
    second.complete(oneAmount(42, '500'));
    await tester.pumpAndSettle();
    expect(find.text('Нооргийн нийлбэр: 5.00 XTS'), findsOneWidget);
    first.complete(oneAmount(41, '100'));
    await tester.pumpAndSettle();
    expect(find.text('Нооргийн нийлбэр: 5.00 XTS'), findsOneWidget);
    expect(find.text('Нооргийн нийлбэр: 1.00 XTS'), findsNothing);
  });

  testWidgets('logout clears the preview and ignores a pending response', (
    tester,
  ) async {
    final pending = Completer<InvoiceReviewRead?>();
    await openHistory(tester, reader: (_) => pending.future);
    await tap(tester, choices.first);
    expect(find.text(nonpayable), findsOneWidget);
    await tap(tester, find.byTooltip('Гарах / Эмнэлэг дахин сонгох'));
    pending.complete(oneAmount(41, '500'));
    await tester.pumpAndSettle();
    expect(choices, findsNothing);
    expect(find.text(nonpayable), findsNothing);
    expect(find.text('Нооргийн нийлбэр: 5.00 XTS'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('wrong booking and failed fixture are redacted and nonpayable', (
    tester,
  ) async {
    await openHistory(
      tester,
      reader: (id) async {
        if (id == 41) return oneAmount(42, '500');
        throw const FormatException('synthetic internal data');
      },
    );
    await tap(tester, choices.first);
    expect(find.text(invalidReview), findsOneWidget);
    expect(find.text('Нооргийн нийлбэр: 5.00 XTS'), findsNothing);
    await tap(tester, choices.last);
    expect(find.text(invalidReview), findsOneWidget);
    expect(find.textContaining('synthetic internal data'), findsNothing);
  });
}
