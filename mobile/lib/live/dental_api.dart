import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart' show kReleaseMode;
import 'package:http/http.dart' as http;

import 'models.dart';
import 'patient_invoice_review.dart';

class ApiFailure implements Exception {
  final int? status;
  final String message;
  const ApiFailure(this.message, [this.status]);
  @override
  String toString() => message;
}

class RegistrationFailure extends ApiFailure {
  final int? retryAfterSeconds;
  final VerificationReceipt? pendingRegistration;
  const RegistrationFailure(
    super.message,
    int super.status, {
    this.retryAfterSeconds,
    this.pendingRegistration,
  });
}

class DentalApi {
  final ApiSettings settings;
  final http.Client client;
  final Duration timeout;
  String? _token;
  int _generation = 0;
  void Function()? onUnauthorized;
  DentalApi(
    this.settings, {
    http.Client? client,
    this.timeout = const Duration(seconds: 20),
  }) : client = client ?? http.Client() {
    if (!settings.isLive || settings.error != null) {
      throw ArgumentError('Valid LIVE API settings required');
    }
  }
  bool get authenticated => _token != null;
  void clearSession() {
    _token = null;
    _generation++;
  }

  void close() {
    clearSession();
    client.close();
  }

  static String messageFor(int status) => switch (status) {
    400 => 'Мэдээллээ шалгаад дахин оролдоно уу.',
    401 => 'Нэвтрэх мэдээлэл буруу эсвэл нэвтрэх хугацаа дууссан. Дахин нэвтэрнэ үү.',
    403 => 'Энэ үйлдэлд эрх хүрэхгүй. Имэйл баталгаажуулалтаа шалгана уу.',
    404 => 'Мэдээлэл олдсонгүй.',
    409 => 'Сонгосон цаг боломжгүй болсон эсвэл тохиргоо зөрчилтэй. Боломжит цагийг шинэчилнэ үү.',
    429 => 'Хэт олон хүсэлт илгээсэн байна. Түр хүлээгээд дахин оролдоно уу.',
    503 => 'Үйлчилгээ түр ажиллахгүй байна. Имэйл илгээх тохиргоог администратороор шалгуулна уу.',
    _ => 'Серверийн алдаа гарлаа. Дараа дахин оролдоно уу.',
  };

  Future<T> _request<T>(
    String method,
    String path,
    T Function(dynamic) parse, {
    Json? body,
    Map<String, String>? query,
    bool auth = true,
    bool privateResponse = false,
    bool registrationResponse = false,
    Set<int> statuses = const {200},
  }) async {
    if (auth && _token == null) throw ApiFailure(messageFor(401), 401);
    final generation = _generation;
    try {
      final uri = Uri.parse(settings.baseUrl)
          .replace(path: path, queryParameters: query);
      final request = http.Request(method, uri)
        ..headers['Accept'] = 'application/json';
      if (privateResponse || registrationResponse) {
        request.followRedirects = false;
        request.headers['Cache-Control'] = 'no-store';
      }
      if (auth) request.headers['Authorization'] = 'Bearer $_token';
      if (body != null) {
        request.headers['Content-Type'] = 'application/json';
        request.body = jsonEncode(body);
      }
      final response = await (() async => http.Response.fromStream(
        await client.send(request),
      ))().timeout(timeout);
      if (generation != _generation) {
        throw const ApiFailure('Нэвтрэх төлөв өөрчлөгдсөн. Дахин оролдоно уу.');
      }
      if (!statuses.contains(response.statusCode)) {
        if (response.statusCode == 401 && auth) {
          clearSession();
          onUnauthorized?.call();
        }
        // Only known domain messages may be shown; never expose framework/HTML/stack traces.
        var message = messageFor(response.statusCode);
        try {
          final j = jsonDecode(utf8.decode(response.bodyBytes));
          final candidate = j is Map ? (j['error'] ?? j['message']) : null;
          if (candidate is String &&
              candidate.length <= 400 &&
              RegExp(r'^[А-Яа-яЁёӨөҮү0-9\s.,:!?()\-]+$').hasMatch(candidate)) {
            message = candidate;
          }
        } catch (_) {
          /* Keep safe status message. */
        }
        if (registrationResponse) {
          throw _registrationFailure(response, message);
        }
        throw ApiFailure(message, response.statusCode);
      }
      return parse(jsonDecode(utf8.decode(response.bodyBytes)));
    } on ApiFailure {
      rethrow;
    } on TimeoutException {
      throw const ApiFailure(
        'Холболтын хугацаа дууслаа. Дахин оролдоно уу. Захиалгын хүсэлтийн дугаар хэвээр үлдэнэ.',
      );
    } on http.ClientException {
      throw const ApiFailure(
        'Сервертэй холбогдож чадсангүй. Интернэт, серверийн хаяг болон CORS тохиргоог шалгана уу.',
      );
    } catch (_) {
      throw const ApiFailure('Серверийн хариу буруу байна. Дахин оролдоно уу.');
    }
  }

