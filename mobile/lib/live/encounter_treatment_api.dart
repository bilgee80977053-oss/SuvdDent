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

const _noteMaxResponseBytes = 16384;
final _clinicalLineEndings = RegExp(r'\r\n|[\r\u000c\u0085\u2028\u2029]');
final _unsupportedNoteControls = RegExp(r'[\x00-\x09\x0b-\x1f\x7f-\x9f]');

void _validateWriteNote(String note) {
  final normalized = note.replaceAll(_clinicalLineEndings, '\n').trim();
  if (note.length > 2000 ||
      normalized.isEmpty ||
      _unsupportedNoteControls.hasMatch(normalized)) {
    throw const ApiFailure('Тэмдэглэл 1–2000 тэмдэгттэй байх ёстой.', 400);
  }
}

enum EncounterAddendumSignResult { recorded, replayed }

enum EncounterWriteReconciliation { originalNote, keyedAddendum }

/// The server may have committed a write whose result could not be verified.
/// The caller must explicitly use the same-encounter audited read. No payload,
/// server error text, note, or retry instruction is stored here.
final class EncounterWriteOutcomeUncertain extends ApiFailure {
  final EncounterWriteReconciliation reconciliation;

  const EncounterWriteOutcomeUncertain._(this.reconciliation, int? status)
    : super(
        reconciliation == EncounterWriteReconciliation.originalNote
            ? 'Хадгалсан эсэхийг баталгаажуулж чадсангүй. Үзлэгийн тэмдэглэлийг уншиж шалгана уу.'
            : 'Нэмэлт хадгалагдсан эсэхийг баталгаажуулж чадсангүй. Ижил дугаараар уншиж шалгана уу.',
        status,
      );
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

const _procedureMaxResponseBytes = 65536;
final _toothCode = RegExp(r'^(?:[1-4][1-8]|[5-8][1-5])$');

enum PerformedProcedureScope { visit, singleTooth }

PerformedProcedureScope _scope(String value) => switch (value) {
  'Visit' => PerformedProcedureScope.visit,
  'SingleTooth' => PerformedProcedureScope.singleTooth,
  _ => throw const FormatException(),
};

String _scopeValue(PerformedProcedureScope value) => switch (value) {
  PerformedProcedureScope.visit => 'Visit',
  PerformedProcedureScope.singleTooth => 'SingleTooth',
};

bool _validTarget(PerformedProcedureScope scope, String? toothCode) =>
    scope == PerformedProcedureScope.visit
    ? toothCode == null
    : toothCode != null &&
          toothCode.length == 2 &&
          _toothCode.hasMatch(toothCode);

bool _positiveId(int id) => id >= 1 && id <= 2147483647;

class PerformedProcedure {
  final String requestId, serviceLabel;
  final int serviceId;
  final PerformedProcedureScope scope;
  final String? toothCode;
  final DateTime signedUtc;

  PerformedProcedure._(Json data)
    : requestId = _requestId(text(data, 'requestId')),
      serviceId = number(data, 'serviceId'),
      serviceLabel = text(data, 'serviceLabel'),
      scope = _scope(text(data, 'scope')),
      toothCode = data['toothCode'] as String?,
      signedUtc = instant(data, 'signedUtc') {
    if (!_positiveId(serviceId) ||
        serviceLabel.trim().length < 2 ||
        serviceLabel.length > 160 ||
        serviceLabel.runes.any((c) => c < 32 || (c >= 127 && c <= 159)) ||
        !data.containsKey('toothCode') ||
        !_validTarget(scope, toothCode)) {
      throw const FormatException();
    }
  }
}

class PerformedProcedurePage {
  final int page, pageSize;
  final bool hasMore;
  final List<PerformedProcedure> items;

