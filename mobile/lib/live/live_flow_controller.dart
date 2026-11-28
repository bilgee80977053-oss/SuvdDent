import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:timezone/data/latest.dart' as data;
import 'package:timezone/timezone.dart' as tz;

import 'dental_api.dart';
import 'models.dart';

String calendarDate(DateTime d) =>
    '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

abstract final class ClinicClock {
  static bool _ready = false;
  static tz.Location location(String zone) {
    if (!_ready) {
      data.initializeTimeZones();
      _ready = true;
    }
    // Backend deployments should use verified IANA IDs. Never guess offsets.
    try {
      return tz.getLocation(zone);
    } catch (_) {
      throw const ApiFailure(
        'Эмнэлгийн цагийн бүсийг харуулах боломжгүй. Администраторт хандана уу.',
      );
    }
  }

  static String label(DateTime utc, String zone) {
    final local = tz.TZDateTime.from(utc, location(zone));
    final offset = local.timeZoneOffset;
    final sign = offset.isNegative ? '-' : '+';
    final minutes = offset.inMinutes.abs();
    return '${calendarDate(local)} ${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')} ($zone UTC$sign${(minutes ~/ 60).toString().padLeft(2, '0')}:${(minutes % 60).toString().padLeft(2, '0')})';
  }
}

String newRequestId() {
  final random = Random.secure();
  final bytes = List<int>.generate(16, (_) => random.nextInt(256));
  bytes[6] = (bytes[6] & 15) | 64;
  bytes[8] = (bytes[8] & 63) | 128;
  final hex = bytes.map((v) => v.toRadixString(16).padLeft(2, '0')).join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
}

class RescheduleDraft {
  final Appointment source;
  String date;
  SlotPage? availability;
  BookingSlot? selection;
  String? _requestId;
  bool uncertain = false;
  Appointment? replacement;

  RescheduleDraft(this.source, this.date);

  void clearSelection() {
    selection = null;
    _requestId = null;
  }
}

class LiveFlowController extends ChangeNotifier {
  DentalApi api;
  final String Function() requestIdFactory;
  int step = 0, _epoch = 0;
  bool busy = false,
      _disposed = false,
      profileMissing = false,
      conflict = false,
      uncertainBooking = false;
  String? error, notice;
  AuthSession? session;
  AuthIdentity? identity;
  VerificationReceipt? registration;
  PatientProfile? patient;
  List<BookingService> services = [];
  List<BookingDoctor> doctors = [];
  BookingService? service;
  BookingDoctor? doctor;
  SlotPage? availability;
  BookingSlot? slot;
  String? requestId;
  Appointment? appointment;
  AppointmentPage? history;
  RescheduleDraft? reschedule;
  NotificationPreferences? notificationPreferences;
  bool emailConsent = false, smsConsent = false;
  final Set<int> uncertainCancellations = {};
  String date = calendarDate(DateTime.now());
  LiveFlowController(this.api, {this.requestIdFactory = newRequestId}) {
    api.onUnauthorized = _unauthorized;
  }

  void _unauthorized() {
    logout();
    error = DentalApi.messageFor(401);
    changed();
  }

  void changed() {
    if (!_disposed) notifyListeners();
  }

  Future<void> run(Future<void> Function(bool Function()) action) async {
    if (busy || _disposed) return;
    final epoch = _epoch;
    bool current() => epoch == _epoch && !_disposed;
    busy = true;
    error = null;
    notice = null;
    changed();
    try {
      await action(current);
    } on ApiFailure catch (failure) {
      if (current()) error = failure.message;
    } catch (_) {
      if (current()) {
        error = 'Мэдээллийг боловсруулахад алдаа гарлаа. Дахин оролдоно уу.';
      }
    } finally {
      if (current()) {
        busy = false;
        changed();
      }
    }
  }

  void logout() {
    _epoch++;
    api.clearSession();
    busy = false;
    step = 0;
    session = null;
    identity = null;
    registration = null;
    registrationRetryAfterSeconds = null;
    patient = null;
    services = [];
    doctors = [];
    service = null;
    doctor = null;
    availability = null;
    slot = null;
    requestId = null;
    appointment = null;
    history = null;
    uncertainCancellations.clear();
    reschedule = null;
    notificationPreferences = null;
    emailConsent = false;
    smsConsent = false;
    error = null;
    notice = null;
    profileMissing = false;
    conflict = false;
    uncertainBooking = false;
    date = calendarDate(DateTime.now());
    changed();
  }

