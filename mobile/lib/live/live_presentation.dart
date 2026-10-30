import 'package:flutter/material.dart';
import 'package:timezone/timezone.dart' as tz;

import 'live_flow_controller.dart';
import 'models.dart';

/// Presentation only: never changes backend values or slot ordering.
abstract final class LiveLabels {
  static const weekdays = [
    'Даваа',
    'Мягмар',
    'Лхагва',
    'Пүрэв',
    'Баасан',
    'Бямба',
    'Ням',
  ];
  static bool syntheticDoctor(BookingDoctor d) =>
      d.fullName == 'SYNTHETIC SCHOOL Doctor' ||
      d.specialty == 'SYNTHETIC SCHOOL DENTISTRY';
  static String doctorName(BookingDoctor d) =>
      d.fullName == 'SYNTHETIC SCHOOL Doctor' ? 'Туршилтын эмч' : d.fullName;
  static String specialty(BookingDoctor d) =>
      d.specialty == 'SYNTHETIC SCHOOL DENTISTRY' ? 'Шүдний эмч' : d.specialty;
  static String serviceName(BookingService s) =>
      s.name == 'SYNTHETIC SCHOOL TREATMENT' ? 'Туршилтын эмчилгээ' : s.name;
  static String serviceDescription(BookingService s) =>
      s.name == 'SYNTHETIC SCHOOL TREATMENT' &&
          s.description == 'Disposable appointment rehearsal only.'
      ? 'Туршилтын мэдээлэл'
      : s.description;
  static DateTime local(DateTime instant, String zone) =>
      tz.TZDateTime.from(instant, ClinicClock.location(zone));
  static String date(String value) {
    final d = DateTime.parse(value);
    return '${d.month}-р сарын ${d.day}, ${weekdays[d.weekday - 1]}';
  }

  static String time(DateTime instant, String zone) {
    final d = local(instant, zone);
    return '${d.hour.toString().padLeft(2, '0')}:${d.minute.toString().padLeft(2, '0')}';
  }

  static String zone(String zone) => zone == 'Asia/Ulaanbaatar'
      ? 'Улаанбаатарын цагаар'
      : 'Эмнэлгийн орон нутгийн цагаар ($zone)';
  static String instant(DateTime value, String zoneId) =>
      '${date(calendarDate(local(value, zoneId)))} ${time(value, zoneId)} · ${zone(zoneId)}';
  static String duration(BookingSlot slot) =>
      '${slot.endUtc.difference(slot.startUtc).inMinutes} минут';
  static String interval(BookingSlot slot, String zone) {
    final start = local(slot.startUtc, zone), end = local(slot.endUtc, zone);
    final endDate = calendarDate(start) == calendarDate(end)
        ? ''
        : '${date(calendarDate(end))} ';
    return '${time(slot.startUtc, zone)}–$endDate${time(slot.endUtc, zone)}';
  }

  static String status(String value) => switch (value) {
    'Confirmed' => 'Баталгаажсан захиалга',
    'Cancelled' => 'Цуцалсан захиалга',
    _ => 'Захиалгын төлөвийг эмнэлгээс лавлана уу',
  };
}

class LiveSlotChoices extends StatelessWidget {
  final SlotPage page;
  final BookingSlot? selected;
  final bool enabled;
  final ValueChanged<BookingSlot> onSelected;
  const LiveSlotChoices({
    super.key,
    required this.page,
    required this.selected,
    required this.enabled,
    required this.onSelected,
  });
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final columns = (c.maxWidth / 104).floor().clamp(1, 6);
      final width = (c.maxWidth - (columns - 1) * 8) / columns;
      return Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final slot in page.slots)
            SizedBox(
              width: width,
              child: Semantics(
                selected: selected?.startUtc == slot.startUtc,
                label:
                    '${LiveLabels.interval(slot, page.timeZoneId)}, ${LiveLabels.duration(slot)}',
                child: OutlinedButton(
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size(48, 56),
                    backgroundColor: selected?.startUtc == slot.startUtc
                        ? Theme.of(context).colorScheme.primary
                        : null,
                    foregroundColor: selected?.startUtc == slot.startUtc
                        ? Theme.of(context).colorScheme.onPrimary
                        : null,
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 12,
                    ),
                  ),
                  onPressed: enabled ? () => onSelected(slot) : null,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        LiveLabels.time(slot.startUtc, page.timeZoneId),
                        style: const TextStyle(
                          fontSize: 18,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      if (selected?.startUtc == slot.startUtc)
                        const Text('Сонгосон'),
                    ],
                  ),
                ),
              ),
            ),
        ],
      );
    },
  );
}

