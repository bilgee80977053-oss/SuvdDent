import 'package:flutter/material.dart';

import '../features/auth/authentication_screen.dart';
import '../features/auth/email_verification_screen.dart';
import '../features/booking/booking_confirmation_screen.dart';
import '../features/booking/slot_selection_screen.dart';
import '../features/clinics/clinic_selection_screen.dart';
import '../features/doctors/doctor_selection_screen.dart';
import '../features/patient_profile/patient_profile_screen.dart';
import '../features/services/service_selection_screen.dart';
import 'app_theme.dart';
import 'clinical_widgets.dart';
import 'demo_flow_controller.dart';

class PatientFlow extends StatefulWidget {
  const PatientFlow({super.key});
  @override
  State<PatientFlow> createState() => _PatientFlowState();
}

class _PatientFlowState extends State<PatientFlow> {
  final flow = DemoFlowController();

  @override
  void dispose() {
    flow.dispose();
    super.dispose();
  }

  static const titles = {
    DemoStep.clinic: 'Сайн байна уу!',
    DemoStep.authentication: 'Тавтай морил.',
    DemoStep.verification: 'Имэйлээ баталгаажуулъя.',
    DemoStep.profile: 'Тантай танилцъя.',
    DemoStep.service: 'Танд ямар үйлчилгээ\nхэрэгтэй вэ?',
    DemoStep.doctor: 'Танд санал болгох эмч\nнар',
    DemoStep.slot: 'Та өөрт тохирох өдрөө\nсонгоорой',
    DemoStep.confirmation: 'Таны захиалгын\nмэдээлэл',
  };
  static const descriptions = {
    DemoStep.clinic: 'Эхлээд үйлчлүүлэх эмнэлгээ сонгоорой.',
    DemoStep.authentication: 'Сонгосон эмнэлгийн бүртгэлээр үргэлжлэх загвар.',
    DemoStep.verification:
        'Имэйл баталгаажуулалт нь захиалгын өмнөх шаардлага.',
    DemoStep.profile: 'Шинэ профайл үүсгэх эсвэл өөрийн профайлыг авах алхам.',
    DemoStep.service: 'Зөвхөн сонгосон эмнэлгийн жишээ үйлчилгээнүүд.',
    DemoStep.doctor: 'Сонгосон үйлчилгээнд тохирох жишээ эмч нар.',
    DemoStep.slot: 'Огноо, цаг сонгох дэлгэцийн урьдчилсан загвар.',
    DemoStep.confirmation: 'Энэ алхам бодит цаг захиалга үүсгэхгүй.',
  };

  String get buttonLabel => switch (flow.step) {
    DemoStep.authentication => 'Жишээ бүртгэлээр үргэлжлэх',
    DemoStep.verification => 'Жишээгээр баталгаажуулах',
    DemoStep.profile =>
      flow.profileMode == DemoProfileMode.create
          ? 'Профайл үүсгэх · жишээ'
          : 'Профайл авах · жишээ',
    DemoStep.confirmation => 'Загварын урсгалыг дуусгах',
    _ => 'Үргэлжлүүлэх',
  };