  void changeClinic(DentalApi next) {
    logout();
    api.onUnauthorized = null;
    api.close();
    api = next;
    api.onUnauthorized = _unauthorized;
    changed();
  }

  void enterClinic() {
    if (!busy) {
      step = 1;
      changed();
    }
  }

  void back() {
    if (!busy && !uncertainBooking && appointment == null && step > 0) {
      step--;
      error = null;
      changed();
    }
  }

  Future<void> login(String identifier, String password) async {
    if (busy) return;
    logout();
    enterClinic();
    await run((current) async {
      final result = await api.login(identifier, password);
      if (!current()) return;
      session = result;
      AuthIdentity who;
      try {
        who = await api.me();
      } catch (_) {
        if (current()) {
          api.clearSession();
          session = null;
        }
        rethrow;
      }
      if (!current()) return;
      identity = who;
      step = 2;
      notice = 'Нэвтрэлт амжилттай.';
    });
  }

  Future<void> register(
    String name,
    String email,
    String password,
    String? phone,
  ) async {
    if (busy) return;
    logout();
    enterClinic();
    await run((current) async {
      final result = await api.register(name, email, password, phone);
      if (!current()) return;
      registration = result;
      step = 2;
      notice = result.message;
    });
  }

  Future<void> registerWithMethod(
    String name,
    String email,
    String password,
    String? phone,
    RegistrationMethod method,
  ) => run((current) async {
    registrationRetryAfterSeconds = null;
    try {
      final result = await api.register(
        name,
        email,
        password,
        phone,
        verificationMethod: method,
      );
      if (!current()) return;
      if (result.userId <= 0 ||
          (result.registrationToken?.isEmpty ?? true) ||
          (result.method != null && result.method != method)) {
        throw const ApiFailure('Бүртгэлийн хариуг баталгаажуулж чадсангүй.');
      }
      registration = result;
      step = 2;
    } on RegistrationFailure catch (failure) {
      if (current()) {
        registrationRetryAfterSeconds = failure.retryAfterSeconds;
        if (failure.pendingRegistration != null) {
          registration = failure.pendingRegistration;
        }
      }
      rethrow;
    }
  });

  Future<void> requestRegistrationCode(RegistrationMethod method) =>
      run((current) async {
        registrationRetryAfterSeconds = null;
        final pending = registration;
        if (pending == null) return;
        try {
          final result = await api.requestRegistrationCode(pending, method);
          if (!current()) return;
          if (result.userId != pending.userId ||
              (result.registrationToken?.isEmpty ?? false) ||
              (result.method != null && result.method != method)) {
            throw const ApiFailure('Бүртгэлийн хариу таарахгүй байна.');
          }
          registration = result.registrationToken == null ? pending : result;
          step = 2;
        } on RegistrationFailure catch (failure) {
          if (current()) {
            if (failure.pendingRegistration?.userId == pending.userId) {
              registration = failure.pendingRegistration;
            }
            registrationRetryAfterSeconds = failure.retryAfterSeconds;
          }
          rethrow;
        }
      });

  int? registrationRetryAfterSeconds;

  Future<void> resendRegistrationCode(String email) => run((current) async {
    final message = await api.resendRegistrationCode(email);
    if (!current()) return;
    notice = message;
  });

  Future<void> verifyRegistrationEmail(String email, String code) =>
      run((current) async {
        final result = await api.verifyRegistrationEmail(email, code);
        if (!current()) return;
        registration = null;
        step = 1;
        notice = '${result.message} Одоо нууц үгээрээ нэвтэрнэ үү.';
      });

  Future<void> verify(String code) => run((current) async {
    if (registration == null) return;
    final pending = registration!;
    final result = await api.verify(
      pending.userId,
      code,
      registrationToken: pending.registrationToken,
    );
    if (!current()) return;
    registration = null;
    step = 1;
    notice = '${result.message} Нууц үгээрээ нэвтэрнэ үү.';
  });
  Future<void> loadProfile() => run((current) async {
    try {
      final result = await api.profile();
      if (!current()) return;
      patient = result;
      profileMissing = false;
      step = 3;
    } on ApiFailure catch (failure) {
      if (failure.status != 404) rethrow;
      if (!current()) return;
      patient = null;
      profileMissing = true;
      step = 3;
    }
  });
  Future<void> createProfile(String name, String phone) => run((current) async {
    final result = await api.createProfile(name, phone);
    if (!current()) return;
    patient = result;
    profileMissing = false;
  });
  // Reuses the backend's own-patient PUT; never sends a user or clinic ID.
  Future<void> updateProfile({
    required String fullName,
    required String phone,
    String? dateOfBirth,
    String? address,
  }) => run((current) async {
    if (session == null || patient == null) return;
    final updated = await api.updateProfile(
      fullName: fullName,
      phone: phone,
      dateOfBirth: dateOfBirth,
      address: address,
    );
    if (!current()) return;
    patient = updated;
    profileMissing = false;
    notice = 'Профайлын мэдээлэл хадгалагдлаа.';
  });

