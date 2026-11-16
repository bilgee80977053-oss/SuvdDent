import 'package:flutter/material.dart';

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

class LivePatientFlow extends StatefulWidget {
  final ApiSettings settings;
  final DentalApi? api;
  const LivePatientFlow({super.key, required this.settings, this.api});
  @override
  State<LivePatientFlow> createState() => _LivePatientFlowState();
}

class _LivePatientFlowState extends State<LivePatientFlow> {
  late final LiveFlowController flow;
  final historyScroll = ScrollController();
  final identifier = TextEditingController(),
      password = TextEditingController(),
      name = TextEditingController(),
      email = TextEditingController(),
      phone = TextEditingController(),
      code = TextEditingController();
  final authKey = GlobalKey<FormState>(),
      profileKey = GlobalKey<FormState>(),
      verifyKey = GlobalKey<FormState>();
  bool registering = false, showHistory = false, showDashboard = false;
  static const titles = [
    'Эмнэлэг сонгох',
    'Нэвтрэх / Бүртгүүлэх',
    'Имэйл баталгаажуулалт',
    'Өвчтөний профайл',
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
    if (oldWidget.settings.baseUrl != widget.settings.baseUrl ||
        oldWidget.settings.clinicId != widget.settings.clinicId ||
        oldWidget.api != widget.api) {
      flow.changeClinic(widget.api ?? DentalApi(widget.settings));
    }
  }

