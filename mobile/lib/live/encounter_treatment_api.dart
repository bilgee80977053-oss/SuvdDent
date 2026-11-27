import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'dental_api.dart';
import 'models.dart';

final _guid = RegExp(
  r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
);
const _emptyGuid = '00000000-0000-0000-0000-000000000000';

String _requestId(String value) {
  if (value.length != 36 || !_guid.hasMatch(value) || value == _emptyGuid) {
    throw const FormatException();
  }
  return value.toLowerCase();
}

String _note(Json data) {
  final note = text(data, 'note');
  if (note.trim().isEmpty || note.length > 2000) {
    throw const FormatException();
  }
  return note;
}

class EncounterTreatmentNote {
  final int appointmentId;
  final String note;
  final DateTime signedUtc;

  EncounterTreatmentNote._(Json data, int expectedAppointment)
    : appointmentId = number(data, 'appointmentId'),
      note = _note(data),
      signedUtc = instant(data, 'signedUtc') {
    if (appointmentId != expectedAppointment) throw const FormatException();
  }
}

class EncounterTreatmentAddendum {
  final String requestId, note;
  final DateTime signedUtc;

  EncounterTreatmentAddendum._(Json data, String expectedRequestId)
    : requestId = _requestId(text(data, 'requestId')),
      note = _note(data),
      signedUtc = instant(data, 'signedUtc') {
    if (requestId != expectedRequestId) throw const FormatException();
  }
}

class EncounterAddendumReference {
  final String requestId;
  final DateTime signedUtc;

  EncounterAddendumReference._(Json data)
    : requestId = _requestId(text(data, 'requestId')),
      signedUtc = instant(data, 'signedUtc');
}

class EncounterAddendaPage {
  final int page, pageSize;
  final bool hasMore;
  final List<EncounterAddendumReference> items;

  EncounterAddendaPage._(Json data, int expectedPage, int expectedPageSize)
    : page = number(data, 'page'),
      pageSize = number(data, 'pageSize'),
      hasMore = data['hasMore'] as bool,
      items = List.unmodifiable(
        (data['items'] as List).map(
          (item) => EncounterAddendumReference._(object(item)),
        ),
      ) {
    if (page != expectedPage ||
        pageSize != expectedPageSize ||
        items.length > pageSize ||
        (hasMore && items.length < pageSize) ||
        items.map((item) => item.requestId).toSet().length != items.length) {
      throw const FormatException();
    }
  }
}

/// Unwired review adapter for one encounter at a time. Requires an explicit
/// transport and keeps only a verified doctor token in memory, never clinical
/// responses. The caller owns the returned records and must not persist them.
/// No default client, UI entry point, writes or patient-wide discovery.
class EncounterTreatmentApi {
  final ApiSettings _settings;
  final http.Client _client;
  final Duration _timeout;
  String? _token;
  int _generation = 0;
  bool _closed = false;

  EncounterTreatmentApi(
    ApiSettings settings, {
    required this._client,
    this._timeout = const Duration(seconds: 20),
  }) : _settings = settings {
    if (!settings.isLive ||
        settings.error != null ||
        Uri.parse(settings.baseUrl).scheme != 'https') {
      throw ArgumentError('An explicit HTTPS test clinic is required.');
    }
  }

  bool get authenticated => _token != null;

  void logout() {
    _generation++;
    _token = null;
  }

  void close() {
    if (_closed) return;
    _closed = true;
    logout();
    _client.close();
  }

  void _checkGeneration(int generation) {
    if (_closed || _generation != generation) {
      throw const ApiFailure('Нэвтрэх төлөв өөрчлөгдсөн.', 401);
    }
  }

