import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:timezone/timezone.dart' as tz;

import '../../app/widgets.dart';
import '../../core/models.dart';
import '../auth/session.dart';
import 'booking_controller.dart';
import 'clinic_time.dart';

class BookingData {
  final Clinic clinic;
  final Doctor doctor;
  final DentalService service;
  final BookingOptions options;
  const BookingData(this.clinic, this.doctor, this.service, this.options);
}

class BookingScreen extends ConsumerStatefulWidget {
  final int clinicId, doctorId, serviceId;
  const BookingScreen(
    this.clinicId,
    this.doctorId,
    this.serviceId, {
    super.key,
  });
  @override
  ConsumerState<BookingScreen> createState() => _BookingScreenState();
}

class _BookingScreenState extends ConsumerState<BookingScreen> {
  late final BookingController controller;
  late Future<BookingData> data;
  DateTime? day;
  TimeOfDay? time;
  bool consent = false;
  String? validation;
  @override
  void initState() {
    super.initState();
    controller = BookingController(
      ref.read(apiProvider),
      ref.read(storeProvider),
      ref.read(sessionProvider).asData!.value!.id,
    );
    data = load();
  }

  Future<BookingData> load() async {
    await controller.restore();
    final api = ref.read(apiProvider);
    final values = await Future.wait<dynamic>([
      api.options(widget.clinicId, widget.doctorId, widget.serviceId),
      api.clinics(),
      api.doctors(widget.clinicId),
      api.services(widget.clinicId, widget.doctorId),
    ]);
    final options = values[0] as BookingOptions;
    final clinic = (values[1] as List<Clinic>).firstWhere(
      (v) => v.id == widget.clinicId,
    );
    final doctor = (values[2] as List<Doctor>).firstWhere(
      (v) => v.id == widget.doctorId,
    );
    final service = (values[3] as List<DentalService>).firstWhere(
      (v) => v.id == widget.serviceId,
    );
    final location = tz.getLocation(options.clinicTimeZoneId);
    if (controller.pending != null) {
      final local = tz.TZDateTime.from(controller.pending!.startUtc, location);
      day = DateTime(local.year, local.month, local.day);
      time = TimeOfDay(hour: local.hour, minute: local.minute);
    } else if (day == null) {
      final now = tz.TZDateTime.now(location);
      var next = DateTime(
        now.year,
        now.month,
        now.day,
      ).add(const Duration(days: 1));
      for (var i = 0; i < 7; i++, next = next.add(const Duration(days: 1))) {
        final windows =
            options.workingHours
                .where((w) => w.dayOfWeek == next.weekday % 7)
                .toList()
              ..sort((a, b) => a.startMinute.compareTo(b.startMinute));
        if (windows.isNotEmpty) {
          day = next;
          final minute =
              windows.first.startMinute + options.bufferBeforeMinutes;
          time = TimeOfDay(hour: minute ~/ 60, minute: minute % 60);
          break;
        }
      }
    }
    controller.needsRefresh = false;
    return BookingData(clinic, doctor, service, options);
  }

  void refresh() => setState(() {
    validation = null;
    data = load();
  });
  Future<void> submit(BookingData info) async {
    if (controller.submitting) {
      return;
    }
    DateTime start;
    if (controller.pending != null) {
      start = controller.pending!.startUtc;
    } else {
      if (day == null || time == null) {
        setState(() => validation = 'Өдөр, цагаа сонгоно уу.');
        return;
      }
      try {
        start = clinicLocalToUtc(
          info.options.clinicTimeZoneId,
          day!,
          time!.hour,
          time!.minute,
        );
      } catch (_) {
        setState(
          () => validation = 'Сонгосон өдөр, цагийг хөрвүүлэх боломжгүй байна.',
        );
        return;
      }
      final now = DateTime.now().toUtc();
      if (!start.isAfter(now) ||
          start.isAfter(now.add(const Duration(days: 365)))) {
        setState(
          () => validation = 'Ирээдүйн 365 хоногийн дотор цаг сонгоно уу.',
        );
        return;
      }
      if (!withinWorkingHours(info.options, start)) {
        setState(
          () => validation =
              'Сонгосон цаг эмчийн ажлын хуваариас гадуур байна.',
        );
        return;
      }
      if (controller.consentRequired && !consent) {
        setState(() => validation = 'Өвчтөний бүртгэл үүсгэх зөвшөөрлөө өгнө үү.');
        return;
      }
    }
    setState(() => validation = null);
    final result = await controller.submit(info.options, start, consent);
    if (mounted && result != null) {
      context.go('/appointments/${result.id}/confirmation');
    }
  }