/// Calendar-only UTC values avoid DST and device timezone arithmetic.
/// They are picker values, never booking instants sent to the API.
abstract final class BookingDateWindow {
  static List<DateTime> dates(DateTime now, String zone) {
    final today = LiveLabels.local(now, zone);
    return [
      for (var day = 0; day <= 30; day++)
        DateTime.utc(today.year, today.month, today.day + day),
    ];
  }

  static bool contains(DateTime date, DateTime now, String zone) =>
      dates(now, zone).any((d) => calendarDate(d) == calendarDate(date));
}

/// Explicit Mongolian labels: no claim of SDK Mongolian Material localization.
Future<DateTime?> showMongolianDatePicker(
  BuildContext context,
  String value, {
  required String? timeZoneId,
  DateTime Function()? now,
}) {
  final clock = now ?? DateTime.now;
  if (timeZoneId == null) {
    return showDialog<DateTime>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Огноо сонгох'),
        content: const Text(
          'Эмнэлгийн цагийн бүсийг авахын тулд боломжит цагийг эхлээд шинэчилнэ үү.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            style: TextButton.styleFrom(minimumSize: const Size(48, 48)),
            child: const Text('Хаах'),
          ),
        ],
      ),
    );
  }
  // Validates IANA IDs through ClinicClock; no guessed timezone fallback.
  final initialDates = BookingDateWindow.dates(clock(), timeZoneId);
  DateTime? chosen;
  for (final date in initialDates) {
    if (calendarDate(date) == value) chosen = date;
  }
  String? error = chosen == null
      ? 'Өмнөх огноо захиалгын хугацаанаас хэтэрсэн. Зөвшөөрөгдсөн өдрөө сонгоно уу.'
      : null;
  return showDialog<DateTime>(
    context: context,
    builder: (context) => StatefulBuilder(
      builder: (context, update) {
        final dates = BookingDateWindow.dates(clock(), timeZoneId);
        final valid = chosen != null && dates.contains(chosen);
        return AlertDialog(
          title: const Text('Огноо сонгох'),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(LiveLabels.zone(timeZoneId)),
                const Text('Өнөөдрөөс дараагийн 30 хоногийн дотор сонгоно уу.'),
                DropdownButtonFormField<DateTime>(
                  key: ValueKey(valid ? chosen : null),
                  initialValue: valid ? chosen : null,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Огноо'),
                  items: [
                    for (final date in dates)
                      DropdownMenuItem(
                        value: date,
                        child: Text(
                          '${date.year} · ${LiveLabels.date(calendarDate(date))}',
                        ),
                      ),
                  ],
                  onChanged: (date) => update(() {
                    chosen = date;
                    error = null;
                  }),
                ),
                if (error != null)
                  Semantics(
                    liveRegion: true,
                    child: Text(
                      error!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ),
              ],
            ),
          ),
          actions: [
            TextButton(
              style: TextButton.styleFrom(minimumSize: const Size(48, 48)),
              onPressed: () => Navigator.pop(context),
              child: const Text('Болих'),
            ),
            FilledButton(
              style: FilledButton.styleFrom(minimumSize: const Size(48, 48)),
              onPressed: !valid
                  ? null
                  : () {
                      // The clinic-local day may have changed while the dialog was open.
                      if (!BookingDateWindow.contains(
                        chosen!,
                        clock(),
                        timeZoneId,
                      )) {
                        update(
                          () => error = 'Захиалгын хугацаа өөрчлөгдсөн. Өдрөө дахин сонгоно уу.',
                        );
                        return;
                      }
                      Navigator.pop(context, chosen);
                    },
              child: const Text('Сонгох'),
            ),
          ],
        );
      },
    ),
  );
}