  void onChange() {
    if (flow.session == null && flow.step == 0) {
      for (final field in [identifier, password, name, email, phone, code]) {
        field.clear();
      }
      showHistory = false;
      showDashboard = false;
    }
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
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

  Widget auth() => Form(
    key: authKey,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SegmentedButton<bool>(
          segments: const [
            ButtonSegment(value: false, label: Text('Нэвтрэх')),
            ButtonSegment(value: true, label: Text('Бүртгүүлэх')),
          ],
          selected: {registering},
          onSelectionChanged: flow.busy
              ? null
              : (v) => setState(() {
                  registering = v.first;
                  password.clear();
                }),
        ),
        const SizedBox(height: 20),
        if (registering) ...[
          field(name, 'Овог нэр'),
          field(
            email,
            'Имэйл',
            keyboard: TextInputType.emailAddress,
            validate: (v) =>
                RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$').hasMatch(v.trim())
                ? null
                : 'Имэйл хаягаа шалгана уу.',
          ),
          field(
            phone,
            'Утас (заавал биш)',
            optional: true,
            keyboard: TextInputType.phone,
            validate: (v) => v.isEmpty || RegExp(r'^\d{8,}$').hasMatch(v)
                ? null
                : 'Утасны дугаараа шалгана уу.',
          ),
        ] else
          field(identifier, 'Имэйл эсвэл утас'),
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
            'Бодит имэйлээр ирсэн 6 оронтой код шаардлагатай. Имэйл үйлчилгээ ажиллахгүй бол бүртгэл баталгаажихгүй.',
          ),
        button(registering ? 'Бүртгэл үүсгэх' : 'Нэвтрэх', () async {
          if (!authKey.currentState!.validate()) return;
          final secret = password.text;
          if (registering) {
            await flow.register(
              name.text.trim(),
              email.text.trim(),
              secret,
              phone.text.isEmpty ? null : phone.text.trim(),
            );
          } else {
            await flow.login(identifier.text.trim(), secret);
          }
          password.clear();
        }),
        if (flow.registration != null)
          button('Ирсэн кодыг оруулах', () {
            flow.step = 2;
            flow.changed();
          }),
      ],
    ),
  );
  Widget verification() {
    if (flow.session != null) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text(
            'Нэвтрэлт амжилттай. Имэйлээ баталгаажуулсан бүртгэлээр цаг захиална уу.',
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
    return Form(
      key: verifyKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text('Имэйлд ирсэн кодоо оруулна уу. Код 120 секунд хүчинтэй.'),
          field(
            code,
            '6 оронтой код',
            secret: true,
            keyboard: TextInputType.number,
            validate: (v) => RegExp(r'^\d{6}$').hasMatch(v)
                ? null
                : '6 оронтой код оруулна уу.',
          ),
          button('Код шалгах', () async {
            if (verifyKey.currentState!.validate()) {
              await flow.verify(code.text);
              code.clear();
              if (mounted && flow.registration == null && flow.step == 1) {
                setState(() => registering = false);
              }
            }
          }),
        ],
      ),
    );
  }

  void openNotificationSettings() {
    if (flow.busy || flow.session == null) return;
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => NotificationSettingsScreen(flow: flow),
      ),
    );
  }

  Widget profile() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (flow.patient != null) ...[
        Text(
          flow.patient!.fullName,
          style: Theme.of(context).textTheme.titleLarge,
        ),
        Text(flow.patient!.phone),
        if (flow.patient!.dateOfBirth != null)
          Text('Төрсөн огноо: ${flow.patient!.dateOfBirth}'),
        if (flow.patient!.address != null) Text(flow.patient!.address!),
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
    ClinicalServiceDetails.show(context, category: category);
  }

  void openServiceInfo(BookingService s) {
    if (flow.busy || flow.uncertainBooking) return;
    ClinicalServiceDetails.show(
      context,
      category: s.name,
      service: s,
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
        ),
      ),
    );
  }

  Widget services() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text(
        'Энд зөвхөн сонгосон эмнэлгийн LIVE каталогоос ирсэн '
        'үйлчилгээнүүд харагдана. Карт дээр дарж үнэ болон '
        'эмчилгээний талаар дэлгэрэнгүй уншаарай.',
        style: TextStyle(color: AppColors.muted, fontSize: 13),
      ),
      const SizedBox(height: 18),
      if (flow.services.isEmpty) empty('Эмнэлгийн үйлчилгээ олдсонгүй.'),
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
        button('Сонгосон үйлчилгээгээр үргэлжлүүлэх',
            flow.loadDoctors),
      button('Үйлчилгээг шинэчлэх', flow.loadServices, secondary: true),
    ],
  );

  Widget doctors() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (!flow.busy && flow.doctors.isEmpty)
        empty('Энэ үйлчилгээнд эмч олдсонгүй.'),
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
      button('Огноо, цаг сонгох', flow.openSlots, enabled: flow.doctor != null),
      button('Эмч нарыг шинэчлэх', flow.loadDoctors, secondary: true),
    ],
  );
  Widget slots() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(
        LiveLabels.date(flow.availability?.date ?? flow.date),
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      const Text(
        'Хуанлиас өдөр сонгоод боломжит цагийг шалгана уу. '
        'Эцсийн боломжийг сервер баталгаажуулна.',
      ),
      const SizedBox(height: 16),
      ClinicalCalendarPicker(
        selectedDate: DateTime.parse(flow.date),
        enabled: !flow.busy && !flow.uncertainBooking,
        onSelected: (value) => flow.changeDate(calendarDate(value)),
      ),
      const SizedBox(height: 12),
      button('Өөр огноо сонгох', () async {
        final result = await showMongolianDatePicker(
          context,
          flow.date,
          timeZoneId: flow.availability?.timeZoneId,
        );
        if (result != null && mounted) flow.changeDate(calendarDate(result));
      }, enabled: !flow.uncertainBooking),
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
        const Text(
          'Захиалга амжилттай үүслээ.',
          style: TextStyle(fontWeight: FontWeight.bold),
        ),
        details(flow.appointment!),
        button(
          'Төлбөрийн дэлгэрэнгүй',
          () => openPayment(flow.appointment!),
        ),
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

  Widget historyAppointment(Appointment value) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      details(value),
      button(
        'Төлбөр / үнийн мэдээлэл',
        () => openPayment(value),
        secondary: true,
      ),
      if (flow.uncertainCancellations.contains(value.id)) ...[
        const Text('Цуцлалтын үр дүнг шалгана уу.'),
        button('Төлөв шалгах', () => flow.reconcileCancellation(value.id)),
      ],
      if (flow.canCancel(value))
        button(
          flow.uncertainCancellations.contains(value.id)
              ? 'Цуцлахыг дахин оролдох'
              : 'Захиалга цуцлах',
          () => confirmCancellation(value),
        ),
    ],
  );
  Widget history() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text('Өмнөх болон удахгүй болох захиалгаа эндээс харна уу.'),
      const SizedBox(height: 16),
      if (flow.history != null) ...[
        Text('Хуудас ${flow.history!.page}'),
        if (flow.history!.items.isEmpty) empty('Энэ хуудсанд захиалга алга.'),
        for (final a in flow.history!.items) historyAppointment(a),
        button(
          'Өмнөх хуудас',
          () => flow.loadAppointments(flow.history!.page - 1),
          enabled: flow.history!.page > 1,
        ),
        button(
          'Дараагийн хуудас',
          () => flow.loadAppointments(flow.history!.page + 1),
          enabled:
              flow.history!.items.length == flow.history!.pageSize &&
              flow.history!.page < 100000,
        ),
      ],
      button(
        'Хуудсыг шинэчлэх',
        () => flow.loadAppointments(flow.history?.page ?? 1),
        secondary: true,
      ),
      button(
        'Захиалгын алхам руу буцах',
        () => setState(() => showHistory = false),
      ),
    ],
  );
  String get stepHint => const [
    'Үйлчлүүлэх эмнэлгээ сонгоод үргэлжлүүлнэ үү.',
    'Бүртгэлээрээ нэвтрэх эсвэл шинээр бүртгүүлнэ үү.',
    'Нэвтэрсэн бол өөрийн профайлыг авна уу. Шинэ бүртгэлтэй бол имэйлээ баталгаажуулна уу.',
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
                  Text('Шүдний эмчилгээний мэдээлэл',
                    style: Theme.of(sheetContext).textTheme.titleLarge),
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
      Text('Таны дараагийн цаг',
          style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 16),
      ClinicalInfoPanel(
        title: flow.appointment == null
            ? 'Одоогоор захиалга харагдахгүй'
            : 'Таны захиалга баталгаажсан',
        children: [
          Text(
            flow.session == null
                ? 'Цаг захиалахын тулд эхлээд нэвтэрнэ үү.'
                : flow.appointment == null
                    ? 'Миний цаг хэсгээс захиалгаа шалгана уу.'
                    : local(flow.appointment!.startUtc,
                        flow.appointment!.clinicTimeZoneId),
            style: const TextStyle(color: AppColors.muted),
          ),
        ],
      ),
      const SizedBox(height: 24),
      const Text(
        'Үйлчилгээ, эмч, цагийн мэдээллийг эмнэлгийн LIVE системээс авна.',
        style: TextStyle(fontSize: 12, color: AppColors.muted),
      ),
    ],
  );

  Widget bookingReview() {
    if (flow.slot == null || flow.service == null ||
        flow.doctor == null || flow.availability == null ||
        flow.appointment != null || flow.conflict) {
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
                flow.slot!.startUtc, flow.availability!.timeZoneId,
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
      4: 'Танд ямар үйлчилгээ\nхэрэгтэй вэ?',
      5: 'Танд санал болгох эмч\nнар',
      6: 'Та өөрт тохирох өдрөө\nсонгоорой',
      7: 'Таны захиалгын\nмэдээлэл',
    };
    const bookingEyebrows = {
      4: 'АЛХАМ 01 / 04    •    Үйлчилгээ',
      5: 'АЛХАМ 02 / 04    •    Эмч',
      6: 'АЛХАМ 03 / 04    •    Боломжит цаг',
      7: 'АЛХАМ 04 / 04    •    Баталгаажуулалт',
    };
    final isBooking = flow.step >= 4;
    final title = flow.step == 2 && flow.session != null
        ? 'Нэвтрэлт амжилттай'
        : titles[flow.step];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ClinicalPageIntro(
          eyebrow: bookingEyebrows[flow.step] ??
              'NEXORA DENTAL  •  НЭВТРЭЛТ',
          title: bookingTitles[flow.step] ?? title,
          titleKey: const ValueKey('screen-title'),
          description: isBooking ? null : stepHint,
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
          flow.back();
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
                ? flow.back
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
        activeIndex: showHistory ? 2 : showDashboard || flow.step == 0 ? 0 : 1,
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
