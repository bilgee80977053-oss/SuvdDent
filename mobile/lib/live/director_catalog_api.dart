import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'dental_api.dart';
import 'models.dart';

/// Director catalog access is intentionally separate from patient auth.
/// No credentials or bearer token are persisted on disk.
class DirectorCatalogApi {
  final ApiSettings settings;
  final http.Client client;
  final Duration timeout;
  String? _token;
  int _generation = 0;

  DirectorCatalogApi(
    this.settings, {
    http.Client? client,
    this.timeout = const Duration(seconds: 20),
  }) : client = client ?? http.Client() {
    if (!settings.isLive || settings.error != null) {
      throw ArgumentError('A configured LIVE clinic is required.');
    }
  }

  bool get authenticated => _token != null;

  void logout() {
    _generation++;
    _token = null;
  }

  void close() {
    logout();
    client.close();
  }

  Future<dynamic> _call(
    String method,
    String path, {
    Map<String, dynamic>? body,
    String? temporaryToken,
    bool needsAuth = true,
    Set<int> accepted = const {200},
  }) async {
    final generation = _generation;
    final token = temporaryToken ?? _token;
    if (needsAuth && token == null) {
      throw const ApiFailure('Захирлын эрхээр нэвтэрнэ үү.', 401);
    }
    final uri = Uri.parse(settings.baseUrl).replace(path: path);
    final request = http.Request(method, uri)
      ..headers['Accept'] = 'application/json';
    if (needsAuth) request.headers['Authorization'] = 'Bearer $token';
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    http.Response response;
    try {
      response = await http.Response.fromStream(
        await client.send(request),
      ).timeout(timeout);
    } on TimeoutException {
      throw const ApiFailure('Серверийн хариу удааширлаа. Дахин шалгана уу.');
    } on http.ClientException {
      throw const ApiFailure('Сервертэй холбогдох боломжгүй. API болон CORS-ийг шалгана уу.');
    }
    if (_generation != generation) {
      throw const ApiFailure('Нэвтрэх төлөв өөрчлөгдлөө.');
    }
    if (!accepted.contains(response.statusCode)) {
      if (response.statusCode == 401 && needsAuth && temporaryToken == null) {
        logout();
      }
      throw ApiFailure(DentalApi.messageFor(response.statusCode), response.statusCode);
    }
    try {
      return jsonDecode(utf8.decode(response.bodyBytes));
    } on FormatException {
      throw const ApiFailure('Серверийн хариу буруу байна.');
    }
  }

  Future<void> login(String identifier, String password) async {
    logout();
    final generation = _generation;
    final result = await _call(
      'POST',
      '/api/Auth/login',
      needsAuth: false,
      body: {
        'identifier': identifier,
        'password': password,
        'clinicId': settings.clinicId,
      },
    );
    final session = AuthSession.fromJson(object(result));
    if (session.clinicId != settings.clinicId || session.role != 'Director') {
      throw const ApiFailure('Зөвхөн энэ эмнэлгийн Director эрхээр нэвтрэх боломжтой.', 403);
    }
    final identity = AuthIdentity.fromJson(object(await _call(
      'GET',
      '/api/Auth/me',
      temporaryToken: session.token,
    )));
    if (identity.role != 'Director' ||
        identity.clinicId != '${settings.clinicId}' ||
        identity.userId != '${session.userId}' ||
        _generation != generation) {
      throw const ApiFailure('Захирлын бүртгэлийг баталгаажуулж чадсангүй.', 403);
    }
    _token = session.token;
  }

  Future<List<DirectorService>> services() async {
    final result = await _call('GET', '/api/admin/services');
    return (result as List)
        .map((item) => DirectorService.fromJson(object(item)))
        .where((item) => item.clinicId == settings.clinicId)
        .toList();
  }

  // Director-only read of the existing doctor roster; no placeholder doctors.
  Future<List<DirectorDoctor>> doctors() async {
    final result = await _call('GET', '/api/admin/Doctors');
    return (result as List)
        .map((item) => DirectorDoctor.fromJson(object(item)))
        .toList();
  }

  // The backend requires an existing active Doctor-role user in this clinic.
  // Creating a profile never grants a user the Doctor role.
  Future<DirectorDoctor> createDoctorProfile({
    required int userId,
    required String specialty,
    required int practiceStartYear,
    String? biography,
  }) async {
    final response = await _call(
      'POST',
      '/api/admin/Doctors',
      accepted: const {201},
      body: {
        'userId': userId,
        'profile': {
          'specialty': specialty.trim(),
          'practiceStartYear': practiceStartYear,
          'biography': biography?.trim(),
          'photoPath': null,
        },
        'isPublished': false,
      },
    );
    return DirectorDoctor.fromJson(object(response));
  }

  Future<DirectorDoctor> updateDoctorProfile(
    DirectorDoctor doctor, {
    required String specialty,
    required int practiceStartYear,
    String? biography,
    required bool isPublished,
    required bool isActive,
  }) async {
    final response = await _call(
      'PUT',
      '/api/admin/Doctors/${doctor.id}',
      body: {
        'version': doctor.version,
        'profile': {
          'specialty': specialty.trim(),
          'practiceStartYear': practiceStartYear,
          'biography': biography?.trim(),
          'photoPath': doctor.photoPath,
        },
        'isPublished': isPublished,
        'isActive': isActive,
      },
    );
    final updated = DirectorDoctor.fromJson(object(response));
    if (updated.id != doctor.id) {
      throw const ApiFailure('Эмчийн мэдээллийн хариу зөрчилтэй байна.');
    }
    return updated;
  }

