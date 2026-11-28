import 'package:flutter/foundation.dart';

class ApiSettings {
  final String baseUrl;
  final int clinicId;
  const ApiSettings({
    this.baseUrl = const String.fromEnvironment('NEXORA_API_BASE_URL'),
    this.clinicId = const int.fromEnvironment('NEXORA_CLINIC_ID'),
  });
  bool get isLive => baseUrl.isNotEmpty;
  String? get error {
    final uri = Uri.tryParse(baseUrl);
    if (!isLive) return null;
    if (uri == null ||
        !uri.hasAuthority ||
        uri.host.isEmpty ||
        !['http', 'https'].contains(uri.scheme) ||
        uri.userInfo.isNotEmpty ||
        uri.hasQuery ||
        uri.hasFragment ||
        (uri.path.isNotEmpty && uri.path != '/')) {
      return 'API хаяг нь http(s) серверийн үндсэн хаяг байх ёстой.';
    }
    if (uri.scheme == 'http' && kReleaseMode) {
      return 'Release горимд HTTPS хаяг шаардлагатай.';
    }
    if (clinicId <= 0) {
      return 'Туршилтын эмнэлгийн NEXORA_CLINIC_ID тохируулна уу.';
    }
    return null;
  }
}

typedef Json = Map<String, dynamic>;
Json object(dynamic value) => value as Json;
String text(Json j, String key) => j[key] as String;
int number(Json j, String key) => j[key] as int;
DateTime instant(Json j, String key) {
  final value = text(j, key);
  if (!RegExp(r'(Z|[+-]\d{2}:\d{2})$').hasMatch(value)) {
    throw const FormatException();
  }
  return DateTime.parse(value).toUtc();
}

class AuthSession {
  final String token, fullName, role;
  final int userId, clinicId;
  AuthSession.fromJson(Json j)
    : token = text(j, 'token'),
      fullName = text(j, 'fullName'),
      role = text(j, 'role'),
      userId = number(j, 'userId'),
      clinicId = number(j, 'clinicId') {
    if (token.isEmpty || userId <= 0 || clinicId <= 0) {
      throw const FormatException();
    }
  }
}

class AuthIdentity {
  final String? userId, name, role, clinicId;
  AuthIdentity.fromJson(Json j)
    : userId = j['userId'] as String?,
      name = j['name'] as String?,
      role = j['role'] as String?,
      clinicId = j['clinicId'] as String?;
}

class VerificationReceipt {
  final int userId;
  final String message;
  VerificationReceipt.fromJson(Json j)
    : userId = number(j, 'userId'),
      message = text(j, 'message');
}

class PatientProfile {
  final int id;
  final String fullName, phone;
  final String? dateOfBirth, address;
  PatientProfile.fromJson(Json j)
    : id = number(j, 'id'),
      fullName = text(j, 'fullName'),
      phone = text(j, 'phone'),
      dateOfBirth = j['dateOfBirth'] as String?,
      address = j['address'] as String?;
}

class BookingService {
  final int id, minPrice;
  final int? maxPrice;
  final String name, description;
  final String? imagePath, aftercareText;
  BookingService.fromJson(Json j)
    : id = number(j, 'id'),
      name = text(j, 'name'),
      description = text(j, 'description'),
      minPrice = number(j, 'minPrice'),
      maxPrice = j['maxPrice'] as int?,
      imagePath = j['imagePath'] as String?,
      aftercareText = j['aftercareText'] as String?;
}

class BookingDoctor {
  final int id, practiceStartYear;
  final String fullName, specialty;
  final String? biography, photoPath;
  BookingDoctor.fromJson(Json j)
    : id = number(j, 'id'),
      fullName = text(j, 'fullName'),
      specialty = text(j, 'specialty'),
      practiceStartYear = number(j, 'practiceStartYear'),
      biography = j['biography'] as String?,
      photoPath = j['photoPath'] as String?;
}

class BookingSlot {
  final DateTime startUtc, endUtc;
  BookingSlot.fromJson(Json j)
    : startUtc = instant(j, 'startUtc'),
      endUtc = instant(j, 'endUtc');
}

class SlotPage {
  final String date, timeZoneId;
  final List<BookingSlot> slots;
  SlotPage.fromJson(Json j)
    : date = text(j, 'date'),
      timeZoneId = text(j, 'timeZoneId'),
      slots = (j['slots'] as List)
          .map((v) => BookingSlot.fromJson(object(v)))
          .toList();
}

class Appointment {
  final int id, doctorProfileId, serviceId;
  final DateTime startUtc,
      endUtc,
      occupiedStartUtc,
      occupiedEndUtc,
      createdAtUtc;
  final String clinicTimeZoneId, status;
  Appointment.fromJson(Json j)
    : id = number(j, 'id'),
      doctorProfileId = number(j, 'doctorProfileId'),
      serviceId = number(j, 'serviceId'),
      startUtc = instant(j, 'startUtc'),
      endUtc = instant(j, 'endUtc'),
      occupiedStartUtc = instant(j, 'occupiedStartUtc'),
      occupiedEndUtc = instant(j, 'occupiedEndUtc'),
      createdAtUtc = instant(j, 'createdAtUtc'),
      clinicTimeZoneId = text(j, 'clinicTimeZoneId'),
      status = text(j, 'status');
}

class AppointmentPage {
  final int page, pageSize;
  final List<Appointment> items;
  AppointmentPage.fromJson(Json j)
    : page = number(j, 'page'),
      pageSize = number(j, 'pageSize'),
      items = (j['items'] as List)
          .map((v) => Appointment.fromJson(object(v)))
          .toList();
}

class NotificationPreferences {
  final bool emailEnabled, smsEnabled, isEmailVerified, isPhoneVerified;
  NotificationPreferences.fromJson(Json j)
    : emailEnabled = j['emailEnabled'] as bool,
      smsEnabled = j['smsEnabled'] as bool,
      isEmailVerified = j['isEmailVerified'] as bool,
      isPhoneVerified = j['isPhoneVerified'] as bool;
}

/// A read-only price estimate returned by the authenticated backend.
/// Never treat this as a final bill, paid transaction or tax receipt.
class PaymentEstimate {
  final int appointmentId, serviceId;
  final String serviceName, currency, priceSource, paymentState,
      priceExplanation, receiptState;
  final int? minPriceMnt, maxPriceMnt, totalDueMnt;
  final bool isFinalAmount;

  PaymentEstimate.fromJson(Json j)
      : appointmentId = number(j, 'appointmentId'),
        serviceId = number(j, 'serviceId'),
        serviceName = text(j, 'serviceName'),
        currency = text(j, 'currency'),
        priceSource = text(j, 'priceSource'),
        paymentState = text(j, 'paymentState'),
        priceExplanation = text(j, 'priceExplanation'),
        receiptState = text(j, 'receiptState'),
        minPriceMnt = j['minPriceMnt'] as int?,
        maxPriceMnt = j['maxPriceMnt'] as int?,
        totalDueMnt = j['totalDueMnt'] as int?,
        isFinalAmount = j['isFinalAmount'] as bool {
    if (appointmentId <= 0 || serviceId <= 0 || currency != 'MNT' ||
        priceSource != 'ClinicServiceCatalog' || serviceName.trim().isEmpty ||
        isFinalAmount || totalDueMnt != null ||
        paymentState != 'NotTracked' || receiptState != 'NotIssued' ||
        (minPriceMnt != null && minPriceMnt! <= 0) ||
        (maxPriceMnt != null && (minPriceMnt == null || maxPriceMnt! < minPriceMnt!))) {
      throw const FormatException('Invalid payment estimate.');
    }
  }
}
