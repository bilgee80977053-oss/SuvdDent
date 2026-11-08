import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';

import '../../core/api.dart';
import '../../core/models.dart';
import '../../core/secure_store.dart';

class BookingIntent {
  final int clinicId;
  final String key;
  final Json body;
  BookingIntent(this.clinicId, this.key, Json body)
    : body = Map.unmodifiable(body);
  factory BookingIntent.fromJson(Json j) {
    final body = jsonObject(j['body']);
    if (j['clinicId'] is! int ||
        j['key'] is! String ||
        body['doctorProfileId'] is! int ||
        body['serviceId'] is! int ||
        body['startUtc'] is! String ||
        body['clinicVersion'] is! String ||
        body['doctorVersion'] is! String ||
        body['consentToCreateNewPatient'] is! bool) {
      throw const FormatException();
    }
    DateTime.parse(body['startUtc'] as String);
    return BookingIntent(j['clinicId'] as int, j['key'] as String, body);
  }
  int get doctorId => body['doctorProfileId'] as int;
  int get serviceId => body['serviceId'] as int;
  DateTime get startUtc => DateTime.parse(body['startUtc'] as String).toUtc();
  Json toJson() => {'clinicId': clinicId, 'key': key, 'body': body};
  String get route =>
      '/clinics/$clinicId/doctors/$doctorId/services/$serviceId/book';
}

class BookingController extends ChangeNotifier {
  final NexoraApi api;
  final SessionStore store;
  final int accountId;
  BookingIntent? pending;
  ApiFailure? error;
  bool submitting = false,
      consentRequired = false,
      needsRefresh = false,
      storageBlocked = false;
  bool _disposed = false;
  BookingController(this.api, this.store, this.accountId);
  void _changed() {
    if (!_disposed) {
      notifyListeners();
    }
  }

  Future<void> restore() async {
    try {
      final raw = await store.pending(accountId);
      pending = raw == null ? null : BookingIntent.fromJson(raw);
      storageBlocked = false;
    } catch (_) {
      storageBlocked = true;
      error = const ApiFailure('STORAGE_ERROR');
    }
    _changed();
  }

  Future<Appointment?> submit(
    BookingOptions options,
    DateTime startUtc,
    bool consent,
  ) async {
    if (submitting || storageBlocked) {
      return null;
    }
    if (pending != null &&
        (pending!.clinicId != options.clinicId ||
            pending!.doctorId != options.doctorProfileId ||
            pending!.serviceId != options.serviceId)) {
      error = const ApiFailure('PENDING_BOOKING_EXISTS');
      _changed();
      return null;
    }
    submitting = true;
    error = null;
    _changed();
    try {
      if (pending == null) {
        final intent = BookingIntent(options.clinicId, const Uuid().v4(), {
          'doctorProfileId': options.doctorProfileId,
          'serviceId': options.serviceId,
          'startUtc': startUtc.toUtc().toIso8601String(),
          'clinicVersion': options.clinicVersion,
          'doctorVersion': options.doctorVersion,
          'consentToCreateNewPatient': consent,
        });
        try {
          await store.savePending(accountId, intent.toJson());
        } catch (_) {
          throw const ApiFailure('STORAGE_ERROR');
        }
        pending = intent; // Persist BEFORE making the request. Retries never rebuild this payload.
      }
      final operation = pending!;
      final appointment = await api.book(
        operation.clinicId,
        operation.key,
        operation.body,
      );
      try {
        await store.clearPending(accountId);
        pending = null;
      } catch (_) {
        /* Keep the key; a later replay retrieves this same successful booking. */
      }
      return appointment;
    } on ApiFailure catch (e) {
      error = e;
      final keep =
          e.ambiguous ||
          e.status == 401 ||
          e.code == 'BOOKING_BUSY' ||
          e.code == 'IDEMPOTENCY_KEY_REUSED';
      if (!keep && pending != null && e.code != 'STORAGE_ERROR') {
        try {
          await store.clearPending(accountId);
          pending = null;
        } catch (_) {
          storageBlocked = true;
        }
      }
      consentRequired =
          e.code == 'PATIENT_CREATION_CONSENT_REQUIRED' || consentRequired;
      needsRefresh =
          !keep &&
          e.code != 'PATIENT_CREATION_CONSENT_REQUIRED' &&
          e.code != 'STORAGE_ERROR';
      return null;
    } catch (_) {
      error = const ApiFailure('INVALID_SERVER_RESPONSE', ambiguous: true);
      return null; // Unknown completion: never invent a new key.
    } finally {
      submitting = false;
      _changed();
    }
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
