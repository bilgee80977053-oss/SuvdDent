import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental_mobile/app/app_theme.dart';
import 'package:nexora_dental_mobile/live/clinical_service_details.dart';
import 'package:nexora_dental_mobile/live/dental_knowledge.dart';
import 'package:nexora_dental_mobile/live/models.dart';

void main() {
  test('12 procedure explanations include concise patient-facing steps', () {
    expect(DentalKnowledge.overview.length, 12);
    for (final item in DentalKnowledge.overview) {
      expect(item.name.trim(), isNotEmpty);
      expect(item.summary.trim(), isNotEmpty);
      expect(item.whenUseful.trim(), isNotEmpty);
      expect(item.steps.length, greaterThanOrEqualTo(3));
      expect(item.priceFactors.trim(), isNotEmpty);
    }
  });

  test('Medical matching selects specific explanations', () {
    expect(DentalKnowledge.forService('Суваг эмчилгээ').name,
        'Суваг эмчилгээ');
    expect(DentalKnowledge.forService('Агт араа авах').name,
        'Агт арааны мэс ажилбар');
    expect(DentalKnowledge.forService('Шүд цэвэрлэгээ').name,
        'Шүдний чулуу, өнгөр цэвэрлэгээ');
  });

  test('Clinic price label comes from API response and never guesses', () {
    final service = BookingService.fromJson({
      'id': 11, 'name': 'Үзлэг', 'description': 'Тайлбар',
      'minPrice': 30000, 'maxPrice': 50000,
      'imagePath': null,
    });
    expect(clinicPriceLabel(service), '30 000 ₮ – 50 000 ₮');
    expect(clinicPriceLabel(null), 'Эмнэлгийн үнийг лавлана уу');
    final unknown = BookingService.fromJson({
      'id': 12, 'name': 'Үзлэг', 'description': 'Тайлбар',
      'minPrice': 0, 'maxPrice': null, 'imagePath': null,
    });
    expect(clinicPriceLabel(unknown), 'Үнэ эмнэлгээс тодруулна');
  });

  test('Payment estimate never becomes paid by parsing clinic service price', () {
    final estimate = PaymentEstimate.fromJson({
      'appointmentId': 91, 'serviceId': 11,
      'serviceName': 'Анхан үзлэг', 'currency': 'MNT',
      'priceSource': 'ClinicServiceCatalog',
      'minPriceMnt': 30000, 'maxPriceMnt': 30000,
      'isFinalAmount': false, 'totalDueMnt': null,
      'paymentState': 'NotTracked',
      'receiptState': 'NotIssued',
      'priceExplanation': 'Энэ нь урьдчилсан үнэ.',
    });
    expect(estimate.isFinalAmount, isFalse);
    expect(estimate.totalDueMnt, isNull);
    expect(estimate.paymentState, 'NotTracked');
    expect(estimate.receiptState, 'NotIssued');
  });

  testWidgets('Service explanation opens a detailed bottom sheet',
      (tester) async {
    await tester.pumpWidget(MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Builder(
          builder: (context) => TextButton(
            onPressed: () => ClinicalServiceDetails.show(
              context,
              category: 'Шүд цэвэрлэгээ',
            ),
            child: const Text('Дэлгэрэнгүй'),
          ),
        ),
      ),
    ));
    await tester.tap(find.text('Дэлгэрэнгүй'));
    await tester.pumpAndSettle();
    expect(find.text('Үзлэг, ажилбарын үед'), findsOneWidget);
    expect(find.text('Хаах'), findsOneWidget);
  });
  for (final reduced in [true, false]) {
    testWidgets(
      'service sheet preserves booking and reduce-motion behavior ($reduced)',
      (tester) async {
        tester.view.devicePixelRatio = 1;
        tester.view.physicalSize = reduced
            ? const Size(320, 640)
            : const Size(1440, 900);
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        var bookings = 0;
        final service = BookingService.fromJson({
          'id': 51,
          'name': 'Сургалтын үзлэг',
          'description': 'Сургалтын жишиг тайлбар',
          'minPrice': 12000,
        });
        final opened = find.text('Үзлэгийн танилцуулга');
        await tester.pumpWidget(
          MaterialApp(
            theme: AppTheme.light,
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context).copyWith(
                disableAnimations: reduced,
                textScaler: TextScaler.linear(reduced ? 2 : 1),
              ),
              child: child!,
            ),
            home: Scaffold(
              body: Builder(
                builder: (context) => TextButton(
                  onPressed: () => ClinicalServiceDetails.show(
                    context,
                    category: service.name,
                    service: service,
                    isAcademic: reduced,
                    onBook: () => bookings++,
                  ),
                  child: const Text('Үзлэгийн танилцуулга'),
                ),
              ),
            ),
          ),
        );
        await tester.tap(opened);
        await tester.pumpAndSettle();
        final scrollable = find.descendant(
          of: find.byKey(const ValueKey('clinical-service-sheet-content')),
          matching: find.byType(Scrollable),
        );
        final caption = find.textContaining(
          reduced
              ? 'Сургалтын жишиг үнэ. Бодит төлбөрийн дүн биш.'
              : 'Эмнэлгийн LIVE үйлчилгээний бүртгэлээс ирсэн үнэ.',
        );
        await tester.scrollUntilVisible(caption, 180, scrollable: scrollable);
        expect(caption, findsOneWidget);
        expect(
          find.textContaining(
            reduced ? 'LIVE үйлчилгээний' : 'Сургалтын жишиг үнэ.',
          ),
          findsNothing,
        );
        final close = find.byTooltip('Танилцуулгыг хаах');
        await tester.scrollUntilVisible(close, -180, scrollable: scrollable);
        final route = ModalRoute.of(tester.element(close));
        expect(route, isA<ModalBottomSheetRoute<void>>());
        expect(
          route!.transitionDuration,
          reduced ? Duration.zero : const Duration(milliseconds: 240),
        );
        expect(
          route.reverseTransitionDuration,
          reduced ? Duration.zero : const Duration(milliseconds: 180),
        );
        expect(
          tester.getSize(find.byKey(
            const ValueKey('clinical-service-sheet-content'),
          )).width,
          lessThanOrEqualTo(680),
        );
        expect(tester.takeException(), isNull);
        await tester.tap(close);
        await tester.pumpAndSettle();
        expect(bookings, 0);
        expect(close, findsNothing);
        await tester.tap(opened);
        await tester.pumpAndSettle();
        final book = find.text('Энэ үйлчилгээгээр цаг захиалах');
        expect(book.hitTestable(), findsOneWidget);
        await tester.tap(book);
        await tester.pumpAndSettle();
        expect(bookings, 1);
        expect(close, findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }
}