  Future<T> _call<T>(
    String path,
    T Function(Json) parse, {
    required int generation,
    String? token,
    bool login = false,
    Json? body,
    Map<String, String>? query,
  }) async {
    _checkGeneration(generation);
    if (!login && token == null) {
      throw const ApiFailure('Эмчийн эрхээр нэвтэрнэ үү.', 401);
    }
    final uri = Uri.parse(_settings.baseUrl)
        .replace(path: path, queryParameters: query);
    final request = http.Request(login ? 'POST' : 'GET', uri)
      ..followRedirects = false
      ..headers['Accept'] = 'application/json'
      ..headers['Cache-Control'] = 'no-store';
    if (token != null) request.headers['Authorization'] = 'Bearer $token';
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    try {
      // Refuse unexpected redirect/error bodies without reading their content.
      final response = await (() async {
        final streamed = await _client.send(request);
        if (streamed.statusCode != 200) {
          await streamed.stream.listen((_) {}).cancel();
          return (streamed.statusCode, null);
        }
        return (200, await http.Response.fromStream(streamed));
      })().timeout(_timeout);
      _checkGeneration(generation);
      final (status, success) = response;
      if (success == null) {
        if (status == 401 && token != null) logout();
        throw ApiFailure(
          status == 503
              ? 'Тэмдэглэл эсвэл хандалтын бүртгэлийг баталгаажуулж чадсангүй.'
              : DentalApi.messageFor(status),
          status,
        );
      }
      return parse(object(jsonDecode(utf8.decode(success.bodyBytes))));
    } on ApiFailure {
      rethrow;
    } on TimeoutException {
      _checkGeneration(generation);
      throw const ApiFailure('Холболтын хугацаа дууслаа.');
    } on http.ClientException {
      _checkGeneration(generation);
      throw const ApiFailure('Сервертэй холбогдож чадсангүй.');
    } catch (_) {
      _checkGeneration(generation);
      throw const ApiFailure('Серверийн хариу буруу байна.');
    }
  }

  Future<void> login(String identifier, String password) async {
    logout();
    final generation = _generation;
    final session = await _call(
      '/api/Auth/login',
      AuthSession.fromJson,
      generation: generation,
      login: true,
      body: {
        'identifier': identifier,
        'password': password,
        'clinicId': _settings.clinicId,
      },
    );
    _checkGeneration(generation);
    if (session.role != 'Doctor' || session.clinicId != _settings.clinicId) {
      throw const ApiFailure('Энэ эмнэлгийн эмчийн бүртгэл шаардлагатай.', 403);
    }
    final identity = await _call(
      '/api/Auth/me',
      AuthIdentity.fromJson,
      generation: generation,
      token: session.token,
    );
    _checkGeneration(generation);
    if (identity.role != 'Doctor' ||
        identity.userId != '${session.userId}' ||
        identity.clinicId != '${_settings.clinicId}') {
      throw const ApiFailure('Эмчийн бүртгэлийг баталгаажуулж чадсангүй.', 403);
    }
    _token = session.token;
  }

  String _encounter(int appointmentId) {
    if (appointmentId < 1 || appointmentId > 2147483647) {
      throw const ApiFailure('Захиалгын дугаар буруу байна.', 400);
    }
    return '/api/clinical/appointments/$appointmentId/treatment';
  }

  Future<EncounterTreatmentNote> readTreatment(int appointmentId) async =>
      _call(
        _encounter(appointmentId),
        (data) => EncounterTreatmentNote._(data, appointmentId),
        generation: _generation,
        token: _token,
      );

  Future<EncounterAddendaPage> listAddenda(
    int appointmentId, {
    int page = 1,
    int pageSize = 20,
  }) async {
    if (page < 1 || page > 1000 || pageSize < 1 || pageSize > 50) {
      throw const ApiFailure('Хуудасны дугаар эсвэл хэмжээ буруу байна.', 400);
    }
    return _call(
      '${_encounter(appointmentId)}/addenda',
      (data) => EncounterAddendaPage._(data, page, pageSize),
      generation: _generation,
      token: _token,
      query: {'page': '$page', 'pageSize': '$pageSize'},
    );
  }

  Future<EncounterTreatmentAddendum> readAddendum(
    int appointmentId,
    String requestId,
  ) async {
    final String id;
    try {
      id = _requestId(requestId);
    } on FormatException {
      throw const ApiFailure('Нэмэлтийн хүсэлтийн дугаар буруу байна.', 400);
    }
    return _call(
      '${_encounter(appointmentId)}/addenda/$id',
      (data) => EncounterTreatmentAddendum._(data, id),
      generation: _generation,
      token: _token,
    );
  }
}
