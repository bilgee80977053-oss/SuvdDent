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

  Future<void> verify(String code) => run((current) async {
    if (registration == null) return;
    final result = await api.verify(registration!.userId, code);
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