  Future<void> loadServices() => run((current) async {
    final result = await api.services();
    if (!current()) return;
    services = result;
    step = 4;
    if (!services.any((s) => s.id == service?.id)) {
      service = null;
      _clearDoctor();
    }
  });
  void _clearSlot() {
    slot = null;
    requestId = null;
    appointment = null;
    conflict = false;
    uncertainBooking = false;
  }

  void _clearDoctor() {
    doctor = null;
    doctors = [];
    availability = null;
    _clearSlot();
  }

  void selectService(BookingService value) {
    if (busy || uncertainBooking || !services.contains(value)) return;
    if (service?.id != value.id) {
      service = value;
      _clearDoctor();
    }
    changed();
  }

  Future<void> loadDoctors() => run((current) async {
    if (service == null) return;
    final result = await api.doctors(service!.id);
    if (!current()) return;
    doctors = result;
    step = 5;
    if (!doctors.any((d) => d.id == doctor?.id)) {
      doctor = null;
      availability = null;
      _clearSlot();
    }
  });
  void selectDoctor(BookingDoctor value) {
    if (busy || uncertainBooking || !doctors.contains(value)) return;
    if (doctor?.id != value.id) {
      doctor = value;
      availability = null;
      _clearSlot();
    }
    changed();
  }

  void openSlots() {
    if (!busy && doctor != null) {
      step = 6;
      changed();
    }
  }

  void changeDate(String value) {
    if (busy || uncertainBooking || date == value) return;
    date = value;
    availability = null;
    _clearSlot();
    changed();
  }

  Future<void> loadSlots() => run((current) async {
    if (doctor == null || service == null || uncertainBooking) return;
    availability = null;
    _clearSlot();
    step = 6;
    final result = await api.slots(doctor!.id, service!.id, date);
    ClinicClock.location(result.timeZoneId);
    if (!current()) return;
    availability = result;
    step = 6;
  });
  void selectSlot(BookingSlot value) {
    if (busy ||
        uncertainBooking ||
        !(availability?.slots.contains(value) ?? false)) {
      return;
    }
    if (slot?.startUtc != value.startUtc) {
      _clearSlot();
      slot = value;
      requestId = requestIdFactory();
    }
    changed();
  }

  void review() {
    if (!busy && slot != null) {
      step = 7;
      changed();
    }
  }

  Future<void> book() => run((current) async {
    if (slot == null ||
        doctor == null ||
        service == null ||
        requestId == null ||
        appointment != null) {
      return;
    }
    uncertainBooking = true;
    try {
      final result = await api.book(
        doctor!.id,
        service!.id,
        slot!.startUtc,
        requestId!,
      );
      if (!current()) return;
      appointment = result;
      uncertainBooking = false;
    } on ApiFailure catch (failure) {
      if (!current()) return;
      if (failure.status == 409) {
        availability = null;
        _clearSlot();
        conflict = true;
      } else if (failure.status != null &&
          failure.status! >= 400 &&
          failure.status! < 500) {
        uncertainBooking = false;
      }
      rethrow;
    }
  });
  Future<void> loadAppointments(int page) => run((current) async {
    final result = await api.appointments(page);
    if (!current()) return;
    history = result;
  });
  void changeNotificationConsent({bool? email, bool? sms}) {
    if (busy || notificationPreferences == null || session == null) return;
    if (email != null) emailConsent = email;
    if (sms != null) smsConsent = sms;
    notice = null;
    error = null;
    changed();
  }

  void _acceptPreferences(NotificationPreferences value) {
    notificationPreferences = value;
    emailConsent = value.emailEnabled;
    smsConsent = value.smsEnabled;
  }

