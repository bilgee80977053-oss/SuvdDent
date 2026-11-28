import 'package:flutter/material.dart';

import 'dental_api.dart';
import 'encounter_treatment_api.dart';

/// Standalone synthetic review panel. Not registered in app navigation.
/// The host owns an already-scoped API and supplies a fresh addendum UUID
/// when a clinician intentionally begins a separate append operation.
class ClinicalNotePanel extends StatefulWidget {
  final EncounterTreatmentApi api;
  final int appointmentId;
  final String Function() createAddendumRequestId;

  const ClinicalNotePanel({
    super.key,
    required this.api,
    required this.appointmentId,
    required this.createAddendumRequestId,
  });

  @override
  State<ClinicalNotePanel> createState() => _ClinicalNotePanelState();
}

class _ClinicalNotePanelState extends State<ClinicalNotePanel> {
  final _originalDraft = TextEditingController();
  final _addendumDraft = TextEditingController();
  EncounterTreatmentNote? _original;
  EncounterTreatmentAddendum? _visibleAddendum;
  List<EncounterAddendumReference> _references = const [];
  String? _pendingOriginal;
  String? _pendingAddendumId, _pendingAddendumText;
  String? _message;
  bool _originalConfirmed = false;
  bool _busy = false;
  bool _confirming = false;
  int _generation = 0;

  @override
  void initState() {
    super.initState();
    widget.api.sessionChanges.addListener(_sessionChanged);
  }