  Widget get screen => switch (flow.step) {
    DemoStep.clinic => ClinicSelectionScreen(flow: flow),
    DemoStep.authentication => AuthenticationScreen(flow: flow),
    DemoStep.verification => const EmailVerificationScreen(),
    DemoStep.profile => PatientProfileScreen(flow: flow),
    DemoStep.service => ServiceSelectionScreen(flow: flow),
    DemoStep.doctor => DoctorSelectionScreen(flow: flow),
    DemoStep.slot => SlotSelectionScreen(flow: flow),
    DemoStep.confirmation => BookingConfirmationScreen(flow: flow),
  };

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: flow,
    builder: (context, _) => PopScope(
      canPop: flow.step == DemoStep.clinic,
      onPopInvokedWithResult: (didPop, result) {
        if (!didPop) flow.back();
      },
      child: Scaffold(
        appBar: ClinicalHeader(
          onBack: flow.step == DemoStep.clinic ? null : flow.back,
        ),
        body: SafeArea(
          top: false,
          bottom: false,
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 760),
              child: SingleChildScrollView(
                key: ValueKey(flow.step),
                padding: const EdgeInsets.fromLTRB(24, 8, 24, 32),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (flow.step == DemoStep.clinic) ...[
                      const ClinicalPageIntro(
                        eyebrow: 'Шүдний эрүүл мэндийн шинэ дэвшил',
                        title: 'Сайн байна уу!',
                        description: 'Таны инээмсэглэл бидний урам зориг.',
                      ),
                      const SizedBox(height: 24),
                      ClinicalHero(
                        onBook: () => flow.continueFlow(),
                        enabled: flow.canContinue,
                      ),
                      const SizedBox(height: 24),
                      Text('Үйлчилгээнүүд',
                          style: Theme.of(context).textTheme.titleLarge),
                      const SizedBox(height: 16),
                      const Row(
                        children: [
                          ClinicalFeatureTile(
                            icon: Icons.medical_services_outlined,
                            label: 'Үзлэг',
                          ),
                          SizedBox(width: 8),
                          ClinicalFeatureTile(
                            icon: Icons.auto_awesome_outlined,
                            label: 'Цэвэрлэгээ',
                          ),
                          SizedBox(width: 8),
                          ClinicalFeatureTile(
                            icon: Icons.favorite_border,
                            label: 'Эмчилгээ',
                          ),
                        ],
                      ),
                      const SizedBox(height: 24),
                    ] else
                      ClinicalPageIntro(
                        eyebrow: switch (flow.step) {
                          DemoStep.service =>
                            'АЛХАМ 01 / 04    •    Үйлчилгээ',
                          DemoStep.doctor => 'АЛХАМ 02 / 04    •    Эмч',
                          DemoStep.slot =>
                            'АЛХАМ 03 / 04    •    Боломжит цаг',
                          DemoStep.confirmation =>
                            'АЛХАМ 04 / 04    •    Баталгаажуулалт',
                          _ => 'NEXORA DENTAL  •  СУРГАЛТЫН ЗАГВАР',
                        },
                        title: titles[flow.step]!,
                        description: descriptions[flow.step]!,
                        progress: flow.step.index >= DemoStep.service.index
                            ? (flow.step.index - DemoStep.service.index + 1) / 4
                            : null,
                      ),
                    const SizedBox(height: 20),
                    Container(
                      padding: const EdgeInsets.all(14),
                      decoration: BoxDecoration(
                        color: const Color(0xFFFFF2D7),
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: const Row(
                        children: [
                          Icon(Icons.science_outlined, size: 20),
                          SizedBox(width: 10),
                          Expanded(
                            child: Text(
                              'DEMO · Зөвхөн жишээ өгөгдөл. Бодит цаг захиалахгүй.',
                              style: TextStyle(fontSize: 12),
                            ),
                          ),
                        ],
                      ),
                    ),
                    if (flow.clinic != null &&
                        flow.step != DemoStep.clinic) ...[
                      const SizedBox(height: 12),
                      Text(
                        flow.clinic!.name,
                        style: const TextStyle(
                          color: AppColors.blue,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                    const SizedBox(height: 24),
                    screen,
                  ],

                ),
              ),
            ),
          ),
        ),
        bottomNavigationBar: DecoratedBox(
          decoration: const BoxDecoration(
            color: AppColors.canvas,
            border: Border(top: BorderSide(color: AppColors.line)),
          ),
          child: SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(24, 12, 24, 12),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 712),
                    child: FilledButton.icon(
                      key: const ValueKey('continue-button'),
                      onPressed: flow.complete
                          ? flow.restart
                          : flow.canContinue
                          ? () {
                              flow.continueFlow();
                            }
                          : null,
                      icon: Icon(
                        flow.complete ? Icons.restart_alt : Icons.arrow_forward,
                      ),
                      label: Text(
                        flow.complete ? 'Эхнээс нь үзэх' : buttonLabel,
                      ),
                    ),
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'Офлайн загвар · Өгөгдөл хадгалахгүй',
                    style: TextStyle(fontSize: 11),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
