import 'package:dio/dio.dart';

import 'models.dart';

class ApiFailure implements Exception {
  final String code;
  final int? status;
  final bool ambiguous;
  const ApiFailure(this.code, {this.status, this.ambiguous = false});
  String get message => switch (code) {
    'INVALID_CREDENTIALS' => 'Нэвтрэх мэдээлэл буруу байна.',
    'SESSION_EXPIRED' => 'Нэвтрэх хугацаа дууслаа. Дахин нэвтэрнэ үү.',
    'NETWORK_ERROR' => 'Сүлжээний алдаа гарлаа. Холболтоо шалгаад дахин оролдоно уу.',
    'PATIENT_CREATION_CONSENT_REQUIRED' =>
      'Шинэ өвчтөний бүртгэл үүсгэх зөвшөөрөл шаардлагатай.',
    'DOCTOR_TIME_CONFLICT' ||
    'ACCOUNT_TIME_CONFLICT' ||
    'BOOKING_CONFLICT' => 'Сонгосон цаг давхцаж байна. Өөр цаг сонгоно уу.',
    'CLINIC_SCHEDULING_VERSION_CONFLICT' ||
    'DOCTOR_SCHEDULING_VERSION_CONFLICT' =>
      'Хуваарийн мэдээлэл шинэчлэгдсэн. Сонголтоо дахин шалгана уу.',
    'OUTSIDE_DOCTOR_WORKING_HOURS' => 'Сонгосон цаг эмчийн ажлын хуваариас гадуур байна.',
    'INVALID_START_TIME' => 'Ирээдүйн 365 хоногийн дотор цаг сонгоно уу.',
    'CLINIC_TIMEZONE_UNVERIFIED' =>
      'Эмнэлгийн цагийн бүс баталгаажаагүй байна.',
    'BOOKING_BUSY' || 'CATALOG_BUSY' =>
      'Сервер түр завгүй байна. Дараа дахин оролдоно уу.',
    'IDEMPOTENCY_KEY_REUSED' =>
      'Захиалгын хүсэлтийн түлхүүр давхцлаа. Хүсэлтээ шалгана уу.',
    'LOCAL_PATIENT_INACTIVE' =>
      'Өвчтөний бүртгэл идэвхгүй байна. Эмнэлэгтэй холбогдоно уу.',
    'STORAGE_ERROR' =>
      'Хадгалсан мэдээлэлд алдаа гарлаа. Хүсэлтээ дахин шалгана уу.',
    'PENDING_BOOKING_EXISTS' =>
      'Өмнөх захиалгын хүсэлт шийдэгдээгүй байна. Эхлээд түүнийг шалгана уу.',
    'INVALID_SERVER_RESPONSE' =>
      'Серверээс буруу хариу ирлээ. Холболтоо дахин шалгана уу.',
    _ when status == 404 => 'Хүссэн мэдээлэл олдсонгүй.',
    _ when status == 429 =>
      'Хэт олон хүсэлт илгээсэн байна. Түр хүлээгээд дахин оролдоно уу.',
    _ when status == 403 => 'Энэ үйлдлийг хийх эрх хүрэлцэхгүй байна.',
    _ => 'Алдаа гарлаа. Дахин оролдоно уу.',
  };
  @override
  String toString() => message; // Never include request bodies, tokens or transport exceptions.
}

abstract interface class NexoraApi {
  String? get token;
  set token(String? value);
  set onUnauthorized(Future<void> Function()? value);
  Future<LoginResult> login(String identifier, String password);
  Future<Account> me();
  Future<List<Clinic>> clinics();
  Future<List<Doctor>> doctors(int clinic);
  Future<List<DentalService>> services(int clinic, int doctor);
  Future<BookingOptions> options(int clinic, int doctor, int service);
  Future<Appointment> book(int clinic, String key, Json body);
  Future<List<Appointment>> appointments({int skip = 0, int take = 50});
  Future<Appointment> appointment(int id);
}

Uri validateBaseUrl(String value) {
  final uri = Uri.tryParse(value);
  if (uri == null ||
      uri.scheme != 'https' ||
      uri.host.isEmpty ||
      uri.userInfo.isNotEmpty ||
      uri.hasQuery ||
      uri.hasFragment ||
      (uri.path.isNotEmpty && uri.path != '/')) {
    throw const FormatException(
      'API_BASE_URL must be an HTTPS origin without credentials or a path.',
    );
  }
  return uri;
}