  @override
  void dispose() {
    controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AppPage(
    title: 'Цаг захиалах',
    child: FutureBuilder<BookingData>(
      future: data,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snapshot.hasError) {
          return ErrorPanel(error: snapshot.error, retry: refresh);
        }
        final info = snapshot.requireData;
        return ListenableBuilder(
          listenable: controller,
          builder: (context, _) {
            final pending = controller.pending;
            if (pending != null &&
                (pending.clinicId != widget.clinicId ||
                    pending.doctorId != widget.doctorId ||
                    pending.serviceId != widget.serviceId)) {
              return Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    const InfoBanner(
                      'Өмнөх захиалгын хүсэлт шийдэгдээгүй байна.',
                    ),
                    FilledButton(
                      onPressed: () => context.go(pending.route),
                      child: const Text('Өмнөх хүсэлт рүү очих'),
                    ),
                  ],
                ),
              );
            }
            final locked = pending != null || controller.submitting;
            return ListView(
              padding: const EdgeInsets.all(20),
              children: [
                Text(
                  info.clinic.name,
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 8),
                Text(info.doctor.fullName),
                Text(
                  '${info.service.name} · ${info.options.durationMinutes} минут',
                ),
                Text(money(info.service.minPrice, info.service.maxPrice)),
                const SizedBox(height: 12),
                InfoBanner(
                  'Эмнэлгийн цагийн бүс: ${info.options.clinicTimeZoneId}\nАжлын цаг нь боломжит цагийг заана. Захиалгыг сервер баталгаажуулсны дараа цаг эцэслэгдэнэ.',
                ),
                const SizedBox(height: 12),
                OutlinedButton.icon(
                  key: const Key('booking.date'),
                  onPressed: locked
                      ? null
                      : () async {
                          final now = tz.TZDateTime.now(
                            tz.getLocation(info.options.clinicTimeZoneId),
                          );
                          final first = DateTime(now.year, now.month, now.day);
                          final selected = await showDatePicker(
                            context: context,
                            initialDate: day,
                            firstDate: first,
                            lastDate: first.add(const Duration(days: 365)),
                          );
                          if (selected != null && mounted) {
                            setState(() {
                              day = selected;
                              validation = null;
                            });
                          }
                        },
                  icon: const Icon(Icons.calendar_month),
                  label: Text(
                    day == null
                        ? 'Өдөр сонгох'
                        : DateFormat('yyyy-MM-dd').format(day!),
                  ),
                ),
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  key: const Key('booking.time'),
                  onPressed: locked
                      ? null
                      : () async {
                          final selected = await showTimePicker(
                            context: context,
                            initialTime:
                                time ?? const TimeOfDay(hour: 9, minute: 0),
                            builder: (context, child) => MediaQuery(
                              data: MediaQuery.of(context)
                                  .copyWith(alwaysUse24HourFormat: true),
                              child: child!,
                            ),
                          );
                          if (selected != null && mounted) {
                            setState(() {
                              time = selected;
                              validation = null;
                            });
                          }
                        },
                  icon: const Icon(Icons.schedule),
                  label: Text(
                    time == null
                        ? 'Цаг сонгох'
                        : '${time!.hour.toString().padLeft(2, '0')}:${time!.minute.toString().padLeft(2, '0')}',
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  'Эмчийн ажлын цагийн хуваарь',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                ...info.options.workingHours.map((w) {
                  const names = [
                    'Ням',
                    'Даваа',
                    'Мягмар',
                    'Лхагва',
                    'Пүрэв',
                    'Баасан',
                    'Бямба',
                  ];
                  String minute(int v) =>
                      '${(v ~/ 60).toString().padLeft(2, '0')}:${(v % 60).toString().padLeft(2, '0')}';
                  return Text(
                    '${names[w.dayOfWeek]}: ${minute(w.startMinute)}–${minute(w.endMinute)}',
                  );
                }),
                if (controller.consentRequired)
                  CheckboxListTile(
                    key: const Key('booking.consent'),
                    contentPadding: EdgeInsets.zero,
                    value: consent,
                    onChanged: locked
                        ? null
                        : (v) => setState(() => consent = v ?? false),
                    title: const Text(
                      'Шинэ өвчтөний бүртгэл үүсгэхийг зөвшөөрч байна.',
                    ),
                    subtitle: const Text(
                      'Энэ эмнэлэгт миний мэдээллээр өвчтөний карт үүсгэнэ. Үүнийг зөвшөөрснөөр цаг захиалж болно.',
                    ),
                  ),
                if (pending != null)
                  const InfoBanner(
                    'Өмнөх хүсэлтийн хариуг шалгаж байна. Ижил захиалгыг давхар үүсгэхээс хамгаалсан.',
                  ),
                if (validation != null || controller.error != null)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 12),
                    child: Text(
                      validation ?? controller.error!.message,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ),
                if (controller.needsRefresh || controller.storageBlocked)
                  OutlinedButton(
                    onPressed: controller.submitting ? null : refresh,
                    child: const Text('Мэдээллийг шинэчлэх'),
                  ),
                const SizedBox(height: 16),
                FilledButton(
                  key: const Key('booking.submit'),
                  onPressed:
                      controller.submitting ||
                          controller.storageBlocked ||
                          controller.needsRefresh
                      ? null
                      : () => submit(info),
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: controller.submitting
                        ? const SizedBox(
                            width: 22,
                            height: 22,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : Text(
                            pending == null
                                ? 'Цаг захиалах'
                                : 'Хүсэлтийг дахин илгээх',
                          ),
                  ),
                ),
                TextButton(
                  onPressed: () => context.go('/appointments'),
                  child: const Text('Миний захиалгууд руу'),
                ),
              ],
            );
          },
        );
      },
    ),
  );
}
