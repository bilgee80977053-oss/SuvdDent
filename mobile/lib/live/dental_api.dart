import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'models.dart';

class ApiFailure implements Exception {
  final int? status;
  final String message;
  const ApiFailure(this.message, [this.status]);
  @override
  String toString() => message;
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
    Set<int> statuses = const {200},
  }) async {
    if (auth && _token == null) throw ApiFailure(messageFor(401), 401);
    final generation = _generation;
    try {
      final uri = Uri.parse(settings.baseUrl)
          .replace(path: path, queryParameters: query);
      final request = http.Request(method, uri)
        ..headers['Accept'] = 'application/json';
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
    String? phone,
  ) => _request(
    'POST',
    '/api/Auth/register',
    (v) => VerificationReceipt.fromJson(object(v)),
    auth: false,
    body: {
      'fullName': name,
      'email': email,
      'phone': phone,
      'password': password,
      'clinicId': settings.clinicId,
    },
  );
  Future<VerificationReceipt> verify(int userId, String code) => _request(
    'POST',
    '/api/Auth/verify',
    (v) => VerificationReceipt.fromJson(object(v)),
    auth: false,
    body: {'userId': userId, 'code': code},
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
  Future<AppointmentPage> appointments(int page, {int pageSize = 20}) =>
      _request(
        'GET',
        '/api/appointments/me',
        (v) => AppointmentPage.fromJson(object(v)),
        query: {'page': '$page', 'pageSize': '$pageSize'},
      );
}