  Future<DirectorDoctor> updateDoctorHours(
    DirectorDoctor doctor,
    List<DirectorWorkingHour> intervals,
  ) async {
    if (intervals.length > 70 ||
        intervals.any((h) =>
            h.dayOfWeek < 0 || h.dayOfWeek > 6 ||
            h.startMinute < 0 || h.endMinute > 1440 ||
            h.startMinute >= h.endMinute)) {
      throw const ApiFailure('Ажлын цагийн мэдээлэл буруу байна.', 400);
    }
    for (var day = 0; day <= 6; day++) {
      final hours = intervals.where((h) => h.dayOfWeek == day).toList()
        ..sort((a, b) => a.startMinute.compareTo(b.startMinute));
      for (var i = 1; i < hours.length; i++) {
        if (hours[i].startMinute < hours[i - 1].endMinute) {
          throw const ApiFailure('Ажлын цагууд давхцаж байна.', 400);
        }
      }
    }
    final response = await _call(
      'PUT',
      '/api/admin/Doctors/${doctor.id}/working-hours',
      body: {
        'version': doctor.version,
        'intervals': intervals
            .map((h) => {
                  'dayOfWeek': h.dayOfWeek,
                  'startMinute': h.startMinute,
                  'endMinute': h.endMinute,
                })
            .toList(),
      },
    );
    final updated = DirectorDoctor.fromJson(object(response));
    if (updated.id != doctor.id) {
      throw const ApiFailure('Ажлын цагийн хариу зөрчилтэй байна.');
    }
    return updated;
  }

  Future<DirectorService> saveService({
    int? id,
    required String name,
    required String description,
    required int minPrice,
    int? maxPrice,
    String? aftercareText,
  }) async {
    final result = await _call(
      id == null ? 'POST' : 'PUT',
      id == null ? '/api/admin/services' : '/api/admin/services/$id',
      accepted: id == null ? const {201} : const {200},
      body: {
        'name': name.trim(),
        'description': description.trim(),
        'minPrice': minPrice,
        'maxPrice': maxPrice,
        'aftercareText': aftercareText?.trim().isEmpty == true ? null : aftercareText?.trim(),
      },
    );
    final service = DirectorService.fromJson(object(result));
    if (service.clinicId != settings.clinicId || (id != null && service.id != id)) {
      throw const ApiFailure('Үйлчилгээний хариу өөр эмнэлэгтэй зөрчилдлөө.');
    }
    return service;
  }

  Future<DirectorService> setActive(DirectorService service, bool active) async {
    final path = '/api/admin/services/${service.id}/${active ? 'activate' : 'deactivate'}';
    final result = DirectorService.fromJson(object(await _call('POST', path)));
    if (result.clinicId != settings.clinicId ||
        result.id != service.id ||
        result.isActive != active) {
      throw const ApiFailure('Үйлчилгээний төлөвийг баталгаажуулж чадсангүй.');
    }
    return result;
  }
}

class DirectorService {
  final int id, clinicId, minPrice;
  final int? maxPrice;
  final String name, description;
  final String? aftercareText;
  final bool isActive;

  const DirectorService({
    required this.id,
    required this.clinicId,
    required this.name,
    required this.description,
    required this.minPrice,
    required this.maxPrice,
    required this.aftercareText,
    required this.isActive,
  });

  factory DirectorService.fromJson(Json json) => DirectorService(
    id: number(json, 'id'),
    clinicId: number(json, 'clinicId'),
    name: text(json, 'name'),
    description: text(json, 'description'),
    minPrice: number(json, 'minPrice'),
    maxPrice: json['maxPrice'] as int?,
    aftercareText: json['aftercareText'] as String?,
    isActive: json['isActive'] as bool,
  );
}

class DirectorDoctor {
  final int id, practiceStartYear;
  final String fullName, specialty, version;
  final String? biography, photoPath;
  final bool isActive, isPublished;
  final List<DirectorWorkingHour> workingHours;

  const DirectorDoctor({
    required this.id,
    required this.fullName,
    required this.specialty,
    required this.practiceStartYear,
    required this.version,
    required this.biography,
    required this.photoPath,
    required this.isActive,
    required this.isPublished,
    required this.workingHours,
  });

  factory DirectorDoctor.fromJson(Json json) => DirectorDoctor(
    id: number(json, 'id'),
    fullName: text(json, 'fullName'),
    specialty: text(json, 'specialty'),
    practiceStartYear: number(json, 'practiceStartYear'),
    version: text(json, 'version'),
    biography: json['biography'] as String?,
    photoPath: json['photoPath'] as String?,
    isActive: json['isActive'] as bool,
    isPublished: json['isPublished'] as bool,
    workingHours: (json['workingHours'] as List)
        .map((v) => DirectorWorkingHour.fromJson(object(v)))
        .toList(),
  );
}

class DirectorWorkingHour {
  final int dayOfWeek, startMinute, endMinute;

  const DirectorWorkingHour({
    required this.dayOfWeek,
    required this.startMinute,
    required this.endMinute,
  });

  factory DirectorWorkingHour.fromJson(Json json) => DirectorWorkingHour(
    dayOfWeek: number(json, 'dayOfWeek'),
    startMinute: number(json, 'startMinute'),
    endMinute: number(json, 'endMinute'),
  );
}
