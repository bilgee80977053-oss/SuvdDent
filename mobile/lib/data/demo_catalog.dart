// Synthetic UI fixtures only. These IDs are never sent to the backend.
class DemoClinic {
  const DemoClinic(
    this.id,
    this.name,
    this.location,
    this.timeZoneId,
    this.utcOffsetHours,
  );
  final int id;
  final String name;
  final String location;
  final String timeZoneId;
  final int utcOffsetHours;
}

class DemoService {
  const DemoService(
    this.id,
    this.clinicId,
    this.name,
    this.description,
    this.durationMinutes,
  );
  final int id;
  final int clinicId;
  final String name;
  final String description;
  final int durationMinutes;
}

class DemoDoctor {
  const DemoDoctor(
    this.id,
    this.clinicId,
    this.name,
    this.specialty,
    this.serviceIds,
  );
  final int id;
  final int clinicId;
  final String name;
  final String specialty;
  final List<int> serviceIds;
}

class DemoSlot {
  const DemoSlot(this.id, this.startUtc);
  final int id;
  final DateTime startUtc;

  // Fixed offsets are for this frozen November 2026 fixture only.
  // Future real slots must use the server's verified clinic timezone.
  String label(DemoClinic clinic) {
    final local = startUtc.add(Duration(hours: clinic.utcOffsetHours));
    String two(int n) => n.toString().padLeft(2, '0');
    return '${two(local.hour)}:${two(local.minute)}';
  }
}

abstract final class DemoCatalog {
  static const email = 'student@example.invalid';
  static const patientName = 'Жишээ өвчтөн';
  static const clinics = [
    DemoClinic(
      101,
      'Нексора · Төв',
      'Улаанбаатар · жишээ салбар',
      'Asia/Ulaanbaatar',
      8,
    ),
    DemoClinic(202, 'Нексора · Баруун', 'Ховд · жишээ салбар', 'Asia/Hovd', 7),
  ];
  static const services = [
    DemoService(51, 101, 'Шүдний үзлэг', 'Анхан үзлэг, зөвлөгөө', 30),
    DemoService(
      52,
      101,
      'Шүд цэвэрлэгээ',
      'Амны хөндийн арчилгааны жишээ үйлчилгээ',
      60,
    ),
    DemoService(61, 202, 'Шүдний үзлэг', 'Анхан үзлэг, зөвлөгөө', 30),
    DemoService(
      62,
      202,
      'Шүд цэвэрлэгээ',
      'Амны хөндийн арчилгааны жишээ үйлчилгээ',
      60,
    ),
  ];
  static const doctors = [
    DemoDoctor(31, 101, 'Жишээ эмч А', 'Ерөнхий шүдний эмч', [51, 52]),
    DemoDoctor(33, 101, 'Жишээ эмч Б', 'Урьдчилан сэргийлэх үзлэг', [51]),
    DemoDoctor(32, 202, 'Жишээ эмч В', 'Ерөнхий шүдний эмч', [61, 62]),
  ];

  static List<DemoSlot> slots(DemoClinic clinic) => [
    DemoSlot(1, DateTime.utc(2026, 11, 2, 9 - clinic.utcOffsetHours)),
    DemoSlot(2, DateTime.utc(2026, 11, 2, 10 - clinic.utcOffsetHours, 30)),
    DemoSlot(3, DateTime.utc(2026, 11, 2, 14 - clinic.utcOffsetHours)),
  ];
}
