import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental_mobile/app/nexora_app.dart';
import 'package:nexora_dental_mobile/features/auth/authentication_screen.dart';
import 'package:nexora_dental_mobile/features/auth/email_verification_screen.dart';
import 'package:nexora_dental_mobile/features/booking/booking_confirmation_screen.dart';
import 'package:nexora_dental_mobile/features/booking/slot_selection_screen.dart';
import 'package:nexora_dental_mobile/features/clinics/clinic_selection_screen.dart';
import 'package:nexora_dental_mobile/features/doctors/doctor_selection_screen.dart';
import 'package:nexora_dental_mobile/features/patient_profile/patient_profile_screen.dart';
import 'package:nexora_dental_mobile/features/services/service_selection_screen.dart';

Future<void> press(WidgetTester tester, Finder target) async {
  await tester.ensureVisible(target);
  await tester.tap(target);
  await tester.pumpAndSettle();
}

Finder next() => find.byKey(const ValueKey('continue-button'));

void main() {
  testWidgets(
    'Registration walkthrough visits all steps and finishes without a booking or email',
    (tester) async {
      await tester.pumpWidget(const NexoraDentalApp());
      expect(find.byType(ClinicSelectionScreen), findsOneWidget);
      expect(tester.widget<FilledButton>(next()).onPressed, isNull);
      await press(tester, find.byKey(const ValueKey('clinic-101')));
      await press(tester, next());
      expect(find.byType(AuthenticationScreen), findsOneWidget);
      await press(tester, find.text('Бүртгүүлэх'));
      await press(tester, next());
      expect(find.byType(EmailVerificationScreen), findsOneWidget);
      await press(tester, next());
      expect(find.byType(PatientProfileScreen), findsOneWidget);
      await press(tester, next());
      expect(find.byType(ServiceSelectionScreen), findsOneWidget);
      expect(tester.widget<FilledButton>(next()).onPressed, isNull);
      await press(tester, find.byKey(const ValueKey('service-51')));
      await press(tester, next());
      expect(find.byType(DoctorSelectionScreen), findsOneWidget);
      await press(tester, find.byKey(const ValueKey('doctor-31')));
      await press(tester, next());
      expect(find.byType(SlotSelectionScreen), findsOneWidget);
      await press(tester, find.byKey(const ValueKey('slot-1')));
      await press(tester, next());
      expect(find.byType(BookingConfirmationScreen), findsOneWidget);
      await press(tester, next());
      expect(find.byKey(const ValueKey('demo-complete')), findsOneWidget);
      expect(
        find.text('Цаг захиалга үүсгээгүй. Имэйл илгээгээгүй.'),
        findsOneWidget,
      );
      await press(tester, next());
      expect(find.byType(ClinicSelectionScreen), findsOneWidget);
      expect(tester.widget<FilledButton>(next()).onPressed, isNull);
    },
  );

  testWidgets(
    'Small-screen login/profile retrieval supports back navigation and clinic reset',
    (tester) async {
      tester.view.physicalSize = const Size(360, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(const NexoraDentalApp());
      await press(tester, find.byKey(const ValueKey('clinic-101')));
      await press(tester, next());
      expect(find.byType(AuthenticationScreen), findsOneWidget);
      expect(find.text('Нэвтрэх дэлгэцийн загвар'), findsOneWidget);
      await press(tester, next());
      await press(tester, next());
      await press(tester, find.text('Миний профайл'));
      expect(find.text('Профайл авах · жишээ'), findsOneWidget);
      await press(tester, next());
      expect(find.byType(ServiceSelectionScreen), findsOneWidget);
      for (var i = 0; i < 4; i++) {
        await press(tester, find.byKey(const ValueKey('back-button')));
      }
      await press(tester, find.byKey(const ValueKey('clinic-202')));
      await press(tester, next());
      await press(tester, next());
      expect(find.byType(EmailVerificationScreen), findsOneWidget);
      await press(tester, next());
      await press(tester, next());
      expect(find.byKey(const ValueKey('service-61')), findsOneWidget);
      expect(find.byKey(const ValueKey('service-51')), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
}
