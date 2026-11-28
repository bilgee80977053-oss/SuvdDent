import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../app/app_theme.dart';
import '../app/clinical_widgets.dart';
import 'dental_api.dart';
import 'live_flow_controller.dart';
import 'models.dart';
import 'live_presentation.dart';
import 'notification_settings_screen.dart';
import 'clinical_service_details.dart';
import 'dental_knowledge.dart';
import 'payment_estimate_screen.dart';
import 'patient_invoice_review.dart';
import 'profile_settings_screen.dart';
import 'reschedule_appointment_screen.dart';

class LivePatientFlow extends StatefulWidget {
  final ApiSettings settings;
  final DentalApi? api;
  // Explicit review-only injection. The normal app supplies no reader.
  final InvoiceReviewReader? invoiceReviewReader;
  const LivePatientFlow({
    super.key,
    required this.settings,
    this.api,
    this.invoiceReviewReader,
  });
  @override
  State<LivePatientFlow> createState() => _LivePatientFlowState();
}

class _LivePatientFlowState extends State<LivePatientFlow> {
  late final LiveFlowController flow;
  final historyScroll = ScrollController();
  int? reviewBookingId;

  InvoiceReviewReader? get reviewReader =>
      kReleaseMode || flow.session == null ? null : widget.invoiceReviewReader;
  final identifier = TextEditingController(),
      password = TextEditingController(),
      name = TextEditingController(),
      email = TextEditingController(),
      phone = TextEditingController(),
      code = TextEditingController();
  final authKey = GlobalKey<FormState>(),
      profileKey = GlobalKey<FormState>(),
      verifyKey = GlobalKey<FormState>();
  bool registering = false,
      recoveringEmail = false,
      showHistory = false,
      showDashboard = false;
  bool selectingVerificationMethod = false;
  RegistrationMethod? selectedMethod, sentMethod;
  Timer? resendTimer;
  int resendSeconds = 0;
  static const methodTitle = 'Баталгаажуулах код авах аргаа сонгоно уу';

  static const titles = [
    'Эмнэлэг сонгох',
    'Нэвтрэх / Бүртгүүлэх',
    'Бүртгэл баталгаажуулах',
    'Миний мэдээлэл',
    'Үйлчилгээ сонгох',
    'Эмч сонгох',
    'Боломжит цаг сонгох',
    'Захиалга баталгаажуулах',
  ];
  @override
  void initState() {
    super.initState();
    flow = LiveFlowController(widget.api ?? DentalApi(widget.settings));
    flow.addListener(onChange);
  }