  Future<void> loadNotificationPreferences() => run((current) async {
    if (session == null) return;
    notificationPreferences = null;
    emailConsent = false;
    smsConsent = false;
    changed();
    final result = await api.notificationPreferences();
    if (!current()) return;
    _acceptPreferences(result);
  });

  Future<void> saveNotificationPreferences() => run((current) async {
    if (session == null || notificationPreferences == null) return;
    try {
      final result = await api.saveNotificationPreferences(
        emailConsent,
        smsConsent,
      );
      if (!current()) return;
      _acceptPreferences(result);
      notice = 'Мэдэгдлийн тохиргоо хадгалагдлаа.';
    } on ApiFailure catch (failure) {
      if (!current()) return;
      if (failure.status == null || failure.status! >= 500) {
        throw const ApiFailure(
          'Хадгалагдсан эсэх тодорхойгүй байна. Тохиргоог шинэчилж шалгана уу.',
        );
      }
      rethrow;
    }
  });
  bool canCancel(Appointment value) =>
      !(reschedule?.uncertain == true && reschedule?.source.id == value.id) &&
      value.status == 'Confirmed' &&
      value.startUtc.isAfter(DateTime.now().toUtc());

  bool canReschedule(Appointment value) =>
      canCancel(value) &&
      !uncertainCancellations.contains(value.id) &&
      reschedule?.uncertain != true;

  bool beginReschedule(Appointment source) {
    if (busy || session == null || !canReschedule(source)) return false;
    try {
      final clinicToday = tz.TZDateTime.now(
        ClinicClock.location(source.clinicTimeZoneId),
      );
      reschedule = RescheduleDraft(source, calendarDate(clinicToday));
      error = null;
      notice = null;
      changed();
      return true;
    } on ApiFailure catch (failure) {
      error = failure.message;
      changed();
      return false;
    }
  }

  void selectRescheduleSlot(BookingSlot value) {
    final draft = reschedule;
    if (busy ||
        draft == null ||
        draft.uncertain ||
        draft.replacement != null ||
        !(draft.availability?.slots.contains(value) ?? false)) {
      return;
    }
    if (draft.selection?.startUtc != value.startUtc) {
      draft.selection = value;
      draft._requestId = requestIdFactory();
      error = null;
      notice = null;
      changed();
    }
  }

  Future<void> _loadRescheduleSlots(
    RescheduleDraft draft,
    bool Function() current,
  ) async {
    final options = await api.rescheduleSlots(draft.source.id, draft.date);
    ClinicClock.location(options.timeZoneId);
    if (!current()) return;
    draft.availability = options;
  }

  Future<void> loadRescheduleSlots({String? onDate}) => run((current) async {
    final draft = reschedule;
    if (session == null ||
        draft == null ||
        draft.uncertain ||
        draft.replacement != null) {
      return;
    }
    draft.date = onDate ?? draft.date;
    draft.availability = null;
    draft.clearSelection();
    changed();
    await _loadRescheduleSlots(draft, current);
  });

  Future<void> submitReschedule() => run((current) async {
    final draft = reschedule;
    final selection = draft?.selection;
    final requestId = draft?._requestId;
    if (session == null ||
        draft == null ||
        selection == null ||
        requestId == null ||
        draft.replacement != null) {
      return;
    }

    final wasUncertain = draft.uncertain;
    draft.uncertain = true;
    changed();
    Appointment replacement;
    try {
      replacement = await api.rescheduleAppointment(
        draft.source,
        selection.startUtc,
        requestId,
      );
      ClinicClock.location(replacement.clinicTimeZoneId);
    } on ApiFailure catch (failure) {
      if (!current()) return;
      // A 409 follows an authoritative, serialized source/ledger lookup.
      // Retriable database ambiguity is 503, and retains the original key.
      if (failure.status == 409) {
        draft.uncertain = false;
        draft.availability = null;
        draft.clearSelection();
        try {
          await _loadRescheduleSlots(draft, current);
          if (!current()) return;
          error =
              'Сонгосон цаг боломжгүй болсон. Шинэ боломжит цагаас сонгоно уу.';
        } on ApiFailure {
          if (!current()) return;
          error = 'Цаг шилжүүлэлт батлагдсангүй. Захиалгын төлөвөө шинэчилж шалгана уу.';
        }
        return;
      }
      if (!wasUncertain &&
          (failure.status == 400 ||
              failure.status == 403 ||
              failure.status == 404)) {
        draft.uncertain = false;
        rethrow;
      }
      // Network errors, malformed success, throttling and server failures do
      // not establish whether a previous POST committed. Retain the exact key.
      throw const ApiFailure(
        'Цаг шилжүүлэлтийн үр дүн тодорхойгүй байна. Ижил хүсэлтээр дахин шалгана уу.',
      );
    }
    if (!current()) return;
    draft.uncertain = false;
    draft.replacement = replacement;
    if (appointment?.id == draft.source.id) appointment = replacement;
    // The only prior list is stale. Do not display the old confirmed state
    // if refresh fails. The confirmed new appointment remains on screen.
    history = null;
    final stillConfirmed = replacement.status == 'Confirmed';
    notice = stillConfirmed
        ? 'Шинэ цаг баталгаажлаа.'
        : 'Цаг шилжсэн боловч тухайн шинэ захиалга одоо идэвхгүй байна.';
    changed();
    try {
      final refreshed = await api.appointments(1);
      if (current()) history = refreshed;
    } on ApiFailure {
      if (current()) {
        error = stillConfirmed
            ? 'Шинэ цаг баталгаажсан. Захиалгын жагсаалтыг дахин шинэчилнэ үү.'
            : 'Тухайн шинэ захиалга идэвхгүй. Захиалгын жагсаалтыг дахин шинэчилнэ үү.';
      }
    }
  });