  RegistrationFailure _registrationFailure(
    http.Response response,
    String message,
  ) {
    int? retry;
    VerificationReceipt? pending;
    if (response.statusCode == 503) {
      message = 'Баталгаажуулах үйлчилгээ түр ажиллахгүй байна.';
    }
    try {
      final j = object(jsonDecode(utf8.decode(response.bodyBytes)));
      final value = j['retryAfterSeconds'];
      if (value is int && value > 0 && value <= 3600) retry = value;
      final reason = j['reason'];
      message = switch (reason) {
        'invalid_code' => 'Баталгаажуулах код буруу байна.',
        'expired_code' => 'Кодын хугацаа дууссан. Шинэ код хүснэ үү.',
        'otp_limit' => 'Баталгаажуулах оролдлогын тоо хэтэрлээ. Түр хүлээгээд дахин оролдоно уу.',
        'delivery_unavailable' => 'Сонгосон сувгаар код илгээж чадсангүй. Өөр арга сонгох эсвэл дахин оролдоно уу.',
        _ => message,
      };
      if ((response.statusCode == 503 && reason == 'delivery_unavailable') ||
          (response.statusCode == 429 && reason == 'retry_cooldown')) {
        final id = j['userId'];
        final token = j['registrationToken'];
        if (id is int && id > 0 && token is String && token.isNotEmpty) {
          pending = VerificationReceipt.fromJson({...j, 'message': message});
        }
      }
    } catch (_) {
      // Keep a safe localized status message for an unexpected response.
    }
    return RegistrationFailure(
      message,
      response.statusCode,
      retryAfterSeconds: retry,
      pendingRegistration: pending,
    );
  }

  Future<AuthSession> login(String identifier, String password) async {
    clearSession();
    final generation = _generation;
    final session = await _request(
      'POST',
      '/api/Auth/login',
      (v) => AuthSession.fromJson(object(v)),
      auth: false,
      body: {
        'identifier': identifier,
        'password': password,
        'clinicId': settings.clinicId,
      },
    );
    if (session.clinicId != settings.clinicId || session.role != 'Patient') {
      throw const ApiFailure(
        'Энэ эмнэлгийн өвчтөний бүртгэлээр нэвтэрнэ үү.',
        403,
      );
    }
    if (generation != _generation) {
      throw const ApiFailure('Нэвтрэх төлөв өөрчлөгдсөн. Дахин нэвтэрнэ үү.');
    }
    _token = session.token;
    return session;
  }

  Future<AuthIdentity> me() =>
      _request('GET', '/api/Auth/me', (v) => AuthIdentity.fromJson(object(v)));
  Future<VerificationReceipt> register(
    String name,
    String email,
    String password,
    String? phone, {
    RegistrationMethod? verificationMethod,
  }) => _request(
    'POST',
    '/api/Auth/register',
    (v) => VerificationReceipt.fromJson(object(v)),
    auth: false,
    registrationResponse: verificationMethod != null,
    body: {
      'fullName': name,
      'email': email.isEmpty ? null : email,
      'phone': phone,
      'password': password,
      'clinicId': settings.clinicId,
      if (verificationMethod != null)
        'verificationMethod': verificationMethod.name,
    },
  );

  Future<VerificationReceipt> requestRegistrationCode(
    VerificationReceipt pending,
    RegistrationMethod method,
  ) {
    final token = pending.registrationToken;
    if (token == null || token.isEmpty) {
      throw const ApiFailure(
        'Код дахин хүсэхэд бүртгэлийн холбоос шаардлагатай.',
      );
    }
    return _request(
      'POST',
      '/api/Auth/registration-code',
      (v) => VerificationReceipt.fromJson(object(v)),
      auth: false,
      registrationResponse: true,
      body: {
        'userId': pending.userId,
        'verificationMethod': method.name,
        'registrationToken': token,
      },
    );
  }

