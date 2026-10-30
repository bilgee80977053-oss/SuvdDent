import 'package:flutter/material.dart';

import 'dental_api.dart';
import 'live_flow_controller.dart';
import 'models.dart';
import 'live_presentation.dart';

class LivePatientFlow extends StatefulWidget {
  final ApiSettings settings;
  final DentalApi? api;
  const LivePatientFlow({super.key, required this.settings, this.api});
  @override
  State<LivePatientFlow> createState() => _LivePatientFlowState();
}

class _LivePatientFlowState extends State<LivePatientFlow> {
  late final LiveFlowController flow;
  final identifier = TextEditingController(),
      password = TextEditingController(),
      name = TextEditingController(),
      email = TextEditingController(),
      phone = TextEditingController(),
      code = TextEditingController();
  final authKey = GlobalKey<FormState>(),
      profileKey = GlobalKey<FormState>(),
      verifyKey = GlobalKey<FormState>();
  bool registering = false, showHistory = false;
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
    }
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    flow.removeListener(onChange);
    flow.dispose();
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
  Widget button(String label, VoidCallback action, {bool enabled = true}) =>
      Padding(
        padding: const EdgeInsets.symmetric(vertical: 6),
        child: FilledButton(
          style: FilledButton.styleFrom(minimumSize: const Size(48, 48)),
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
  ) => Card(
    child: ListTile(
      selected: selected,
      title: Text(label),
      subtitle: Text(subtitle),
      trailing: Icon(
        selected ? Icons.radio_button_checked : Icons.radio_button_off,
      ),
      enabled: !flow.busy && !flow.uncertainBooking,
      onTap: action,
    ),
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
      button('Профайлыг шинэчлэх', flow.loadProfile),
    ],
  );
  Widget services() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (flow.services.isEmpty) empty('Үйлчилгээ олдсонгүй.'),
      for (final s in flow.services)
        choice(
          LiveLabels.serviceName(s),
          '${LiveLabels.serviceDescription(s)}\n${s.minPrice} – ${s.maxPrice ?? s.minPrice} ₮',
          flow.service?.id == s.id,
          () => flow.selectService(s),
        ),
      button('Эмч сонгох', flow.loadDoctors, enabled: flow.service != null),
      button('Үйлчилгээг шинэчлэх', flow.loadServices),
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
      button('Эмч нарыг шинэчлэх', flow.loadDoctors),
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
        'Өнөөдрөөс дараагийн 30 хоногийн өдрөө сонгоод боломжит цагийг шинэчилнэ үү. Хүчинтэй хугацааг сервер шалгана.',
      ),
      button('Огноо сонгох', () async {
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
        if (flow.slot != null) ...[
          const SizedBox(height: 16),
          Semantics(
            liveRegion: true,
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Text(
                  'Сонгосон цаг: ${LiveLabels.interval(flow.slot!, flow.availability!.timeZoneId)}\nҮргэлжлэх хугацаа: ${LiveLabels.duration(flow.slot!)}',
                ),
              ),
            ),
          ),
        ],
      ] else if (!flow.busy && flow.error == null)
        empty('Боломжит цагийг харахын тулд шинэчлэх товчийг дарна уу.'),
      button('Захиалгыг хянах', flow.review, enabled: flow.slot != null),
    ],
  );
  Widget details(Appointment a) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            LiveLabels.status(a.status),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          Text('Захиалгын дугаар: №${a.id}'),
          Text('Эхлэх: ${local(a.startUtc, a.clinicTimeZoneId)}'),
          Text('Дуусах: ${local(a.endUtc, a.clinicTimeZoneId)}'),
        ],
      ),
    ),
  );
  Widget confirmation() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (flow.appointment != null) ...[
        const Text(
          'Захиалга амжилттай үүслээ.',
          style: TextStyle(fontWeight: FontWeight.bold),
        ),
        details(flow.appointment!),
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
  Widget history() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(
        'Миний захиалгууд',
        style: Theme.of(context).textTheme.headlineSmall,
      ),
      if (flow.history != null) ...[
        Text('Хуудас ${flow.history!.page}'),
        if (flow.history!.items.isEmpty) empty('Энэ хуудсанд захиалга алга.'),
        for (final a in flow.history!.items) details(a),
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
      ),
      button(
        'Захиалгын алхам руу буцах',
        () => setState(() => showHistory = false),
      ),
    ],
  );
  Widget screen() => switch (flow.step) {
    0 => Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Text(
          'Тохируулсан туршилтын эмнэлэг. Энэ нь олон нийтэд нээлттэй эмнэлгийн жагсаалт биш.',
        ),
        const Text('Сонгосон туршилтын эмнэлэг'),
        button('Энэ эмнэлгээр үргэлжлүүлэх', flow.enterClinic),
      ],
    ),
    1 => auth(),
    2 => verification(),
    3 => profile(),
    4 => services(),
    5 => doctors(),
    6 => slots(),
    _ => confirmation(),
  };
  @override
  Widget build(BuildContext context) => PopScope(
    canPop: flow.step == 0,
    onPopInvokedWithResult: (didPop, result) {
      if (!didPop) flow.back();
    },
    child: Scaffold(
      appBar: AppBar(
        title: const Text('NEXORA DENTAL'),
        leading: flow.step > 0
            ? IconButton(
                tooltip: 'Буцах',
                onPressed:
                    !flow.busy &&
                        !flow.uncertainBooking &&
                        flow.appointment == null
                    ? flow.back
                    : null,
                icon: const Icon(Icons.arrow_back),
              )
            : null,
        actions: [
          if (flow.session != null)
            IconButton(
              tooltip: 'Миний захиалгууд',
              onPressed: flow.busy
                  ? null
                  : () {
                      setState(() => showHistory = true);
                      flow.loadAppointments(1);
                    },
              icon: const Icon(Icons.event_note),
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
              key: ValueKey((flow.step, showHistory)),
              padding: const EdgeInsets.all(24),
              children: [
                Container(
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(
                    color: Theme.of(context).colorScheme.primaryContainer,
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: const Text(
                    'Онлайн захиалга · Туршилтын эмнэлэг',
                    style: TextStyle(fontWeight: FontWeight.bold),
                  ),
                ),
                const SizedBox(height: 12),
                const Text(
                  'Нэвтрэлт зөвхөн санах ойд хадгалагдана. Апп дахин эхлэхэд дахин нэвтэрнэ.',
                ),
                const SizedBox(height: 20),
                Text('АЛХАМ ${flow.step + 1} / 8'),
                LinearProgressIndicator(value: (flow.step + 1) / 8),
                const SizedBox(height: 20),
                Text(
                  titles[flow.step],
                  style: Theme.of(context).textTheme.headlineMedium,
                ),
                const SizedBox(height: 20),
                if (flow.busy)
                  const Column(
                    children: [
                      LinearProgressIndicator(semanticsLabel: 'Ачаалж байна'),
                      Text('Мэдээллийг ачаалж байна…'),
                    ],
                  ),
                if (flow.error != null)
                  Semantics(
                    liveRegion: true,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 16),
                      child: Text(
                        flow.error!,
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                    ),
                  ),
                if (flow.notice != null)
                  Semantics(
                    liveRegion: true,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 16),
                      child: Text(flow.notice!),
                    ),
                  ),
                if (showHistory && flow.session != null)
                  history()
                else
                  screen(),
              ],
            ),
          ),
        ),
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
