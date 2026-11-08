import 'package:intl/intl.dart';
import 'package:timezone/timezone.dart' as tz;

import '../../core/models.dart';

DateTime clinicLocalToUtc(String zone, DateTime day, int hour, int minute) {
  final value = tz.TZDateTime(
    tz.getLocation(zone),
    day.year,
    day.month,
    day.day,
    hour,
    minute,
  );
  if (value.year != day.year ||
      value.month != day.month ||
      value.day != day.day ||
      value.hour != hour ||
      value.minute != minute) {
    throw const FormatException('Invalid local time.');
  }
  return value.toUtc();
}

String clinicDateTime(DateTime utc, String zone) =>
    '${DateFormat('yyyy-MM-dd HH:mm').format(tz.TZDateTime.from(utc, tz.getLocation(zone)))} ($zone)';
String money(int min, int? max) {
  final format = NumberFormat.decimalPattern('mn');
  return max == null || max == min
      ? '${format.format(min)} ?'
      : '${format.format(min)}?${format.format(max)} ?';
}

bool withinWorkingHours(BookingOptions options, DateTime startUtc) {
  final location = tz.getLocation(options.clinicTimeZoneId);
  final begin = startUtc.subtract(
    Duration(minutes: options.bufferBeforeMinutes),
  );
  final end = startUtc.add(
    Duration(minutes: options.durationMinutes + options.bufferAfterMinutes),
  );
  for (var t = begin; t.isBefore(end); t = t.add(const Duration(minutes: 1))) {
    final local = tz.TZDateTime.from(t, location);
    final day = local.weekday % 7; // .NET Sunday=0, Dart Sunday=7.
    final minute = local.hour * 60 + local.minute;
    if (!options.workingHours.any(
      (h) =>
          h.dayOfWeek == day && h.startMinute <= minute && minute < h.endMinute,
    )) {
      return false;
    }
  }
  return true;
}