  @override
  void didUpdateWidget(covariant ClinicalNotePanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.api != widget.api) {
      oldWidget.api.sessionChanges.removeListener(_sessionChanged);
      widget.api.sessionChanges.addListener(_sessionChanged);
    }
    if (oldWidget.api != widget.api ||
        oldWidget.appointmentId != widget.appointmentId) {
      _clear();
    }
  }

  void _clear() {
    _generation++;
    _originalDraft.clear();
    _addendumDraft.clear();
    _original = null;
    _visibleAddendum = null;
    _references = const [];
    _pendingOriginal = null;
    _pendingAddendumId = null;
    _pendingAddendumText = null;
    _message = null;
    _originalConfirmed = false;
    _busy = false;
    _confirming = false;
  }

  void _sessionChanged() {
    if (mounted) setState(_clear);
  }

  @override
  void dispose() {
    widget.api.sessionChanges.removeListener(_sessionChanged);
    _clear();
    _originalDraft.dispose();
    _addendumDraft.dispose();
    super.dispose();
  }

  bool _current(int generation) =>
      mounted && _generation == generation && widget.api.authenticated;
  bool get _canStart =>
      mounted && !_busy && !_confirming && widget.api.authenticated;

  Future<void> _run(Future<void> Function(int generation) action) async {
    if (!_canStart) return;
    final generation = _generation;
    setState(() {
      _busy = true;
      _message = null;
    });
    try {
      await action(generation);
    } finally {
      if (_current(generation)) setState(() => _busy = false);
    }
  }

  Future<bool> _confirm() async =>
      await showDialog<bool>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: const Text('Анхны тэмдэглэлийг баталгаажуулах уу?'),
          content: const Text('Баталгаажсаны дараа эх хувийг солих боломжгүй.'),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: const Text('Буцах'),
            ),
            FilledButton(
              key: const ValueKey('clinical-confirm-original'),
              onPressed: () => Navigator.of(dialogContext).pop(true),
              child: const Text('Баталгаажуулах'),
            ),
          ],
        ),
      ) ??
      false;

  Future<void> _readOriginal(int generation) async {
    try {
      final record = await widget.api.readTreatment(widget.appointmentId);
      if (!_current(generation)) return;
      setState(() {
        final expected = _pendingOriginal;
        _original = record;
        _originalConfirmed = true;
        _pendingOriginal = null;
        _originalDraft.clear();
        if (expected != null) {
          _message = record.note == expected
              ? 'Ижил тэмдэглэл бүртгэлтэй нь уншилтаар батлагдлаа.'
              : 'Өөр анхны тэмдэглэл бүртгэлтэй байна. Илгээсэн утга хадгалагдсан гэж үзэхгүй.';
        }
      });
    } on ApiFailure catch (error) {
      if (!_current(generation)) return;
      setState(() {
        _original = null;
        if (_pendingOriginal != null) {
          _message = 'Анхны тэмдэглэлийн үр дүнг тогтоогоогүй. Дахин илгээхгүй, уншиж шалгана уу.';
        } else {
          _message = error.status == 404
              ? 'Анхны тэмдэглэл олдсонгүй эсвэл харах эрхгүй.'
              : 'Анхны тэмдэглэлийг уншиж чадсангүй.';
        }
      });
    }
  }

  Future<void> _beginOriginal() async {
    if (!_canStart || _pendingOriginal != null || _originalConfirmed) return;
    final generation = _generation;
    final submitted = _originalDraft.text;
    final expected = canonicalClinicalNote(submitted);
    if (expected.isEmpty) {
      setState(() => _message = 'Тэмдэглэл оруулна уу.');
      return;
    }
    setState(() => _confirming = true);
    final confirmed = await _confirm();
    if (!_current(generation)) return;
    setState(() => _confirming = false);
    if (confirmed) {
      await _run((current) => _signOriginal(current, submitted, expected));
    }
  }

  Future<void> _signOriginal(
    int generation,
    String submitted,
    String expected,
  ) async {
    try {
      await widget.api.signTreatment(widget.appointmentId, note: submitted);
      if (!_current(generation)) return;
      setState(() {
        _originalConfirmed = true;
        _originalDraft.clear();
        _message = 'Анхны тэмдэглэл баталгаажлаа.';
      });
    } on EncounterWriteOutcomeUncertain {
      if (!_current(generation)) return;
      _pendingOriginal = expected;
      await _readOriginal(generation);
    } on ApiFailure catch (error) {
      if (!_current(generation)) return;
      if (error.status == 409) {
        // A conflict means the original is immutable, not that our POST won.
        _pendingOriginal = expected;
        await _readOriginal(generation);
      } else {
        setState(
          () => _message = 'Анхны тэмдэглэлийг баталгаажуулж чадсангүй.',
        );
      }
    }
  }

  Future<void> _readList(int generation) async {
    try {
      final page = await widget.api.listAddenda(widget.appointmentId);
      if (_current(generation)) setState(() => _references = page.items);
    } on ApiFailure {
      if (_current(generation)) {
        setState(() {
          _references = const [];
          _visibleAddendum = null;
          _message = 'Нэмэлтийн жагсаалтыг уншиж чадсангүй.';
        });
      }
    }
  }

  Future<void> _readAddendum(int generation, String key) async {
    try {
      final record = await widget.api.readAddendum(widget.appointmentId, key);
      if (!_current(generation)) return;
      setState(() {
        _visibleAddendum = record;
        if (_pendingAddendumId?.toLowerCase() == key.toLowerCase()) {
          final matches = record.note == _pendingAddendumText;
          _pendingAddendumId = null;
          _pendingAddendumText = null;
          if (matches) _addendumDraft.clear();
          _message = matches
              ? 'Ижил нэмэлт бүртгэлтэй нь уншилтаар батлагдлаа.'
              : 'Энэ дугаарт өөр нэмэлт байна. Илгээсэн утга хадгалагдсан гэж үзэхгүй.';
        }
      });
    } on ApiFailure {
      if (!_current(generation)) return;
      setState(() {
        _visibleAddendum = null;
        _message = 'Нэмэлтийг уншиж баталгаажуулж чадсангүй.';
      });
    }
  }

  Future<void> _signAddendum(int generation) async {
    final submitted = _addendumDraft.text;
    final expected = canonicalClinicalNote(submitted);
    if (expected.isEmpty) {
      if (_current(generation)) {
        setState(() => _message = 'Нэмэлтийн тэмдэглэл оруулна уу.');
      }
      return;
    }
    final pending = _pendingAddendumId;
    if (pending != null && expected != _pendingAddendumText) {
      if (_current(generation)) {
        setState(
          () => _message =
              'Шийдэгдээгүй нэмэлтийг эхлээд дугаараар нь уншиж шалгана уу.',
        );
      }
      return;
    }

    late final String key;
    try {
      key = pending ?? widget.createAddendumRequestId();
    } catch (_) {
      if (_current(generation)) {
        setState(() => _message = 'Нэмэлтийн дугаар үүсгэсэнгүй.');
      }
      return;
    }
    if (!_current(generation)) return;
    try {
      final result = await widget.api.signAddendum(
        widget.appointmentId,
        requestId: key,
        note: submitted,
      );
      if (!_current(generation)) return;
      setState(() {
        _pendingAddendumId = null;
        _pendingAddendumText = null;
        _addendumDraft.clear();
        _message = result == EncounterAddendumSignResult.recorded
            ? 'Нэмэлт баталгаажлаа.'
            : 'Ижил дугаартай нэмэлт өмнө нь баталгаажсан байна.';
      });
    } on EncounterWriteOutcomeUncertain {
      if (!_current(generation)) return;
      setState(() {
        _pendingAddendumId = key;
        _pendingAddendumText = expected;
      });
      await _readAddendum(generation, key);
    } on ApiFailure catch (error) {
      if (!_current(generation)) return;
      if (error.status == 409) {
        setState(() {
          _pendingAddendumId = key;
          _pendingAddendumText = expected;
        });
        await _readAddendum(generation, key);
      } else {
        setState(() => _message = 'Нэмэлтийг баталгаажуулж чадсангүй.');
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.api.authenticated) {
      return const Text('Туршилтын эмчийн эрх шаардлагатай.');
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        const Text('Эмнэлзүйн тэмдэглэл · Зөвхөн туршилтын орчин'),
        if (_busy) const LinearProgressIndicator(),
        if (_message != null)
          Semantics(liveRegion: true, child: Text(_message!)),
        OutlinedButton(
          key: const ValueKey('clinical-read-original'),
          onPressed: _canStart ? () => _run(_readOriginal) : null,
          child: const Text('Анхны тэмдэглэл унших'),
        ),
        if (_original != null)
          Text(
            _original!.note,
            key: const ValueKey('clinical-original-content'),
          ),
        if (!_originalConfirmed) ...[
          TextField(
            key: const ValueKey('clinical-original-draft'),
            controller: _originalDraft,
            enabled: _canStart && _pendingOriginal == null,
            maxLength: 2000,
            maxLines: 3,
            enableSuggestions: false,
            autocorrect: false,
            decoration: const InputDecoration(labelText: 'Анхны тэмдэглэл'),
          ),
          FilledButton(
            key: const ValueKey('clinical-sign-original'),
            onPressed: _canStart && _pendingOriginal == null
                ? _beginOriginal
                : null,
            child: const Text('Анхны тэмдэглэл баталгаажуулах'),
          ),
        ],
        if (_originalConfirmed) ...[
          TextField(
            key: const ValueKey('clinical-addendum-draft'),
            controller: _addendumDraft,
            enabled: _canStart && _pendingAddendumId == null,
            maxLength: 2000,
            maxLines: 3,
            enableSuggestions: false,
            autocorrect: false,
            decoration: const InputDecoration(labelText: 'Нэмэлт тэмдэглэл'),
          ),
          FilledButton(
            key: const ValueKey('clinical-sign-addendum'),
            onPressed: _canStart ? () => _run(_signAddendum) : null,
            child: Text(
              _pendingAddendumId == null
                  ? 'Нэмэлт баталгаажуулах'
                  : 'Ижил дугаараар дахин оролдох',
            ),
          ),
          if (_pendingAddendumId case final pending?)
            OutlinedButton(
              key: const ValueKey('clinical-reconcile-addendum'),
              onPressed: _canStart
                  ? () => _run((g) => _readAddendum(g, pending))
                  : null,
              child: const Text('Нэмэлтийг уншиж шалгах'),
            ),
          OutlinedButton(
            key: const ValueKey('clinical-list-addenda'),
            onPressed: _canStart ? () => _run(_readList) : null,
            child: const Text('Нэмэлтийн жагсаалт унших'),
          ),
          for (final row in _references)
            TextButton(
              onPressed: _canStart
                  ? () => _run((g) => _readAddendum(g, row.requestId))
                  : null,
              child: Text(row.signedUtc.toIso8601String()),
            ),
          if (_visibleAddendum != null)
            Text(
              _visibleAddendum!.note,
              key: const ValueKey('clinical-addendum-content'),
            ),
        ],
      ],
    );
  }
}