  Future<VerificationReceipt> verify(
    int userId,
    String code, {
    String? registrationToken,
  }) => _request(
    'POST',
    '/api/Auth/verify',
    (v) => VerificationReceipt.fromJson(object(v)),
    auth: false,
    registrationResponse: registrationToken != null,
    body: {
      'userId': userId,
      'code': code,
      'registrationToken': ?registrationToken,
    },
  );
  // Pending email registrations can recover after a failed first SMTP send.
  // The backend response is deliberately generic to avoid email enumeration.
  Future<String> resendRegistrationCode(String email) => _request(
    'POST',
    '/api/Auth/resend-code',
    (v) => text(object(v), 'message'),
    auth: false,
    body: {'email': email, 'clinicId': settings.clinicId},
  );

  // Verify an existing registration without the userId lost on a 503.
  Future<VerificationReceipt> verifyRegistrationEmail(
    String email,
    String code,
  ) => _request(
    'POST',
    '/api/Auth/verify-email',
    (v) => VerificationReceipt.fromJson(object(v)),
    auth: false,
    body: {'email': email, 'code': code, 'clinicId': settings.clinicId},
  );

  Future<NotificationPreferences> notificationPreferences() => _request(
    'GET',
    '/api/notification-preferences/me',
    (v) => NotificationPreferences.fromJson(object(v)),
  );
  Future<NotificationPreferences> saveNotificationPreferences(
    bool email,
    bool sms,
  ) => _request(
    'PUT',
    '/api/notification-preferences/me',
    (v) => NotificationPreferences.fromJson(object(v)),
    body: {'emailEnabled': email, 'smsEnabled': sms},
  );

  Future<PatientProfile> profile() => _request(
    'GET',
    '/api/Patients/me',
    (v) => PatientProfile.fromJson(object(v)),
  );
  Future<PatientProfile> createProfile(
    String name,
    String phone, {
    String? dateOfBirth,
    String? address,
  }) => _request(
    'POST',
    '/api/Patients/me',
    (v) => PatientProfile.fromJson(object(v)),
    statuses: const {201},
    body: {
      'fullName': name,
      'phone': phone,
      'dateOfBirth': dateOfBirth,
      'address': address,
    },
  );
  // Authenticated patient updates only their own tenant-scoped profile.
  Future<PatientProfile> updateProfile({
    required String fullName,
    required String phone,
    String? dateOfBirth,
    String? address,
  }) => _request(
    'PUT',
    '/api/Patients/me',
    (v) => PatientProfile.fromJson(object(v)),
    body: {
      'fullName': fullName,
      'phone': phone,
      'dateOfBirth': dateOfBirth,
      'address': address,
    },
  );

  Future<List<BookingService>> services() => _request(
    'GET',
    '/api/appointments/services',
    (v) => (v as List).map((j) => BookingService.fromJson(object(j))).toList(),
  );
  Future<List<BookingDoctor>> doctors(int serviceId) => _request(
    'GET',
    '/api/appointments/doctors',
    (v) => (v as List).map((j) => BookingDoctor.fromJson(object(j))).toList(),
    query: {'serviceId': '$serviceId'},
  );
  Future<SlotPage> slots(int doctorId, int serviceId, String date) => _request(
    'GET',
    '/api/appointments/slots',
    (v) => SlotPage.fromJson(object(v)),
    query: {
      'doctorProfileId': '$doctorId',
      'serviceId': '$serviceId',
      'date': date,
    },
  );
  Future<SlotPage> rescheduleSlots(int appointmentId, String date) =>
      _request('GET', '/api/appointments/$appointmentId/reschedule-slots', (v) {
        final result = SlotPage.fromJson(object(v));
        if (result.date != date) {
          throw const ApiFailure('Боломжит цагийн огноо буруу байна.');
        }
        return result;
      }, query: {'date': date});
  Future<Appointment> rescheduleAppointment(
    Appointment source,
    DateTime startUtc,
    String requestId,
  ) => _request(
    'POST',
    '/api/appointments/${source.id}/reschedule',
    (v) {
      final result = Appointment.fromJson(object(v));
      if (result.id <= 0 ||
          result.id == source.id ||
          result.doctorProfileId != source.doctorProfileId ||
          result.serviceId != source.serviceId ||
          result.startUtc != startUtc.toUtc() ||
          (result.status != 'Confirmed' && result.status != 'Cancelled')) {
        throw const ApiFailure('Цаг шилжүүлэлтийн хариу тодорхойгүй байна.');
      }
      return result;
    },
    body: {
      'startUtc': startUtc.toUtc().toIso8601String(),
      'requestId': requestId,
    },
  );

