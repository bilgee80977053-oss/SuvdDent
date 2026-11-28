import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental_mobile/app/app_theme.dart';
import 'package:nexora_dental_mobile/app/clinical_widgets.dart';
import 'package:nexora_dental_mobile/app/nexora_app.dart';

void main() {
  testWidgets('Figma Clinical Premium DEMO home is visible', (tester) async {
    await tester.pumpWidget(const NexoraDentalApp());
    expect(find.text('Шүдний эрүүл мэндийн шинэ дэвшил'), findsOneWidget);
    expect(find.text('Сайн байна уу!'), findsOneWidget);
    expect(find.text('Цаг сонгох'), findsOneWidget);
    expect(find.text('Үйлчилгээнүүд'), findsOneWidget);
    expect(
      find.text('DEMO · Зөвхөн жишээ өгөгдөл. Бодит цаг захиалахгүй.'),
      findsOneWidget,
    );
  });

  testWidgets('Clinical choice changes its selection presentation', (
    tester,
  ) async {
    var tapped = false;
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: Scaffold(
          body: ClinicalChoiceCard(
            label: 'Анхан шатны үзлэг',
            subtitle: 'Эмчийн үзлэг, зөвлөгөө',
            selected: true,
            leadingText: '01',
            onTap: () => tapped = true,
          ),
        ),
      ),
    );
    expect(find.text('Анхан шатны үзлэг'), findsOneWidget);
    expect(find.byIcon(Icons.check_circle_rounded), findsOneWidget);
    await tester.tap(find.text('Анхан шатны үзлэг'));
    expect(tapped, isTrue);
  });

  testWidgets('Navigation routes to its configured callback', (tester) async {
    var appointmentsOpened = false;
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: Scaffold(
          bottomNavigationBar: ClinicalNavigation(
            activeIndex: 0,
            onAppointments: () => appointmentsOpened = true,
          ),
        ),
      ),
    );
    await tester.tap(find.text('Миний цаг'));
    expect(appointmentsOpened, isTrue);
  });
  testWidgets('premium booking choices and navigation respect reduced motion', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: MediaQuery(
          data: const MediaQueryData(disableAnimations: true),
          child: Scaffold(
            body: Column(
              children: [
                ClinicalPageIntro(
                  eyebrow: 'Үйлчилгээ',
                  title: 'Эмч сонгох',
                  progress: 0.5,
                ),
                ClinicalChoiceCard(
                  label: 'Эмч сонгох',
                  subtitle: 'Шүдний үзлэг',
                  selected: true,
                  onTap: () {},
                ),
              ],
            ),
            bottomNavigationBar: const ClinicalNavigation(activeIndex: 0),
          ),
        ),
      ),
    );
    final progress = tester.widget<TweenAnimationBuilder<double>>(
      find.byType(TweenAnimationBuilder<double>),
    );
    expect(progress.duration, Duration.zero);
    final selection = tester.widget<AnimatedSwitcher>(
      find.byType(AnimatedSwitcher),
    );
    expect(selection.duration, Duration.zero);
    expect(
      tester
          .widgetList<AnimatedContainer>(
            find.descendant(
              of: find.byType(ClinicalNavigation),
              matching: find.byType(AnimatedContainer),
            ),
          )
          .every((item) => item.duration == Duration.zero),
      isTrue,
    );
    expect(tester.takeException(), isNull);
  });
}