  PerformedProcedurePage._(Json data, int requestedPage, int requestedSize)
    : page = number(data, 'page'),
      pageSize = number(data, 'pageSize'),
      hasMore = data['hasMore'] as bool,
      items = List.unmodifiable(
        (data['items'] as List).map(
          (item) => PerformedProcedure._(object(item)),
        ),
      ) {
    if (page != requestedPage ||
        pageSize != requestedSize ||
        items.length > pageSize ||
        (hasMore && items.length != pageSize) ||
        items.map((item) => item.requestId).toSet().length != items.length) {
      throw const FormatException();
    }
  }
}

const _patientHistoryMaxResponseBytes = 8192;
const _assignedMaxResponseBytes = 32768;
const _boundedClinicalMaxPage = 100;
final _clinicalInstant = RegExp(
  r'^([0-9]{4}-[0-9]{2}-[0-9]{2}T(?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9])(?:\.([0-9]{1,7}))?(?:Z|[+]00:00)$',
);

bool _utcDate(DateTime value) =>
    value.isUtc &&
    value.year >= 1 &&
    value.year <= 9999 &&
    value.hour == 0 &&
    value.minute == 0 &&
    value.second == 0 &&
    value.millisecond == 0 &&
    value.microsecond == 0;

String _isoDate(DateTime utc) => utc.toIso8601String().substring(0, 10);

bool _validClinicalPeriod(DateTime from, DateTime until) =>
    _utcDate(from) &&
    _utcDate(until) &&
    until.difference(from).inDays >= 1 &&
    until.difference(from).inDays <= 31;

bool _validClinicalPage(int page, int size) =>
    page >= 1 && page <= _boundedClinicalMaxPage && size >= 1 && size <= 20;

(DateTime, String) _parseClinicalUtc(String value) {
  final match = _clinicalInstant.firstMatch(value);
  if (match == null || match.end != value.length) throw const FormatException();
  final parsed = DateTime.parse(value).toUtc();
  final prefix = match[1]!;
  // DateTime.parse normalizes impossible calendar dates; don't accept them.
  if (parsed.year < 1 || parsed.toIso8601String().substring(0, 19) != prefix) {
    throw const FormatException();
  }
  // Retain a comparison key for .NET's seventh fractional digit.
  return (parsed, prefix + (match[2] ?? '').padRight(7, '0'));
}

bool _outOfClinicalOrder(
  String key,
  int id,
  String? previousKey,
  int? previousId,
) =>
    previousKey != null &&
    (key.compareTo(previousKey) > 0 ||
        (key == previousKey && id >= previousId!));

class AssignedEncounter {
  final int appointmentId, serviceId;
  final DateTime startUtc, endUtc;
  final String serviceLabel;

  const AssignedEncounter._(
    this.appointmentId,
    this.startUtc,
    this.endUtc,
    this.serviceId,
    this.serviceLabel,
  );

  static (AssignedEncounter, String) _parse(Json data) {
    final appointment = number(data, 'appointmentId');
    final service = number(data, 'serviceId');
    final label = text(data, 'serviceLabel');
    if (data.length != 5 ||
        !_positiveId(appointment) ||
        !_positiveId(service) ||
        label.length < 2 ||
        label.length > 160 ||
        label.trim().isEmpty ||
        label.runes.any((c) => c < 32 || (c >= 127 && c <= 159))) {
      throw const FormatException();
    }
    final (start, startKey) = _parseClinicalUtc(text(data, 'startUtc'));
    final (end, endKey) = _parseClinicalUtc(text(data, 'endUtc'));
    if (endKey.compareTo(startKey) <= 0) throw const FormatException();
    return (
      AssignedEncounter._(appointment, start, end, service, label),
      startKey,
    );
  }
}

class AssignedEncounterPage {
  final int page, pageSize;
  final List<AssignedEncounter> items;

  const AssignedEncounterPage._(this.page, this.pageSize, this.items);

  static AssignedEncounterPage _parse(
    Json data,
    int page,
    int size,
    DateTime fromUtc,
    DateTime untilUtc,
  ) {
    final rawItems = data['items'] as List;
    if (data.length != 3 ||
        number(data, 'page') != page ||
        number(data, 'pageSize') != size ||
        rawItems.length > size) {
      throw const FormatException();
    }

    final items = <AssignedEncounter>[];
    final seen = <int>{};
    String? previousKey;
    int? previousId;
    for (final raw in rawItems) {
      final (item, orderKey) = AssignedEncounter._parse(object(raw));
      if (!seen.add(item.appointmentId) ||
          item.startUtc.isBefore(fromUtc) ||
          !item.startUtc.isBefore(untilUtc) ||
          _outOfClinicalOrder(
            orderKey,
            item.appointmentId,
            previousKey,
            previousId,
          )) {
        throw const FormatException();
      }
      items.add(item);
      previousKey = orderKey;
      previousId = item.appointmentId;
    }
    return AssignedEncounterPage._(page, size, List.unmodifiable(items));
  }
}

class PatientHistoryVisit {
  final int appointmentId;
  final DateTime startedUtc;