  Future<Appointment> book(
    int doctorId,
    int serviceId,
    DateTime startUtc,
    String requestId,
  ) => _request(
    'POST',
    '/api/appointments',
    (v) => Appointment.fromJson(object(v)),
    statuses: const {200, 201},
    body: {
      'doctorProfileId': doctorId,
      'serviceId': serviceId,
      'startUtc': startUtc.toUtc().toIso8601String(),
      'requestId': requestId,
    },
  );
  Future<PaymentEstimate> paymentEstimate(
    int appointmentId, {
    int? expectedServiceId,
  }) async {
    if (appointmentId <= 0 ||
        (expectedServiceId != null && expectedServiceId <= 0)) {
      throw const ApiFailure('Захиалгын мэдээлэл буруу байна.', 400);
    }
    return _request(
      'GET',
      '/api/appointments/$appointmentId/payment-estimate',
      (v) {
        final estimate = PaymentEstimate.fromJson(object(v));
        if (estimate.appointmentId != appointmentId ||
            (expectedServiceId != null &&
                estimate.serviceId != expectedServiceId)) {
          throw const FormatException(
            'Estimate does not match the selected appointment.',
          );
        }
        return estimate;
      },
      privateResponse: true,
    );
  }

  /// Review composition only; never called by the normal or release entry.
  /// The server, not the selected ID or this transport, authorizes ownership.
  Future<InvoiceReviewRead?> readInvoiceDraftReview(int appointmentId) async {
    final base = Uri.parse(settings.baseUrl);
    if (kReleaseMode ||
        base.scheme != 'https' ||
        base.host != '127.0.0.1' ||
        !base.hasPort ||
        base.port <= 0) {
      throw const ApiFailure('Туршилтын HTTPS задаргаа идэвхгүй байна.');
    }
    if (appointmentId < 1 || appointmentId > 2147483647) {
      throw const ApiFailure('Захиалгын мэдээлэл буруу байна.', 400);
    }
    try {
      return await _request(
        'GET',
        '/api/appointments/$appointmentId/invoice-draft-review',
        (v) {
          final data = object(v);
          if (data.length != 2 ||
              data['appointmentId'] is! int ||
              data['appointmentId'] != appointmentId ||
              !data.containsKey('breakdown')) {
            throw const FormatException('Review does not match the booking.');
          }
          return InvoiceReviewRead(
            appointmentId,
            InvoiceReviewBreakdown.fromJson(data['breakdown']),
          );
        },
        privateResponse: true,
      );
    } on ApiFailure catch (error) {
      if (error.status == 404) return null;
      // Never expose a provider/body error or error details from this route.
      throw ApiFailure(
        'Энэ захиалгын задаргааг харуулах боломжгүй.',
        error.status,
      );
    }
  }

  Future<String> emailPaymentEstimate(int appointmentId) => _request(
    'POST',
    '/api/appointments/$appointmentId/payment-estimate/email',
    (v) => text(object(v), 'message'),
  );

  Future<Appointment> appointment(int id) =>
      _request('GET', '/api/appointments/$id', (v) {
        final result = Appointment.fromJson(object(v));
        if (result.id != id) {
          throw const ApiFailure('Захиалгын хариу буруу байна.');
        }
        return result;
      });

  Future<Appointment> cancelAppointment(int id) =>
      _request('POST', '/api/appointments/$id/cancel', (v) {
        final result = Appointment.fromJson(object(v));
        if (result.id != id || result.status != 'Cancelled') {
          throw const ApiFailure('Цуцлалтын хариу тодорхойгүй байна.');
        }
        return result;
      });

  Future<AppointmentPage> appointments(int page, {int pageSize = 20}) =>
      _request(
        'GET',
        '/api/appointments/me',
        (v) => AppointmentPage.fromJson(object(v)),
        query: {'page': '$page', 'pageSize': '$pageSize'},
      );
}