  void _replaceAppointment(Appointment value) {
    if (appointment?.id == value.id) appointment = value;
    final items = history?.items;
    if (items != null) {
      final index = items.indexWhere((a) => a.id == value.id);
      if (index >= 0) items[index] = value;
    }
  }

  Future<void> _finishCancellation(
    Appointment value,
    bool Function() current,
  ) async {
    uncertainCancellations.remove(value.id);
    _replaceAppointment(value);
    notice = 'Таны захиалга амжилттай цуцлагдлаа.';
    try {
      final page = await api.appointments(
        history?.page ?? 1,
        pageSize: history?.pageSize ?? 20,
      );
      if (!current()) return;
      history = page;
      _replaceAppointment(value);
    } on ApiFailure {
      if (!current()) return;
      error = 'Захиалга цуцлагдсан. Жагсаалтыг шинэчилж чадсангүй. Дахин шинэчилнэ үү.';
    }
  }

  Future<void> _reconcileCancellation(int id, bool Function() current) async {
    try {
      final result = await api.appointment(id);
      if (!current()) return;
      _replaceAppointment(result);
      if (result.status == 'Cancelled') {
        await _finishCancellation(result, current);
      } else {
        // An earlier timed-out POST may still be in flight. A Confirmed GET
        // does not establish failure, and never triggers an automatic POST.
        notice = 'Цуцлалт баталгаажаагүй байна. Төлөвийг дахин шалгах эсвэл цуцлахыг дахин оролдоно уу.';
      }
    } on ApiFailure {
      if (!current()) return;
      error = 'Цуцлалтын үр дүн тодорхойгүй байна. Холболтоо шалгаад төлөвийг дахин шалгана уу.';
    }
  }

  Future<void> reconcileCancellation(int id) => run((current) async {
    if (!uncertainCancellations.contains(id)) return;
    await _reconcileCancellation(id, current);
  });

  Future<void> cancelAppointment(Appointment value) => run((current) async {
    if (!canCancel(value)) {
      error = 'Энэ захиалгыг цуцлах боломжгүй.';
      return;
    }
    try {
      final result = await api.cancelAppointment(value.id);
      if (!current()) return;
      await _finishCancellation(result, current);
    } on ApiFailure catch (failure) {
      if (!current()) return;
      if (failure.status != null &&
          failure.status! >= 400 &&
          failure.status! < 500) {
        // A previous uncertain request can still finish independently.
        error = failure.status == 409
            ? 'Захиалгыг цуцлах боломжгүй болсон. Төлөвийг шинэчилнэ үү.'
            : failure.message;
        return;
      }
      uncertainCancellations.add(value.id);
      await _reconcileCancellation(value.id, current);
    }
  });
  void newBooking() {
    if (busy || uncertainBooking) return;
    _clearSlot();
    availability = null;
    history = null;
    step = 4;
    changed();
  }

  @override
  void dispose() {
    _disposed = true;
    _epoch++;
    api.onUnauthorized = null;
    api.close();
    super.dispose();
  }
}