  @override
  void didUpdateWidget(covariant LivePatientFlow oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.invoiceReviewReader != widget.invoiceReviewReader) {
      reviewBookingId = null;
    }
    if (oldWidget.settings.baseUrl != widget.settings.baseUrl ||
        oldWidget.settings.clinicId != widget.settings.clinicId ||
        oldWidget.api != widget.api) {
      reviewBookingId = null;
      flow.changeClinic(widget.api ?? DentalApi(widget.settings));
    }
  }

  void onChange() {
    if (flow.session == null) reviewBookingId = null;
    if (flow.session == null && flow.step == 0) {
      for (final field in [identifier, password, name, email, phone, code]) {
        field.clear();
      }
      showHistory = false;
      showDashboard = false;
      selectingVerificationMethod = false;
      selectedMethod = sentMethod = null;
      resendTimer?.cancel();
      resendSeconds = 0;
    }
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    resendTimer?.cancel();
    flow.removeListener(onChange);
    flow.dispose();
    historyScroll.dispose();
    for (final c in [identifier, password, name, email, phone, code]) {
      c.dispose();
    }
    super.dispose();
  }

  Widget field(
    TextEditingController controller,
    String label, {
    bool secret = false,
    bool optional = false,
    TextInputType? keyboard,
    String? Function(String)? validate,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: TextFormField(
      controller: controller,
      enabled: !flow.busy,
      obscureText: secret,
      autocorrect: !secret,
      enableSuggestions: !secret,
      keyboardType: keyboard,
      decoration: InputDecoration(labelText: label),
      validator: (v) {
        if (!optional && (v == null || v.trim().isEmpty)) {
          return 'Энэ талбарыг бөглөнө үү.';
        }
        return validate?.call(v ?? '');
      },
    ),
  );
  Widget button(
    String label,
    VoidCallback action, {
    bool enabled = true,
    bool secondary = false,
  }) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 6),
    child: secondary
        ? OutlinedButton(
            onPressed: !flow.busy && enabled ? action : null,
            style: OutlinedButton.styleFrom(
              foregroundColor: AppColors.navy,
              minimumSize: const Size.fromHeight(52),
              side: const BorderSide(color: AppColors.line),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(15),
              ),
            ),
            child: Text(label),
          )
        : FilledButton(
            onPressed: !flow.busy && enabled ? action : null,
            child: Text(label),
          ),
  );

  Widget empty(String message) =>
      Padding(padding: const EdgeInsets.all(20), child: Text(message));
  Widget choice(
    String label,
    String subtitle,
    bool selected,
    VoidCallback action,
  ) => ClinicalChoiceCard(
    label: label,
    subtitle: subtitle,
    selected: selected,
    onTap: !flow.busy && !flow.uncertainBooking ? action : null,
    leadingText: label.isEmpty ? '?' : label.substring(0, 1),
  );

  String local(DateTime time, String zone) {
    try {
      return LiveLabels.instant(time, zone);
    } on ApiFailure catch (e) {
      return e.message;
    }
  }

  Widget auth() => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Form(
        key: authKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SegmentedButton<bool>(
              direction: MediaQuery.textScalerOf(context).scale(14) >= 22
                  ? Axis.vertical
                  : Axis.horizontal,
              segments: const [
                ButtonSegment(value: false, label: Text('Нэвтрэх')),
                ButtonSegment(value: true, label: Text('Бүртгүүлэх')),
              ],
              selected: {registering},
              onSelectionChanged: flow.busy
                  ? null
                  : (v) => setState(() {
                      registering = v.first;
                      recoveringEmail = false;
                      password.clear();
                    }),
            ),
            const SizedBox(height: 20),
            if (registering) ...[
              field(name, 'Овог нэр'),
              field(
                email,
                'Имэйл (заавал биш)',
                optional: true,
                keyboard: TextInputType.emailAddress,
                validate: (v) => v.trim().isEmpty && phone.text.trim().isEmpty
                    ? 'Имэйл эсвэл утасны дугаар оруулна уу.'
                    : v.trim().isEmpty ||
                          RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$')
                              .hasMatch(v.trim())
                    ? null
                    : 'Имэйл хаягаа шалгана уу.',
              ),
              field(
                phone,
                'Утас (заавал биш)',
                optional: true,
                keyboard: TextInputType.phone,
                validate: (v) =>
                    v.trim().isEmpty ||
                        RegExp(r'^(?:976)?[0-9]{8}$').hasMatch(v.trim())
                    ? null
                    : 'Утасны дугаараа шалгана уу.',
              ),
            ] else
              field(
                identifier,
                'Имэйл эсвэл утас',
                keyboard: TextInputType.emailAddress,
              ),
            field(
              password,
              'Нууц үг',
              secret: true,
              validate: (v) => registering && v.length < 8
                  ? 'Нууц үг 8-аас доошгүй тэмдэгт байна.'
                  : null,
            ),
            if (registering)
              const Text(
                'Имэйл эсвэл утасны дугаараас дор хаяж нэгийг оруулна уу.',
              ),
            button(registering ? 'Бүртгэл үүсгэх' : 'Нэвтрэх', () async {
              if (!authKey.currentState!.validate()) return;
              final secret = password.text;
              if (registering) {
                openMethodSelection();
              } else {
                await flow.login(identifier.text.trim(), secret);
                if (mounted) password.clear();
              }
            }),
            if (flow.registration != null && sentMethod != null)
              button('Ирсэн кодыг оруулах', returnToOtp, secondary: true),
            if (!registering)
              button(
                recoveringEmail
                    ? 'Имэйл баталгаажуулалтыг хаах'
                    : 'Баталгаажуулах имэйл ирээгүй юу?',
                () => setState(() {
                  recoveringEmail = !recoveringEmail;
                  if (recoveringEmail && identifier.text.contains('@')) {
                    email.text = identifier.text.trim();
                  }
                }),
                secondary: true,
              ),
            if (!registering && recoveringEmail) ...[
              const Text(
                'Өмнө бүртгүүлсэн имэйлээ оруулаад баталгаажуулах '
                'кодыг дахин хүснэ үү. Имэйл үйлчилгээ ажиллахгүй бол '
                'код ирэхгүй, бүртгэл идэвхжихгүй.',
              ),
              const SizedBox(height: 12),
              field(
                email,
                'Бүртгүүлсэн имэйл',
                keyboard: TextInputType.emailAddress,
              ),
              button('Баталгаажуулах код дахин илгээх', () async {
                final address = email.text.trim();
                if (!address.contains('@')) {
                  return;
                }
                await flow.resendRegistrationCode(address);
              }, secondary: true),
              field(
                code,
                widget.settings.isAcademic
                    ? 'Серверийн цонхонд гарсан 6 оронтой код'
                    : 'Имэйлээр ирсэн 6 оронтой код',
                keyboard: TextInputType.number,
              ),
              button('Имэйлээр бүртгэлээ баталгаажуулах', () async {
                final address = email.text.trim();
                final pin = code.text.trim();
                if (!address.contains('@') ||
                    pin.length != 6 ||
                    int.tryParse(pin) == null) {
                  return;
                }
                await flow.verifyRegistrationEmail(address, pin);
                if (mounted && flow.error == null) {
                  code.clear();
                  setState(() => recoveringEmail = false);
                }
              }),
            ],
          ],
        ),
      ),
    ),
  );
  String maskedDestination(RegistrationMethod method) {
    if (method == RegistrationMethod.sms) {
      final number = phone.text.trim();
      if (number.length < 2) return '';
      return '•••• ${number.substring(number.length - 2)}';
    }
    final address = email.text.trim();
    final separator = address.indexOf('@');
    if (separator <= 0 || separator == address.length - 1) return '';
    final domain = address.substring(separator + 1);
    final localFirst = String.fromCharCode(address.runes.first);
    final domainFirst = String.fromCharCode(domain.runes.first);
    return '$localFirst•••@$domainFirst•••';
  }

  void openMethodSelection() {
    FocusScope.of(context).unfocus();
    setState(() {
      selectedMethod =
          sentMethod ??
          (email.text.trim().isNotEmpty
              ? RegistrationMethod.email
              : RegistrationMethod.sms);
      selectingVerificationMethod = true;
      flow.error = null;
      flow.notice = null;
    });
    if (historyScroll.hasClients) historyScroll.jumpTo(0);
  }

  void returnToOtp() {
    FocusScope.of(context).unfocus();
    setState(() {
      selectingVerificationMethod = false;
      flow.error = null;
      flow.notice = null;
      flow.step = 2;
    });
    if (historyScroll.hasClients) historyScroll.jumpTo(0);
  }

  void startResendCountdown(int seconds) {
    resendTimer?.cancel();
    if (!mounted) return;
    setState(() => resendSeconds = seconds);
    if (seconds <= 0) return;
    resendTimer = Timer.periodic(const Duration(seconds: 1), (timer) {
      final remaining = seconds - timer.tick;
      if (!mounted || remaining <= 0) timer.cancel();
      if (mounted) {
        setState(() => resendSeconds = remaining > 0 ? remaining : 0);
      }
    });
  }

  Future<void> sendRegistrationCode(RegistrationMethod method) async {
    if (flow.busy || resendSeconds > 0) return;
    final pending = flow.registration;
    if (pending == null) {
      await flow.registerWithMethod(
        name.text.trim(),
        email.text.trim(),
        password.text,
        phone.text.trim().isEmpty ? null : phone.text.trim(),
        method,
      );
    } else {
      await flow.requestRegistrationCode(method);
    }
    if (!mounted || flow.step == 0) return;
    final currentPending = flow.registration;
    if (currentPending != null) password.clear();
    if (flow.error != null) {
      final seconds =
          flow.registrationRetryAfterSeconds ??
          (currentPending != pending
              ? currentPending?.retryAfterSeconds
              : null);
      if (seconds != null && seconds > 0) startResendCountdown(seconds);
      if (pending == null && currentPending?.method != null) {
        // The server found a previous accepted delivery; no new code was sent.
        setState(() {
          sentMethod = currentPending!.method;
          selectingVerificationMethod = false;
          flow.step = 2;
        });
      }
      return;
    }
    if (currentPending == null) return;
    code.clear();
    setState(() {
      sentMethod = method;
      selectingVerificationMethod = false;
    });
    startResendCountdown(currentPending.retryAfterSeconds ?? 60);
  }

  String get cooldownLabel {
    final minutes = (resendSeconds ~/ 60).toString().padLeft(2, '0');
    final seconds = (resendSeconds % 60).toString().padLeft(2, '0');
    return '$minutes:$seconds';
  }

  Widget resendStatus() => Padding(
    padding: const EdgeInsets.symmetric(vertical: 8),
    child: Semantics(
      liveRegion: resendSeconds == 0,
      label: resendSeconds == 0
          ? 'Шинэ код хүсэх боломжтой.'
          : 'Дахин хүсэх хүртэл $resendSeconds секунд.',
      child: ExcludeSemantics(
        child: Text(
          resendSeconds > 0
              ? 'Дахин хүсэх хүртэл $cooldownLabel'
              : 'Шинэ код хүсэх боломжтой.',
          style: const TextStyle(color: AppColors.muted),
        ),
      ),
    ),
  );

  Widget methodSelection() {
    final sms = maskedDestination(RegistrationMethod.sms);
    final mail = maskedDestination(RegistrationMethod.email);
    final current = selectedMethod;
    final canSend =
        current != null &&
        maskedDestination(current).isNotEmpty &&
        resendSeconds == 0;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ClinicalChoiceCard(
          icon: Icons.sms_outlined,
          label: 'Утасны дугаараар — SMS',
          subtitle: sms.isEmpty ? 'Утасны дугаар оруулаагүй' : sms,
          selected: current == RegistrationMethod.sms,
          onTap: flow.busy || sms.isEmpty
              ? null
              : () => setState(() => selectedMethod = RegistrationMethod.sms),
        ),
        ClinicalChoiceCard(
          icon: Icons.mail_outline_rounded,
          label: 'Имэйл хаягаар — Email',
          subtitle: mail.isEmpty ? 'Имэйл хаяг оруулаагүй' : mail,
          selected: current == RegistrationMethod.email,
          onTap: flow.busy || mail.isEmpty
              ? null
              : () => setState(() => selectedMethod = RegistrationMethod.email),
        ),
        if (current == RegistrationMethod.sms)
          const Padding(
            padding: EdgeInsets.only(bottom: 8),
            child: Text(
              'SMS-ээр нэвтрэх бүртгэлээ баталгаажуулна. Цаг захиалахад баталгаажсан имэйл шаардлагатай.',
            ),
          ),
        if (widget.settings.isAcademic)
          const Text(
            'Сургалтын горим: SMS болон имэйл бодитоор илгээхгүй. Код серверийн цонхонд гарна.',
          ),
        if (resendSeconds > 0) resendStatus(),
        button('Код авах', () {
          if (current != null) sendRegistrationCode(current);
        }, enabled: canSend),
        if (sentMethod != null)
          button('Код оруулах хэсэг рүү буцах', returnToOtp, secondary: true)
        else if (flow.registration == null)
          button(
            'Бүртгэлийн мэдээлэл засах',
            backFromVerification,
            secondary: true,
          ),
      ],
    );
  }

  void backFromVerification() {
    if (flow.busy) return;
    if (selectingVerificationMethod) {
      if (sentMethod != null) {
        returnToOtp();
      } else if (flow.registration == null) {
        setState(() {
          selectingVerificationMethod = false;
          flow.error = null;
          flow.notice = null;
        });
      } else {
        flow.logout();
      }
    } else if (flow.step == 2 &&
        sentMethod != null &&
        flow.registration != null) {
      openMethodSelection();
    } else {
      flow.back();
    }
  }

  Widget verification() {
    if (flow.session != null) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text(
            'Нэвтрэлт амжилттай. Үргэлжлүүлэхийн тулд профайлаа нээнэ үү.',
          ),
          button('Миний профайлыг авах', flow.loadProfile),
        ],
      );
    }
    if (flow.registration == null) {
      return button('Нэвтрэх', () {
        flow.step = 1;
        flow.changed();
      });
    }
    final method = sentMethod;
    final destination = method == null ? '' : maskedDestination(method);
    return AutofillGroup(
      onDisposeAction: AutofillContextAction.cancel,
      child: Form(
        key: verifyKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (destination.isNotEmpty)
              ClinicalInfoPanel(
                title: method == RegistrationMethod.sms
                    ? 'Утасны дугаараар · SMS'
                    : 'Имэйл хаягаар · Email',
                children: [ClinicalDetail('Сонгосон хаяг', destination)],
              ),
            const SizedBox(height: 12),
            Text(
              widget.settings.isAcademic
                  ? 'Серверийн цонхонд гарсан сургалтын кодыг оруулна уу. Бодит мессеж илгээгээгүй.'
                  : 'Сонгосон сувгаар хүлээж авсан кодоо оруулна уу. Код 120 секунд хүчинтэй.',
            ),
            const SizedBox(height: 16),
            TextFormField(
              controller: code,
              enabled: !flow.busy,
              keyboardType: TextInputType.number,
              textInputAction: TextInputAction.done,
              autofillHints: const [AutofillHints.oneTimeCode],
              inputFormatters: [
                FilteringTextInputFormatter.digitsOnly,
                LengthLimitingTextInputFormatter(6),
              ],
              autocorrect: false,
              enableSuggestions: false,
              obscureText: true,
              maxLength: 6,
              decoration: const InputDecoration(labelText: '6 оронтой код'),
              validator: (v) => RegExp(r'^[0-9]{6}$').hasMatch(v ?? '')
                  ? null
                  : '6 оронтой код оруулна уу.',
              onFieldSubmitted: (_) => submitOtp(),
            ),
            button('Код шалгах', submitOtp),
            if (method != null &&
                flow.registration?.registrationToken != null) ...[
              resendStatus(),
              button(
                'Код дахин авах',
                () => sendRegistrationCode(method),
                enabled: resendSeconds == 0,
                secondary: true,
              ),
              button(
                'Баталгаажуулах аргаа солих',
                openMethodSelection,
                secondary: true,
              ),
            ],
          ],
        ),
      ),
    );
  }

  Future<void> submitOtp() async {
    if (flow.busy || !(verifyKey.currentState?.validate() ?? false)) return;
    final wasSms = sentMethod == RegistrationMethod.sms;
    await flow.verify(code.text);
    if (!mounted) return;
    code.clear();
    if (flow.registration == null && flow.step == 1) {
      resendTimer?.cancel();
      setState(() {
        registering = false;
        selectingVerificationMethod = false;
        selectedMethod = sentMethod = null;
        resendSeconds = 0;
        if (wasSms) {
          flow.notice =
              '${flow.notice} Цаг захиалахад баталгаажсан имэйл шаардлагатай.';
        }
      });
    }
  }

  void openNotificationSettings() {
    if (flow.busy || flow.session == null) return;
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => NotificationSettingsScreen(flow: flow),
      ),
    );
  }

  void openProfileSettings() {
    if (flow.busy || flow.session == null || flow.patient == null) return;
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => ProfileSettingsScreen(flow: flow),
      ),
    );
  }

  Widget profile() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (flow.patient != null) ...[
        ClinicalInfoPanel(
          title: flow.patient!.fullName,
          children: [
            ClinicalDetail('Утас', flow.patient!.phone),
            if (flow.patient!.dateOfBirth case final birth?)
              ClinicalDetail('Төрсөн огноо', birth),
            if (flow.patient!.address case final address?)
              ClinicalDetail('Хаяг', address),
          ],
        ),
        const SizedBox(height: 10),
        button(
          'Профайлын мэдээлэл засах',
          openProfileSettings,
          secondary: true,
        ),
        button('Үйлчилгээ авах', flow.loadServices),
      ] else if (flow.profileMissing)
        Form(
          key: profileKey,
          child: Column(
            children: [
              const Text(
                'Таны өвчтөний бүртгэл олдсонгүй. Бүртгэлээ үүсгэнэ үү.',
              ),
              field(name, 'Овог нэр'),
              field(phone, 'Утас', keyboard: TextInputType.phone),
              button('Миний профайл үүсгэх', () {
                if (profileKey.currentState!.validate()) {
                  flow.createProfile(name.text.trim(), phone.text.trim());
                }
              }),
            ],
          ),
        ),
      button('Профайлыг шинэчлэх', flow.loadProfile, secondary: true),
      button('Мэдэгдлийн тохиргоо', openNotificationSettings, secondary: true),
    ],
  );
  void openEducationalInfo(String category) {
    ClinicalServiceDetails.show(
      context,
      category: category,
      isAcademic: widget.settings.isAcademic,
    );
  }

  void openServiceInfo(BookingService s) {
    if (flow.busy || flow.uncertainBooking) return;
    ClinicalServiceDetails.show(
      context,
      category: s.name,
      service: s,
      isAcademic: widget.settings.isAcademic,
      onBook: () {
        if (!mounted || flow.busy || flow.uncertainBooking) return;
        flow.selectService(s);
        flow.loadDoctors();
      },
    );
  }

  void openPayment(Appointment appointment) {
    if (flow.session == null || flow.busy) return;
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (context) => PaymentEstimateScreen(
          api: flow.api,
          appointment: appointment,
          allowEstimateEmail: !flow.api.settings.isAcademic,
        ),
      ),
    );
  }

  Widget services() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text(
        'Үйлчилгээ сонгон тайлбар, үнийн мэдээллийг харна уу.',
        style: TextStyle(color: AppColors.muted, fontSize: 13),
      ),
      if (widget.settings.isAcademic)
        const Text(
          'Үзүүлэнгийн жишиг үнэ',
          style: TextStyle(color: AppColors.blue, fontWeight: FontWeight.w700),
        ),
      const SizedBox(height: 18),
      if (flow.services.isEmpty && !flow.busy && flow.error == null) ...[
        const ClinicalInfoPanel(
          title: 'Цаг захиалах үйлчилгээ одоогоор бүртгэгдээгүй',
          children: [
            Text(
              'Suvd Dent-ийн LIVE үйлчилгээний жагсаалт хоосон байна. '
              'Эмнэлгийн захирал үйлчилгээний нэр, тайлбар болон '
              'үнийн мэдээллээ баталгаажуулж бүртгэсний дараа '
              'эндээс үйлчилгээ сонгож цаг захиална. '
              'Бүртгэгдээгүй үйлчилгээг захиалах боломжгүй.',
            ),
          ],
        ),
        const SizedBox(height: 12),
        OutlinedButton.icon(
          onPressed: () => openEducationalInfo('Шүдний үзлэг'),
          icon: const Icon(Icons.menu_book_outlined),
          label: const Text('Эмчилгээний ерөнхий мэдээлэл унших'),
        ),
        const SizedBox(height: 8),
        OutlinedButton.icon(
          onPressed: () => setState(() => showDashboard = true),
          icon: const Icon(Icons.home_outlined),
          label: const Text('Нүүр хуудас руу буцах'),
        ),
      ],
      for (final s in flow.services)
        ClinicalChoiceCard(
          label: LiveLabels.serviceName(s),
          subtitle: '${clinicPriceLabel(s)}  •  Дэлгэрэнгүй үзэх',
          selected: flow.service?.id == s.id,
          onTap: !flow.busy && !flow.uncertainBooking
              ? () => openServiceInfo(s)
              : null,
          icon: Icons.medical_services_outlined,
        ),
      if (flow.service != null)
        button('Сонгосон үйлчилгээгээр үргэлжлүүлэх', flow.loadDoctors),
      button('Үйлчилгээг шинэчлэх', flow.loadServices, secondary: true),
    ],
  );

  Widget doctors() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (!flow.busy && flow.doctors.isEmpty)
        empty('Энэ үйлчилгээнд эмч олдсонгүй.'),
      if (flow.doctors.isNotEmpty) ...[
        const Text(
          'Сонгосон үйлчилгээг үзүүлэх эмчээс нэгийг сонгоно уу.',
          style: TextStyle(color: AppColors.muted),
        ),
        const SizedBox(height: 14),
      ],
      LayoutBuilder(
        builder: (context, c) => Wrap(
          spacing: 12,
          runSpacing: 12,
          children: [
            for (final d in flow.doctors)
              SizedBox(
                width: c.maxWidth >= 600 ? (c.maxWidth - 12) / 2 : c.maxWidth,
                child: choice(
                  LiveLabels.doctorName(d),
                  'Эмч · ${LiveLabels.specialty(d)}\nАжиллаж эхэлсэн он: ${d.practiceStartYear}${LiveLabels.syntheticDoctor(d) ? '\nТуршилтын мэдээлэл' : ''}',
                  flow.doctor?.id == d.id,
                  () => flow.selectDoctor(d),
                ),
              ),
          ],
        ),
      ),
      button(
        'Огноо, цаг сонгох',
        openBookingSlots,
        enabled: flow.doctor != null,
      ),
      button('Эмч нарыг шинэчлэх', flow.loadDoctors, secondary: true),
    ],
  );
  Future<void> openBookingSlots() async {
    if (flow.busy ||
        flow.uncertainBooking ||
        flow.doctor == null ||
        flow.service == null) {
      return;
    }
    flow.openSlots();
    if (flow.availability == null) await flow.loadSlots();
  }

  Future<void> selectBookingDate(DateTime picked) async {
    if (flow.busy ||
        flow.uncertainBooking ||
        flow.step != 6 ||
        flow.session == null ||
        flow.doctor == null ||
        flow.service == null) {
      return;
    }
    final next = calendarDate(picked);
    if (next == flow.date) return;
    flow.changeDate(next);
    await flow.loadSlots();
  }

  Future<void> openBookingDatePicker() async {
    final api = flow.api;
    final session = flow.session;
    final doctor = flow.doctor?.id;
    final service = flow.service?.id;
    final initialDate = flow.date;
    final result = await showMongolianDatePicker(
      context,
      initialDate,
      timeZoneId: flow.availability?.timeZoneId,
    );
    if (!mounted ||
        result == null ||
        flow.api != api ||
        flow.session != session ||
        flow.doctor?.id != doctor ||
        flow.service?.id != service ||
        flow.date != initialDate) {
      return;
    }
    await selectBookingDate(result);
  }

  Widget slots() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(
        LiveLabels.date(flow.availability?.date ?? flow.date),
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      const Text(
        'Өдөр сонгоход боломжит цаг автоматаар шинэчлэгдэнэ. '
        'Эцсийн боломжийг сервер баталгаажуулна.',
      ),
      const SizedBox(height: 16),
      ClinicalCalendarPicker(
        selectedDate: DateTime.parse(flow.date),
        enabled: !flow.busy && !flow.uncertainBooking,
        onSelected: selectBookingDate,
      ),
      const SizedBox(height: 12),
      button(
        'Өөр огноо сонгох',
        openBookingDatePicker,
        enabled: !flow.uncertainBooking && flow.availability != null,
        secondary: true,
      ),
      button(
        'Боломжит цагийг шинэчлэх',
        flow.loadSlots,
        enabled: !flow.uncertainBooking,
      ),
      if (flow.availability != null) ...[
        Text(LiveLabels.zone(flow.availability!.timeZoneId)),
        const SizedBox(height: 16),
        if (flow.availability!.slots.isEmpty)
          empty('Энэ өдөр боломжит цаг алга. Өөр огноо сонгоно уу.'),
        LiveSlotChoices(
          page: flow.availability!,
          selected: flow.slot,
          enabled: !flow.busy && !flow.uncertainBooking,
          onSelected: flow.selectSlot,
        ),
        if (flow.slot != null)
          Semantics(
            liveRegion: true,
            child: BookingTimeSummary(
              slot: flow.slot!,
              date: flow.availability!.date,
              timeZoneId: flow.availability!.timeZoneId,
            ),
          ),
      ] else if (!flow.busy && flow.error == null)
        empty('Боломжит цагийг харахын тулд шинэчлэх товчийг дарна уу.'),
      button('Захиалгыг хянах', flow.review, enabled: flow.slot != null),
    ],
  );
  Widget details(Appointment a) {
    String? doctorName, serviceName;
    for (final doctor in flow.doctors) {
      if (doctor.id == a.doctorProfileId) {
        doctorName = LiveLabels.doctorName(doctor);
      }
    }
    for (final service in flow.services) {
      if (service.id == a.serviceId) {
        serviceName = LiveLabels.serviceName(service);
      }
    }
    return AppointmentDetailsCard(
      appointment: a,
      doctorName: doctorName,
      serviceName: serviceName,
    );
  }

  Widget confirmation() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (flow.appointment != null) ...[
        Text(
          flow.appointment!.status == 'Confirmed'
              ? 'Захиалга амжилттай үүслээ.'
              : 'Энэ захиалга одоо идэвхгүй байна.',
          style: const TextStyle(fontWeight: FontWeight.bold),
        ),
        details(flow.appointment!),
        button('Төлбөрийн дэлгэрэнгүй', () => openPayment(flow.appointment!)),
        button('Шинэ захиалга', flow.newBooking),
      ] else if (flow.conflict) ...[
        const Text(
          'Сонгосон цаг боломжгүй болсон. Боломжит цагийг шинэчлээд дахин сонгоно уу.',
        ),
        button('Боломжит цагийг шинэчлэх', flow.loadSlots),
      ] else if (flow.slot != null) ...[
        Text(
          '${flow.service == null ? '' : LiveLabels.serviceName(flow.service!)}\n${flow.doctor == null ? '' : LiveLabels.doctorName(flow.doctor!)}\n${local(flow.slot!.startUtc, flow.availability!.timeZoneId)}',
        ),
        BookingTimeSummary(
          slot: flow.slot!,
          date: flow.availability!.date,
          timeZoneId: flow.availability!.timeZoneId,
        ),
        const Text(
          'Захиалга илгээхэд сервер боломжит цаг болон таны эрхийг дахин шалгана.',
        ),
        if (flow.uncertainBooking)
          const Text(
            'Өмнөх хүсэлтийн үр дүн тодорхойгүй. Ижил хүсэлтээр дахин шалгана уу. Өөр захиалга хийхээс өмнө үр дүнг баталгаажуулна уу.',
          ),
        button(
          flow.uncertainBooking
              ? 'Ижил хүсэлтээр дахин оролдох'
              : 'Захиалга баталгаажуулах',
          flow.book,
        ),
      ],
    ],
  );
  Future<void> confirmCancellation(Appointment value) async {
    final activeApi = flow.api;
    final activeSession = flow.session;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Захиалга цуцлах уу?'),
        content: Text(
          '${local(value.startUtc, value.clinicTimeZoneId)}\nЭнэ захиалгыг цуцлахдаа итгэлтэй байна уу?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Буцах'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Тийм, цуцлах'),
          ),
        ],
      ),
    );
    if (!mounted ||
        confirmed != true ||
        flow.api != activeApi ||
        flow.session != activeSession ||
        flow.busy) {
      return;
    }
    // Bring loading/result feedback into view after confirming a card near the
    // bottom of a long history page.
    if (historyScroll.hasClients) historyScroll.jumpTo(0);
    await flow.cancelAppointment(value);
  }

  void openReschedule(Appointment source, {bool resume = false}) {
    if (flow.busy || flow.session == null) return;
    if (!resume && !flow.beginReschedule(source)) return;
    final draft = flow.reschedule;
    if (draft == null || draft.source.id != source.id) return;
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => RescheduleAppointmentScreen(flow: flow, draft: draft),
      ),
    );
    if (!resume) flow.loadRescheduleSlots();
  }

  void loadHistoryPage(int page) {
    if (reviewBookingId != null) setState(() => reviewBookingId = null);
    flow.loadAppointments(page);
  }

  Widget historyAppointment(Appointment value) => Padding(
    padding: const EdgeInsets.only(bottom: 18),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        details(value),
        if (reviewReader case final reader?) ...[
          button(
            'Туршилтын задаргаа харах',
            () => setState(() => reviewBookingId = value.id),
            secondary: true,
          ),
          if (reviewBookingId == value.id)
            PatientInvoiceReviewPanel(
              key: ValueKey(value.id),
              bookingId: value.id,
              reader: reader,
            ),
        ] else
          button(
            'Төлбөр / үнийн мэдээлэл',
            () => openPayment(value),
            secondary: true,
          ),
        if (flow.uncertainCancellations.contains(value.id)) ...[
          const Text('Цуцлалтын үр дүнг шалгана уу.'),
          button('Төлөв шалгах', () => flow.reconcileCancellation(value.id)),
        ],
        if (flow.canReschedule(value))
          button(
            'Захиалгын цаг шилжүүлэх',
            () => openReschedule(value),
            secondary: true,
          ),
        if (flow.canCancel(value))
          button(
            flow.uncertainCancellations.contains(value.id)
                ? 'Цуцлахыг дахин оролдох'
                : 'Захиалга цуцлах',
            () => confirmCancellation(value),
          ),
      ],
    ),
  );
  Widget history() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text('Өмнөх болон удахгүй болох захиалгаа эндээс харна уу.'),
      if (flow.reschedule?.uncertain == true)
        button(
          'Хүлээгдэж буй цаг шилжүүлэлтийг шалгах',
          () => openReschedule(flow.reschedule!.source, resume: true),
        ),
      const SizedBox(height: 16),
      if (flow.history != null) ...[
        Text('Хуудас ${flow.history!.page}'),
        if (flow.history!.items.isEmpty) empty('Энэ хуудсанд захиалга алга.'),
        for (final a in flow.history!.items) historyAppointment(a),
        button(
          'Өмнөх хуудас',
          () => loadHistoryPage(flow.history!.page - 1),
          enabled: flow.history!.page > 1,
        ),
        button(
          'Дараагийн хуудас',
          () => loadHistoryPage(flow.history!.page + 1),
          enabled:
              flow.history!.items.length == flow.history!.pageSize &&
              flow.history!.page < 100000,
        ),
      ],
      button(
        'Хуудсыг шинэчлэх',
        () => loadHistoryPage(flow.history?.page ?? 1),
        secondary: true,
      ),
      button(
        'Захиалгын алхам руу буцах',
        () => setState(() {
          showHistory = false;
          reviewBookingId = null;
        }),
      ),
    ],
  );
  String get stepHint => const [
    'Үйлчлүүлэх эмнэлгээ сонгоод үргэлжлүүлнэ үү.',
    'Бүртгэлээрээ нэвтрэх эсвэл шинээр бүртгүүлнэ үү.',
    'Нэвтэрсэн бол өөрийн профайлыг авна уу. Шинэ бүртгэлтэй бол кодоо оруулна уу.',
    'Өөрийн мэдээллээ шалгаад үйлчилгээ сонгоно уу.',
    'Авах үйлчилгээгээ сонгоод эмч сонгох товчийг дарна уу.',
    'Эмчээ сонгоод боломжит огноо, цагийг харна уу.',
    'Огноогоо шалгаж, боломжит цагаас сонгоно уу.',
    'Мэдээллээ хянаад захиалгаа баталгаажуулна уу.',
  ][flow.step];

  Widget home() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const ClinicalPageIntro(
        eyebrow: 'Дархан-Уул • Suvd Dent',
        title: 'Сайн байна уу!',
        description: 'Таны инээмсэглэл бидний урам зориг.',
      ),
      if (widget.settings.isAcademic) ...[
        const SizedBox(height: 10),
        const Text(
          'Сургалтын хувилбар',
          style: TextStyle(color: AppColors.blue, fontWeight: FontWeight.w700),
        ),
      ],
      const SizedBox(height: 25),
      ClinicalHero(onBook: goToBooking, enabled: !flow.busy),
      const SizedBox(height: 26),
      Text('Үйлчилгээнүүд', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 16),
      Row(
        children: [
          ClinicalFeatureTile(
            icon: Icons.medical_services_outlined,
            label: 'Үзлэг',
            onTap: () => openEducationalInfo('Шүдний үзлэг'),
          ),
          const SizedBox(width: 8),
          ClinicalFeatureTile(
            icon: Icons.auto_awesome_outlined,
            label: 'Цэвэрлэгээ',
            onTap: () => openEducationalInfo('Шүдний чулуу цэвэрлэгээ'),
          ),
          const SizedBox(width: 8),
          ClinicalFeatureTile(
            icon: Icons.favorite_border,
            label: 'Эмчилгээ',
            onTap: () => openEducationalInfo('Цоорлын эмчилгээ'),
          ),
        ],
      ),
      const SizedBox(height: 14),
      OutlinedButton.icon(
        onPressed: () => showModalBottomSheet<void>(
          context: context,
          showDragHandle: true,
          isScrollControlled: true,
          backgroundColor: AppColors.paper,
          builder: (sheetContext) => SafeArea(
            child: FractionallySizedBox(
              heightFactor: 0.8,
              child: ListView(
                padding: const EdgeInsets.all(22),
                children: [
                  Text(
                    'Шүдний эмчилгээний мэдээлэл',
                    style: Theme.of(sheetContext).textTheme.titleLarge,
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'Доорх нь ерөнхий танилцуулга. Үйлчилгээг захиалах '
                    'боломж эмнэлгийн бодит каталогоос хамаарна.',
                    style: TextStyle(color: AppColors.muted),
                  ),
                  const SizedBox(height: 18),
                  for (final topic in DentalKnowledge.overview)
                    ClinicalChoiceCard(
                      label: topic.name,
                      subtitle: topic.summary,
                      selected: false,
                      onTap: () {
                        Navigator.pop(sheetContext);
                        openEducationalInfo(topic.name);
                      },
                      icon: Icons.book_outlined,
                    ),
                ],
              ),
            ),
          ),
        ),
        icon: const Icon(Icons.menu_book_outlined),
        label: const Text('Бүх төрлийн эмчилгээний тайлбар'),
      ),
      const SizedBox(height: 26),
      Text('Таны дараагийн цаг', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 16),
      ClinicalInfoPanel(
        title: flow.appointment?.status == 'Confirmed'
            ? 'Таны захиалга баталгаажсан'
            : 'Одоогоор идэвхтэй захиалга харагдахгүй',
        children: [
          Text(
            flow.session == null
                ? 'Цаг захиалахын тулд эхлээд нэвтэрнэ үү.'
                : flow.appointment?.status != 'Confirmed'
                ? 'Миний цаг хэсгээс захиалгаа шалгана уу.'
                : local(
                    flow.appointment!.startUtc,
                    flow.appointment!.clinicTimeZoneId,
                  ),
            style: const TextStyle(color: AppColors.muted),
          ),
        ],
      ),
      const SizedBox(height: 24),
      Text(
        widget.settings.isAcademic
            ? 'Үйлчилгээ, эмч, цагийн мэдээлэл нь сургалтын жишээ.'
            : 'Үйлчилгээ, эмч, цагийн мэдээллийг эмнэлгийн LIVE системээс авна.',
        style: const TextStyle(fontSize: 12, color: AppColors.muted),
      ),
    ],
  );

  Widget bookingReview() {
    if (flow.slot == null ||
        flow.service == null ||
        flow.doctor == null ||
        flow.availability == null ||
        flow.appointment != null ||
        flow.conflict) {
      return confirmation();
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ClinicalInfoPanel(
          title: 'Захиалгын дэлгэрэнгүй',
          children: [
            ClinicalDetail('Үйлчилгээ', LiveLabels.serviceName(flow.service!)),
            const Divider(),
            ClinicalDetail('Эмч', LiveLabels.doctorName(flow.doctor!)),
            const Divider(),
            ClinicalDetail('Өдөр', LiveLabels.date(flow.availability!.date)),
            const Divider(),
            ClinicalDetail(
              'Цаг',
              LiveLabels.time(
                flow.slot!.startUtc,
                flow.availability!.timeZoneId,
              ),
            ),
            const Divider(),
            const ClinicalDetail('Эмнэлэг', 'Дархан-Уул • Suvd Dent'),
          ],
        ),
        const SizedBox(height: 20),
        const Card(
          color: AppColors.sky,
          child: Padding(
            padding: EdgeInsets.all(15),
            child: Text(
              'Захиалгын цаг болон таны эрхийг сервер дахин шалгана.',
              style: TextStyle(color: AppColors.blue, fontSize: 12),
            ),
          ),
        ),
        if (flow.uncertainBooking)
          const Text(
            'Өмнөх хүсэлтийн үр дүн тодорхойгүй. Ижил хүсэлтээр дахин шалгана уу.',
          ),
        const SizedBox(height: 18),
        button(
          flow.uncertainBooking
              ? 'Ижил хүсэлтээр дахин оролдох'
              : 'Захиалга баталгаажуулах',
          flow.book,
        ),
      ],
    );
  }

  Widget screen() => switch (flow.step) {
    0 => home(),
    1 => auth(),
    2 => verification(),
    3 => profile(),
    4 => services(),
    5 => doctors(),
    6 => slots(),
    _ => bookingReview(),
  };
  void openHistory() {
    if (flow.busy || flow.session == null) return;
    setState(() {
      reviewBookingId = null;
      showHistory = true;
      showDashboard = false;
    });
    flow.loadAppointments(1);
  }

  void goToBooking() {
    if (flow.busy || flow.uncertainBooking) return;
    setState(() {
      showHistory = false;
      showDashboard = false;
    });
    if (flow.session == null) {
      if (flow.step == 0) flow.enterClinic();
    } else if (flow.step < 3) {
      flow.loadProfile();
    } else if (flow.step == 3) {
      flow.loadServices();
    } else if (flow.appointment != null) {
      flow.newBooking();
    } else if (flow.step > 4) {
      flow.loadServices();
    }
  }

  Widget pageContent() {
    if (showHistory) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const ClinicalPageIntro(
            eyebrow: 'Өмнөх болон удахгүй болох захиалгууд',
            title: 'Миний захиалгууд',
            titleKey: ValueKey('screen-title'),
          ),
          const SizedBox(height: 25),
          // The history view must report cancellation/loading feedback too.
          // Previously this early-return path skipped the shared booking
          // feedback shown by other steps.
          if (flow.busy) ...[
            const LinearProgressIndicator(semanticsLabel: 'Ачаалж байна'),
            const SizedBox(height: 10),
            const Text('Мэдээллийг ачаалж байна…'),
          ],
          if (flow.error != null)
            Semantics(
              liveRegion: true,
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 16),
                child: Text(
                  flow.error!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              ),
            ),
          if (flow.notice != null)
            Semantics(liveRegion: true, child: Text(flow.notice!)),
          history(),
        ],
      );
    }
    if (showDashboard || flow.step == 0) return home();
    const bookingTitles = {
      4: 'Танд ямар үйлчилгээ хэрэгтэй вэ?',
      5: 'Эмч сонгох',
      6: 'Өдөр, цаг сонгох',
      7: 'Таны захиалгын мэдээлэл',
    };
    const bookingEyebrows = {
      4: 'АЛХАМ 01 / 04    •    Үйлчилгээ',
      5: 'АЛХАМ 02 / 04    •    Эмч',
      6: 'АЛХАМ 03 / 04    •    Боломжит цаг',
      7: 'АЛХАМ 04 / 04    •    Баталгаажуулалт',
    };
    final isBooking = flow.step >= 4;
    final title = selectingVerificationMethod
        ? methodTitle
        : flow.step == 2 && flow.session != null
        ? 'Нэвтрэлт амжилттай'
        : titles[flow.step];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ClinicalPageIntro(
          eyebrow:
              bookingEyebrows[flow.step] ??
              (flow.step == 3 ? 'Миний бүртгэл' : 'Suvd Dent · Бүртгэл'),
          title: bookingTitles[flow.step] ?? title,
          titleKey: const ValueKey('screen-title'),
          description: isBooking
              ? null
              : selectingVerificationMethod
              ? '6 оронтой код авах сувгаа сонгоорой.'
              : flow.step == 2 && flow.session == null
              ? null
              : stepHint,
          progress: isBooking ? (flow.step - 3) / 4 : null,
        ),
        if (isBooking && (flow.service != null || flow.doctor != null)) ...[
          const SizedBox(height: 18),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if (flow.service != null)
                    Text(
                      'Сонгосон үйлчилгээ: ${LiveLabels.serviceName(flow.service!)}',
                    ),
                  if (flow.doctor != null)
                    Text(
                      'Сонгосон эмч: ${LiveLabels.doctorName(flow.doctor!)}',
                    ),
                ],
              ),
            ),
          ),
        ],
        const SizedBox(height: 24),
        if (flow.busy) ...[
          const LinearProgressIndicator(semanticsLabel: 'Ачаалж байна'),
          const SizedBox(height: 10),
          const Text('Мэдээллийг ачаалж байна…'),
        ],
        if (flow.error != null)
          Semantics(
            liveRegion: true,
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 16),
              child: Text(
                flow.error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          ),
        if (flow.notice != null)
          Semantics(liveRegion: true, child: Text(flow.notice!)),
        if (flow.step == 1 || (flow.step == 2 && flow.session == null))
          TweenAnimationBuilder<double>(
            key: ValueKey((flow.step, selectingVerificationMethod)),
            duration: clinicalMotion(context, 180),
            tween: Tween(begin: .88, end: 1),
            builder: (context, opacity, child) =>
                Opacity(opacity: opacity, child: child),
            child: selectingVerificationMethod ? methodSelection() : screen(),
          )
        else
          screen(),
      ],
    );
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !showHistory && flow.step == 0,
    onPopInvokedWithResult: (didPop, result) {
      if (!didPop) {
        if (showHistory) {
          setState(() => showHistory = false);
        } else {
          backFromVerification();
        }
      }
    },
    child: Scaffold(
      appBar: ClinicalHeader(
        onBack: showHistory
            ? () => setState(() => showHistory = false)
            : flow.step > 0 &&
                  !flow.busy &&
                  !flow.uncertainBooking &&
                  flow.appointment == null
            ? backFromVerification
            : null,
        actions: [
          if (flow.session != null)
            IconButton(
              tooltip: 'Мэдэгдлийн тохиргоо',
              onPressed: flow.busy ? null : openNotificationSettings,
              icon: const Icon(Icons.notifications_outlined),
            ),
          if (flow.session != null)
            IconButton(
              tooltip: 'Миний захиалгууд',
              onPressed: flow.busy ? null : openHistory,
              icon: const Icon(Icons.event_note_outlined),
            ),
          if (flow.step > 0)
            IconButton(
              tooltip: 'Гарах / Эмнэлэг дахин сонгох',
              onPressed: flow.busy ? null : flow.logout,
              icon: const Icon(Icons.logout),
            ),
        ],
      ),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 760),
            child: ListView(
              controller: historyScroll,
              key: ValueKey((flow.step, showHistory)),
              padding: EdgeInsets.fromLTRB(
                MediaQuery.sizeOf(context).width < 400 ? 16 : 24,
                24,
                MediaQuery.sizeOf(context).width < 400 ? 16 : 24,
                30,
              ),
              children: [pageContent()],
            ),
          ),
        ),
      ),
      bottomNavigationBar: ClinicalNavigation(
        activeIndex: showHistory
            ? 2
            : showDashboard || flow.step == 0
            ? 0
            : 1,
        onHome: flow.busy || flow.uncertainBooking || flow.appointment != null
            ? null
            : () {
                if (flow.session == null) {
                  flow.logout();
                } else {
                  setState(() {
                    showHistory = false;
                    showDashboard = true;
                  });
                }
              },
        onBook: goToBooking,
        onAppointments: flow.session == null ? null : openHistory,
        onProfile: flow.session == null || flow.busy
            ? null
            : () {
                setState(() {
                  showHistory = false;
                  showDashboard = false;
                });
                flow.loadProfile();
              },
      ),
    ),
  );
}

class LiveConfigurationError extends StatelessWidget {
  final String message;
  const LiveConfigurationError(this.message, {super.key});
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('NEXORA DENTAL · LIVE')),
    body: Center(
      child: Padding(padding: const EdgeInsets.all(24), child: Text(message)),
    ),
  );
}