  const PatientHistoryVisit._(this.appointmentId, this.startedUtc);

  static (PatientHistoryVisit, String) _parse(Json data) {
    final id = number(data, 'appointmentId');
    if (data.length != 2 || !_positiveId(id)) throw const FormatException();
    final (started, orderKey) = _parseClinicalUtc(text(data, 'startedUtc'));
    return (PatientHistoryVisit._(id, started), orderKey);
  }
}

class PatientHistoryPage {
  final int page, pageSize;
  final bool hasMore;
  final List<PatientHistoryVisit> items;

  const PatientHistoryPage._(
    this.page,
    this.pageSize,
    this.hasMore,
    this.items,
  );

  static PatientHistoryPage _parse(
    Json data,
    int page,
    int pageSize,
    DateTime fromUtc,
    DateTime untilUtc,
  ) {
    final hasMore = data['hasMore'] as bool;
    final rawItems = data['items'] as List;
    if (data.length != 4 ||
        number(data, 'page') != page ||
        number(data, 'pageSize') != pageSize ||
        rawItems.length > pageSize ||
        (hasMore &&
            (rawItems.length != pageSize || page == _boundedClinicalMaxPage))) {
      throw const FormatException();
    }

    final visits = <PatientHistoryVisit>[];
    final seen = <int>{};
    String? previousKey;
    int? previousId;
    for (final rawItem in rawItems) {
      final (visit, orderKey) = PatientHistoryVisit._parse(object(rawItem));
      if (!seen.add(visit.appointmentId) ||
          visit.startedUtc.isBefore(fromUtc) ||
          !visit.startedUtc.isBefore(untilUtc) ||
          _outOfClinicalOrder(
            orderKey,
            visit.appointmentId,
            previousKey,
            previousId,
          )) {
        throw const FormatException();
      }
      visits.add(visit);
      previousKey = orderKey;
      previousId = visit.appointmentId;
    }
    return PatientHistoryPage._(
      page,
      pageSize,
      hasMore,
      List.unmodifiable(visits),
    );
  }
}

sealed class PerformedProcedureSignResult {
  const PerformedProcedureSignResult();
}

final class PerformedProcedureRecorded extends PerformedProcedureSignResult {
  final int appointmentId;
  final PerformedProcedure procedure;
  const PerformedProcedureRecorded._(this.appointmentId, this.procedure);
}

final class PerformedProcedureReplayed extends PerformedProcedureSignResult {
  const PerformedProcedureReplayed._();
}

/// Unwired review adapter scoped to an authorized encounter. Requires an explicit
/// transport and keeps only a verified doctor token in memory, never clinical
/// responses. The caller owns the returned records and must not persist them.
/// No default client, UI entry point or unrestricted patient search.
/// Writes are explicit, single-encounter only, and never retried automatically.
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

  void _discardResponse(http.StreamedResponse response) {
    // Transport cleanup never masks an authoritative HTTP status.
    final subscription = response.stream.listen((_) {}, onError: (Object _) {});
    unawaited(subscription.cancel().catchError((Object _) {}));
  }

