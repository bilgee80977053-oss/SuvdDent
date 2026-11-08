typedef Json = Map<String, dynamic>;

Json jsonObject(dynamic value) => Map<String, dynamic>.from(value as Map);

class Account {
  final int id;
  final String fullName;
  const Account(this.id, this.fullName);
  factory Account.fromJson(Json j) =>
      Account(j['id'] as int, j['fullName'] as String);
}

class Clinic {
  final int id;
  final String name, timeZoneId;
  const Clinic(this.id, this.name, this.timeZoneId);
  factory Clinic.fromJson(Json j) =>
      Clinic(j['id'] as int, j['name'] as String, j['timeZoneId'] as String);
}

class Doctor {
  final int id, practiceStartYear;
  final String fullName, specialty;
  final String? biography;
  const Doctor(
    this.id,
    this.fullName,
    this.specialty,
    this.practiceStartYear,
    this.biography,
  );
  factory Doctor.fromJson(Json j) => Doctor(
    j['id'] as int,
    j['fullName'] as String,
    j['specialty'] as String,
    j['practiceStartYear'] as int,
    j['biography'] as String?,
  );
}

class DentalService {
  final int id,
      minPrice,
      durationMinutes,
      bufferBeforeMinutes,
      bufferAfterMinutes;
  final int? maxPrice;
  final String name, description;
  const DentalService(
    this.id,
    this.name,
    this.description,
    this.minPrice,
    this.maxPrice,
    this.durationMinutes,
    this.bufferBeforeMinutes,
    this.bufferAfterMinutes,
  );
  factory DentalService.fromJson(Json j) => DentalService(
    j['id'] as int,
    j['name'] as String,
    j['description'] as String,
    j['minPrice'] as int,
    j['maxPrice'] as int?,
    j['durationMinutes'] as int,
    j['bufferBeforeMinutes'] as int,
    j['bufferAfterMinutes'] as int,
  );
}

class WorkingWindow {
  final int dayOfWeek, startMinute, endMinute;
  const WorkingWindow(this.dayOfWeek, this.startMinute, this.endMinute);
  factory WorkingWindow.fromJson(Json j) => WorkingWindow(
    j['dayOfWeek'] as int,
    j['startMinute'] as int,
    j['endMinute'] as int,
  );
}

class BookingOptions {
  final int clinicId,
      doctorProfileId,
      serviceId,
      durationMinutes,
      bufferBeforeMinutes,
      bufferAfterMinutes;
  final String clinicVersion, doctorVersion, clinicTimeZoneId;
  final List<WorkingWindow> workingHours;
  const BookingOptions(
    this.clinicId,
    this.doctorProfileId,
    this.serviceId,
    this.clinicVersion,
    this.doctorVersion,
    this.clinicTimeZoneId,
    this.durationMinutes,
    this.bufferBeforeMinutes,
    this.bufferAfterMinutes,
    this.workingHours,
  );
  factory BookingOptions.fromJson(Json j) => BookingOptions(
    j['clinicId'] as int,
    j['doctorProfileId'] as int,
    j['serviceId'] as int,
    j['clinicVersion'] as String,
    j['doctorVersion'] as String,
    j['clinicTimeZoneId'] as String,
    j['durationMinutes'] as int,
    j['bufferBeforeMinutes'] as int,
    j['bufferAfterMinutes'] as int,
    (j['workingHours'] as List)
        .map((v) => WorkingWindow.fromJson(jsonObject(v)))
        .toList(growable: false),
  );
}

class Appointment {
  final int id,
      clinicId,
      doctorProfileId,
      serviceId,
      status,
      durationMinutes,
      minPrice;
  final int? maxPrice;
  final DateTime startUtc, endUtc;
  final String clinicTimeZoneId, doctorName, serviceName;
  const Appointment(
    this.id,
    this.clinicId,
    this.doctorProfileId,
    this.serviceId,
    this.status,
    this.startUtc,
    this.endUtc,
    this.clinicTimeZoneId,
    this.doctorName,
    this.serviceName,
    this.durationMinutes,
    this.minPrice,
    this.maxPrice,
  );
  factory Appointment.fromJson(Json j) => Appointment(
    j['id'] as int,
    j['clinicId'] as int,
    j['doctorProfileId'] as int,
    j['serviceId'] as int,
    j['status'] as int,
    DateTime.parse(j['startUtc'] as String).toUtc(),
    DateTime.parse(j['endUtc'] as String).toUtc(),
    j['clinicTimeZoneId'] as String,
    j['doctorName'] as String,
    j['serviceName'] as String,
    j['durationMinutes'] as int,
    j['minPrice'] as int,
    j['maxPrice'] as int?,
  );
  String get reference => 'NX-$id';
  String get statusLabel => switch (status) {
    0 => 'Баталгаажсан',
    1 => 'Цуцлагдсан',
    _ => 'Төлөв: $status',
  };
}

class LoginResult {
  final String token;
  final Account account;
  const LoginResult(this.token, this.account);
}
