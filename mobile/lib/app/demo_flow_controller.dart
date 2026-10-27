import 'package:flutter/foundation.dart';

import '../data/demo_catalog.dart';

enum DemoStep {
  clinic,
  authentication,
  verification,
  profile,
  service,
  doctor,
  slot,
  confirmation,
}

enum DemoAuthMode { login, registration }

enum DemoProfileMode { create, retrieve }

// In-memory walkthrough state, not authentication, authorization or booking logic.
// No HTTP, tokens, persistence, SMTP or user-entered credentials exist in STEP 1.
class DemoFlowController extends ChangeNotifier {
  DemoStep _step = DemoStep.clinic;
  DemoClinic? _clinic;
  DemoService? _service;
  DemoDoctor? _doctor;
  DemoSlot? _slot;
  bool _signedIn = false;
  bool _emailVerified = false;
  bool _profileReady = false;
  bool _complete = false;
  DemoAuthMode _authMode = DemoAuthMode.login;
  DemoProfileMode _profileMode = DemoProfileMode.create;

  DemoStep get step => _step;
  DemoClinic? get clinic => _clinic;
  DemoService? get service => _service;
  DemoDoctor? get doctor => _doctor;
  DemoSlot? get slot => _slot;
  bool get demoEmailVerified => _emailVerified;
  bool get demoProfileReady => _profileReady;
  bool get complete => _complete;
  DemoAuthMode get authMode => _authMode;
  DemoProfileMode get profileMode => _profileMode;
  List<DemoService> get services =>
      DemoCatalog.services.where((s) => s.clinicId == _clinic?.id).toList();
  List<DemoDoctor> get doctors => DemoCatalog.doctors
      .where(
        (d) => d.clinicId == _clinic?.id && d.serviceIds.contains(_service?.id),
      )
      .toList();
  List<DemoSlot> get slots =>
      _clinic == null || _doctor == null ? [] : DemoCatalog.slots(_clinic!);

  bool get canContinue => switch (_step) {
    DemoStep.clinic => _clinic != null,
    DemoStep.authentication => _clinic != null,
    DemoStep.verification => _signedIn,
    DemoStep.profile => _signedIn && _emailVerified,
    DemoStep.service => _profileReady && _service != null,
    DemoStep.doctor => _service != null && _doctor != null,
    DemoStep.slot => _doctor != null && _slot != null,
    DemoStep.confirmation =>
      _signedIn &&
          _emailVerified &&
          _profileReady &&
          _clinic != null &&
          _service != null &&
          _doctor != null &&
          _slot != null &&
          !_complete,
  };

  void selectClinic(int id) {
    if (_step != DemoStep.clinic) {
      throw StateError('Clinic selection requires its screen.');
    }
    final selected = DemoCatalog.clinics.singleWhere((c) => c.id == id);
    if (selected.id != _clinic?.id) {
      _clinic = selected;
      _service = null;
      _doctor = null;
      _slot = null;
      _signedIn = false;
      _emailVerified = false;
      _profileReady = false;
      _complete = false;
    }
    notifyListeners();
  }

  void selectService(int id) {
    if (_step != DemoStep.service) {
      throw StateError('Service selection requires its screen.');
    }
    final selected = services.singleWhere((s) => s.id == id);
    if (selected.id != _service?.id) {
      _service = selected;
      _doctor = null;
      _slot = null;
    }
    notifyListeners();
  }

  void selectDoctor(int id) {
    if (_step != DemoStep.doctor) {
      throw StateError('Doctor selection requires its screen.');
    }
    final selected = doctors.singleWhere((d) => d.id == id);
    if (selected.id != _doctor?.id) {
      _doctor = selected;
      _slot = null;
    }
    notifyListeners();
  }

  void selectSlot(int id) {
    if (_step != DemoStep.slot) {
      throw StateError('Slot selection requires its screen.');
    }
    _slot = slots.singleWhere((s) => s.id == id);
    notifyListeners();
  }

  void setAuthMode(DemoAuthMode mode) {
    _authMode = mode;
    notifyListeners();
  }

  void setProfileMode(DemoProfileMode mode) {
    _profileMode = mode;
    notifyListeners();
  }

  bool continueFlow() {
    if (!canContinue) return false;
    if (_step == DemoStep.authentication) _signedIn = true;
    if (_step == DemoStep.verification) _emailVerified = true;
    if (_step == DemoStep.profile) _profileReady = true;
    if (_step == DemoStep.confirmation) {
      _complete = true;
    } else {
      _step = DemoStep.values[_step.index + 1];
    }
    notifyListeners();
    return true;
  }

  void back() {
    if (_step == DemoStep.clinic) return;
    _complete = false;
    _step = DemoStep.values[_step.index - 1];
    notifyListeners();
  }

  void restart() {
    _step = DemoStep.clinic;
    _clinic = null;
    _service = null;
    _doctor = null;
    _slot = null;
    _signedIn = false;
    _emailVerified = false;
    _profileReady = false;
    _complete = false;
    _authMode = DemoAuthMode.login;
    _profileMode = DemoProfileMode.create;
    notifyListeners();
  }
}