  Future<T> _call<T>(
    String path,
    T Function(Json) parse, {
    required int generation,
    String? token,
    bool login = false,
    Json? body,
    Map<String, String>? query,
    int successStatus = 200,
    T Function()? onReplay,
    T Function()? onEmptySuccess,
    String? expectedLocationPath,
    int? maxResponseBytes,
  }) async {
    _checkGeneration(generation);
    if (!login && token == null) {
      throw const ApiFailure('Эмчийн эрхээр нэвтэрнэ үү.', 401);
    }
    final uri = Uri.parse(_settings.baseUrl)
        .replace(path: path, queryParameters: query);
    final request =
        http.Request(
            login || onReplay != null || onEmptySuccess != null
                ? 'POST'
                : 'GET',
            uri,
          )
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
        final isReplay = streamed.statusCode == 204 && onReplay != null;
        if (streamed.statusCode != successStatus && !isReplay) {
          _discardResponse(streamed);
          return (streamed.statusCode, null as List<int>?);
        }
        if (!isReplay &&
            expectedLocationPath != null &&
            !_validWriteLocation(
              _singleHeader(streamed.headers, 'location'),
              uri,
              expectedLocationPath,
            )) {
          _discardResponse(streamed);
          throw const FormatException();
        }
        final limit = isReplay || onEmptySuccess != null ? 0 : maxResponseBytes;
        if (limit != null && (streamed.contentLength ?? 0) > limit) {
          _discardResponse(streamed);
          throw const FormatException();
        }
        final bytes = <int>[];
        await for (final chunk in streamed.stream) {
          // A timed-out or replaced session may receive late transport data.
          _checkGeneration(generation);
          if (limit != null && chunk.length > limit - bytes.length) {
            throw const FormatException();
          }
          bytes.addAll(chunk);
        }
        return (streamed.statusCode, bytes);
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
      if (status == 204 && onReplay != null) return onReplay();
      if (onEmptySuccess != null) return onEmptySuccess();
      return parse(object(jsonDecode(utf8.decode(success))));
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

  static String? _singleHeader(Map<String, String> headers, String name) {
    String? match;
    for (final entry in headers.entries) {
      if (entry.key.toLowerCase() != name) continue;
      if (match != null) return null;
      match = entry.value;
    }
    return match;
  }

  static bool _validWriteLocation(String? raw, Uri target, String path) {
    final location = raw == null ? null : Uri.tryParse(raw);
    if (location == null ||
        location.path != path ||
        location.hasQuery ||
        location.hasFragment ||
        location.userInfo.isNotEmpty) {
      return false;
    }
    return !location.hasScheme && !location.hasAuthority
        ? raw!.startsWith('/')
        : location.scheme == target.scheme &&
              location.host == target.host &&
              location.port == target.port;
  }

  static Future<T> _noteWrite<T>(
    EncounterWriteReconciliation reconciliation,
    Future<T> Function() send,
  ) async {
    try {
      return await send();
    } on ApiFailure catch (failure) {
      // An authoritative rejection is never treated as a successful write.
      // Other HTTP responses, timeout, malformed success and transport errors
      // are ambiguous; an explicit audited GET is required, not another POST.
      if (failure.status == 409) {
        throw ApiFailure(
          reconciliation == EncounterWriteReconciliation.originalNote
              ? 'Үзлэгийн тэмдэглэл өмнө нь баталгаажсан. Солих боломжгүй.'
              : 'Энэ хүсэлтийн дугаараар өөр нэмэлт баталгаажсан байна.',
          409,
        );
      }
      if (const {
        400,
        401,
        403,
        404,
        413,
        415,
        422,
        429,
      }.contains(failure.status)) {
        rethrow;
      }
      throw EncounterWriteOutcomeUncertain._(reconciliation, failure.status);
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

  String _encounter(int appointmentId, {String resource = 'treatment'}) {
    if (!_positiveId(appointmentId)) {
      throw const ApiFailure('Захиалгын дугаар буруу байна.', 400);
    }
    return '/api/clinical/appointments/$appointmentId/$resource';
  }

  Future<EncounterTreatmentNote> readTreatment(int appointmentId) async =>
      _call(
        _encounter(appointmentId),
        (data) => EncounterTreatmentNote._(data, appointmentId),
        generation: _generation,
        token: _token,
        maxResponseBytes: _noteMaxResponseBytes,
      );

  /// On an uncertain result, explicitly call [readTreatment]. A duplicate 409
  /// is not equivalent to signing this note, and this method never retries.
  Future<void> signTreatment(int appointmentId, {required String note}) async {
    final path = _encounter(appointmentId);
    _validateWriteNote(note);
    await _noteWrite(
      EncounterWriteReconciliation.originalNote,
      () => _call<void>(
        path,
        (_) => throw const FormatException(),
        generation: _generation,
        token: _token,
        body: {'note': note},
        successStatus: 201,
        expectedLocationPath: path,
        onEmptySuccess: () {},
      ),
    );
  }

  /// The UUID is supplied/retained by the caller. On uncertainty, use the
  /// audited keyed read; this method never creates or replaces retry keys.
  Future<EncounterAddendumSignResult> signAddendum(
    int appointmentId, {
    required String requestId,
    required String note,
  }) async {
    final path = '${_encounter(appointmentId)}/addenda';
    final String id;
    try {
      id = _requestId(requestId);
    } on FormatException {
      throw const ApiFailure('Нэмэлтийн хүсэлтийн дугаар буруу байна.', 400);
    }
    _validateWriteNote(note);
    return _noteWrite(
      EncounterWriteReconciliation.keyedAddendum,
      () => _call<EncounterAddendumSignResult>(
        path,
        (_) => throw const FormatException(),
        generation: _generation,
        token: _token,
        body: {'requestId': id, 'note': note},
        successStatus: 201,
        expectedLocationPath: '$path/$id',
        onEmptySuccess: () => EncounterAddendumSignResult.recorded,
        onReplay: () => EncounterAddendumSignResult.replayed,
      ),
    );
  }

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
      maxResponseBytes: _noteMaxResponseBytes,
    );
  }

  Future<PerformedProcedurePage> listPerformedProcedures(
    int appointmentId, {
    int page = 1,
    int pageSize = 20,
  }) async {
    if (page < 1 || page > 1000 || pageSize < 1 || pageSize > 50) {
      throw const ApiFailure('Хуудасны дугаар эсвэл хэмжээ буруу байна.', 400);
    }
    return _call(
      _encounter(appointmentId, resource: 'performed-procedures'),
      (data) => PerformedProcedurePage._(data, page, pageSize),
      generation: _generation,
      token: _token,
      query: {'page': '$page', 'pageSize': '$pageSize'},
      maxResponseBytes: _procedureMaxResponseBytes,
    );
  }

  /// Discovery for this authorized doctor only; a full page does not imply
  /// additional rows. Selection is never entitlement for a subsequent action.
  Future<AssignedEncounterPage> listAssignedEncounters({
    required DateTime fromUtc,
    required DateTime untilUtc,
    int page = 1,
    int pageSize = 10,
  }) async {
    if (!_validClinicalPeriod(fromUtc, untilUtc) ||
        !_validClinicalPage(page, pageSize)) {
      throw const ApiFailure(
        'Огнооны хүрээ эсвэл хуудасны хэмжээ буруу байна.',
        400,
      );
    }
    return _call(
      '/api/clinical/appointments/assigned',
      (data) =>
          AssignedEncounterPage._parse(data, page, pageSize, fromUtc, untilUtc),
      generation: _generation,
      token: _token,
      query: {
        'from': _isoDate(fromUtc),
        'until': _isoDate(untilUtc),
        'page': '$page',
        'pageSize': '$pageSize',
      },
      maxResponseBytes: _assignedMaxResponseBytes,
    );
  }

  Future<PatientHistoryPage> listPatientPeriodHistory(
    int anchorAppointmentId, {
    required DateTime fromUtc,
    required DateTime untilUtc,
    int page = 1,
    int pageSize = 10,
  }) async {
    if (!_validClinicalPeriod(fromUtc, untilUtc) ||
        !_validClinicalPage(page, pageSize)) {
      throw const ApiFailure(
        'Огнооны хүрээ эсвэл хуудасны хэмжээ буруу байна.',
        400,
      );
    }
    return _call(
      _encounter(anchorAppointmentId, resource: 'patient-history'),
      (data) =>
          PatientHistoryPage._parse(data, page, pageSize, fromUtc, untilUtc),
      generation: _generation,
      token: _token,
      query: {
        'from': _isoDate(fromUtc),
        'until': _isoDate(untilUtc),
        'page': '$page',
        'pageSize': '$pageSize',
      },
      maxResponseBytes: _patientHistoryMaxResponseBytes,
    );
  }

  Future<PerformedProcedureSignResult> signPerformedProcedure(
    int appointmentId, {
    required String requestId,
    required int serviceId,
    required PerformedProcedureScope scope,
    String? toothCode,
  }) async {
    final String key;
    try {
      key = _requestId(requestId);
      if (!_positiveId(serviceId) || !_validTarget(scope, toothCode)) {
        throw const FormatException();
      }
    } on FormatException {
      throw const ApiFailure('Ажилбарын мэдээлэл буруу байна.', 400);
    }
    return _call<PerformedProcedureSignResult>(
      _encounter(appointmentId, resource: 'performed-procedures'),
      (data) {
        final procedure = PerformedProcedure._(data);
        if (number(data, 'appointmentId') != appointmentId ||
            procedure.requestId != key ||
            procedure.serviceId != serviceId ||
            procedure.scope != scope ||
            procedure.toothCode != toothCode) {
          throw const FormatException();
        }
        return PerformedProcedureRecorded._(appointmentId, procedure);
      },
      generation: _generation,
      token: _token,
      body: {
        'requestId': key,
        'serviceId': serviceId,
        'scope': _scopeValue(scope),
        'toothCode': toothCode,
      },
      successStatus: 201,
      onReplay: PerformedProcedureReplayed._,
      maxResponseBytes: _procedureMaxResponseBytes,
    );
  }
}