class ApiClient implements NexoraApi {
  final Dio dio;
  @override
  String? token;
  @override
  Future<void> Function()? onUnauthorized;
  ApiClient(Uri baseUrl, {Dio? client}) : dio = client ?? Dio() {
    validateBaseUrl(baseUrl.toString());
    dio.options = BaseOptions(
      baseUrl: baseUrl.origin,
      connectTimeout: const Duration(seconds: 10),
      receiveTimeout: const Duration(seconds: 20),
      sendTimeout: const Duration(seconds: 20),
      contentType: Headers.jsonContentType,
      followRedirects: false,
    );
  }
  Future<dynamic> _send(
    String method,
    String path, {
    Json? body,
    Map<String, String>? headers,
  }) async {
    final usedToken = token;
    try {
      final response = await dio.request<dynamic>(
        path,
        data: body,
        options: Options(
          method: method,
          headers: {
            if (usedToken != null) 'Authorization': 'Bearer $usedToken',
            ...?headers,
          },
        ),
      );
      return response.data;
    } on DioException catch (e) {
      final status = e.response?.statusCode;
      if (status == 401 && usedToken != null && token == usedToken) {
        token = null;
        await onUnauthorized?.call();
      }
      final data = e.response?.data;
      final rawCode = data is Map ? data['errorCode'] : null;
      final safeCode =
          rawCode is String && RegExp(r'^[A-Z0-9_]{1,80}$').hasMatch(rawCode)
          ? rawCode
          : null;
      throw ApiFailure(
        safeCode ??
            (status == 401
                ? (path.endsWith('/login')
                      ? 'INVALID_CREDENTIALS'
                      : 'SESSION_EXPIRED')
                : status == null
                ? 'NETWORK_ERROR'
                : 'HTTP_ERROR'),
        status: status,
        ambiguous:
            status == null || status >= 500 || (status >= 300 && status < 400),
      );
    }
  }

  Future<T> _decode<T>(
    Future<dynamic> request,
    T Function(dynamic) convert,
  ) async {
    final data = await request;
    try {
      return convert(data);
    } on FormatException {
      throw const ApiFailure('INVALID_SERVER_RESPONSE', ambiguous: true);
    } on TypeError {
      throw const ApiFailure('INVALID_SERVER_RESPONSE', ambiguous: true);
    }
  }

  Future<List<T>> _list<T>(String path, T Function(Json) convert) => _decode(
    _send('GET', path),
    (data) => (data as List)
        .map((v) => convert(jsonObject(v)))
        .toList(growable: false),
  );
  @override
  Future<LoginResult> login(String identifier, String password) => _decode(
    _send(
      'POST',
      '/api/platform/auth/login',
      body: {'identifier': identifier, 'password': password},
    ),
    (v) {
      final j = jsonObject(v);
      final result = LoginResult(
        j['token'] as String,
        Account.fromJson(jsonObject(j['account'])),
      );
      if (result.token.isEmpty || result.account.id <= 0) {
        throw const FormatException();
      }
      return result;
    },
  );
  @override
  Future<Account> me() => _decode(
    _send('GET', '/api/platform/auth/me'),
    (v) => Account.fromJson(jsonObject(v)),
  );
  @override
  Future<List<Clinic>> clinics() =>
      _list('/api/platform/clinics', Clinic.fromJson);
  @override
  Future<List<Doctor>> doctors(int clinic) =>
      _list('/api/platform/clinics/$clinic/doctors', Doctor.fromJson);
  @override
  Future<List<DentalService>> services(int clinic, int doctor) => _list(
    '/api/platform/clinics/$clinic/doctors/$doctor/services',
    DentalService.fromJson,
  );
  @override
  Future<BookingOptions> options(
    int clinic,
    int doctor,
    int service,
  ) => _decode(
    _send(
      'GET',
      '/api/platform/clinics/$clinic/booking-options?doctorProfileId=$doctor&serviceId=$service',
    ),
    (v) => BookingOptions.fromJson(jsonObject(v)),
  );
  @override
  Future<Appointment> book(int clinic, String key, Json body) => _decode(
    _send(
      'POST',
      '/api/platform/clinics/$clinic/appointments',
      body: body,
      headers: {'Idempotency-Key': key},
    ),
    (v) => Appointment.fromJson(jsonObject(v)),
  );
  @override
  Future<List<Appointment>> appointments({int skip = 0, int take = 50}) =>
      _list(
        '/api/platform/me/appointments?skip=$skip&take=$take',
        Appointment.fromJson,
      );
  @override
  Future<Appointment> appointment(int id) => _decode(
    _send('GET', '/api/platform/me/appointments/$id'),
    (v) => Appointment.fromJson(jsonObject(v)),
  );
}
