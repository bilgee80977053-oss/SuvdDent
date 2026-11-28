import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'dental_api.dart';
import 'encounter_treatment_api.dart';
import 'live_flow_controller.dart' show newRequestId;

/// Opt-in panel for a caller-selected, already-authorized encounter. The caller
/// supplies its booked service from the server and owns the authenticated API.
/// This panel neither discovers encounters nor participates in app routing.
/// The host must remove the panel on logout/account change.
class PerformedProcedurePanel extends StatefulWidget {
  const PerformedProcedurePanel({
    super.key,
    required this.api,
    required this.appointmentId,
    required this.bookedServiceId,
    required this.bookedServiceLabel,
    this.requestIdFactory = newRequestId,
    this.onPendingIntentChanged,
  });

  final EncounterTreatmentApi api;
  final int appointmentId;
  final int bookedServiceId;
  final String bookedServiceLabel;
  final String Function() requestIdFactory;

  /// A host can keep the panel mounted rather than discard an unresolved
  /// procedure request when navigating among encounters.
  final ValueChanged<bool>? onPendingIntentChanged;

  @override
  State<PerformedProcedurePanel> createState() =>
      _PerformedProcedurePanelState();
}

class _SignIntent {
  const _SignIntent(this.id, this.scope, this.tooth);

  final String id;
  final PerformedProcedureScope scope;
  final String? tooth;

  bool matches(PerformedProcedure item, int serviceId) =>
      item.requestId == id.toLowerCase() &&
      item.serviceId == serviceId &&
      item.scope == scope &&
      item.toothCode == tooth;
}

class _PerformedProcedurePanelState extends State<PerformedProcedurePanel> {
  static final _validTooth = RegExp(r'^(?:[1-4][1-8]|[5-8][1-5])$');
  final _tooth = TextEditingController();
  PerformedProcedureScope? _scope;
  PerformedProcedurePage? _history;
  _SignIntent? _pending;
  bool _loading = false, _confirming = false, _signing = false;
  bool _denied = false, _completed = false, _conflict = false;
  String? _readError, _signMessage;
  int _generation = 0;

  bool get _validEncounter =>
      widget.appointmentId > 0 &&
      widget.appointmentId <= 2147483647 &&
      widget.bookedServiceId > 0 &&
      widget.bookedServiceId <= 2147483647 &&
      widget.bookedServiceLabel.trim().length >= 2 &&
      widget.bookedServiceLabel.length <= 160;

  bool get _canStart =>
      _validEncounter &&
      widget.api.authenticated &&
      _history != null &&
      !_denied &&
      !_completed &&
      !_loading &&
      !_confirming &&
      !_signing &&
      _pending == null;

  bool _current(int generation) => mounted && _generation == generation;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  @override
  void didUpdateWidget(PerformedProcedurePanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (identical(oldWidget.api, widget.api) &&
        oldWidget.appointmentId == widget.appointmentId &&
        oldWidget.bookedServiceId == widget.bookedServiceId) {
      return;
    }
    _generation++;
    _tooth.clear();
    _scope = null;
    _history = null;
    _pending = null;
    _loading = _confirming = _signing = _denied = _completed = _conflict =
        false;
    _readError = _signMessage = null;
    _refresh();
  }

  @override
  void dispose() {
    _generation++;
    _tooth.dispose();
    // The caller may share and continues to own the API/client.
    super.dispose();
  }

  bool _isDenied(Object error) =>
      error is ApiFailure && const {401, 403, 404}.contains(error.status);

  Future<void> _refresh() async {
    if (!_validEncounter || _loading || _signing || _confirming) return;
    final generation = _generation;
    final api = widget.api;
    setState(() {
      _loading = true;
      _readError = null;
    });
    try {
      final history = await api.listPerformedProcedures(widget.appointmentId);
      if (!_current(generation)) return;
      final hadPending = _pending != null;
      setState(() {
        _history = history;
        _denied = false;
        final pending = _pending;
        if (pending != null &&
            !_conflict &&
            history.items.any(
              (item) => pending.matches(item, widget.bookedServiceId),
            )) {
          _pending = null;
          _completed = true;
          _signMessage = 'Ажилбарын бүртгэл түүхээр баталгаажлаа.';
        }
      });
      if (hadPending && _pending == null) {
        widget.onPendingIntentChanged?.call(false);
      }
    } catch (error) {
      if (!_current(generation)) return;
      setState(() {
        _history = null;
        _denied = _isDenied(error);
        _readError = _denied
            ? 'Ажилбарын мэдээлэлд хандах эрхгүй эсвэл үзлэг олдсонгүй.'
            : 'Ажилбарын түүхийг шинэчилж чадсангүй.';
      });
    } finally {
      if (_current(generation)) setState(() => _loading = false);
    }
  }

  Future<void> _confirm() async {
    if (!_canStart) return;
    final scope = _scope;
    final tooth = scope == PerformedProcedureScope.singleTooth
        ? _tooth.text
        : null;
    if (scope == null || (tooth != null && !_validTooth.hasMatch(tooth))) {
      setState(
        () =>
            _signMessage = 'Хүрээг сонгоно уу. Шүд бол FDI дугаар оруулна уу.',
      );
      return;
    }
    final generation = _generation;
    final label = widget.bookedServiceLabel;
    setState(() {
      _confirming = true;
      _signMessage = null;
    });
    try {
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Гүйцэтгэсэн ажилбарыг баталгаажуулах'),
          content: Text(
            '$label\n${tooth == null ? 'Үзлэгийн хэмжээнд' : 'Шүд: $tooth'}',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Буцах'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Баталгаажуулж бүртгэх'),
            ),
          ],
        ),
      );
      if (!_current(generation)) return;
      setState(() => _confirming = false);
      if (confirmed != true ||
          !_canStart ||
          label != widget.bookedServiceLabel) {
        return;
      }
      final intent = _SignIntent(widget.requestIdFactory(), scope, tooth);
      await _sign(intent, isRetry: false);
    } catch (_) {
      if (_current(generation)) {
        setState(() => _signMessage = 'Ажилбарыг баталгаажуулж чадсангүй.');
      }
    } finally {
      if (_current(generation) && _confirming) {
        setState(() => _confirming = false);
      }
    }
  }

  Future<void> _sign(_SignIntent intent, {required bool isRetry}) async {
    if (_signing ||
        _loading ||
        _completed ||
        _denied ||
        !widget.api.authenticated) {
      return;
    }
    final generation = _generation;
    final api = widget.api;
    setState(() {
      _pending = intent;
      _signing = true;
      _signMessage = null;
    });
    if (!isRetry) widget.onPendingIntentChanged?.call(true);
    var refresh = false;
    try {
      final result = await api.signPerformedProcedure(
        widget.appointmentId,
        requestId: intent.id,
        serviceId: widget.bookedServiceId,
        scope: intent.scope,
        toothCode: intent.tooth,
      );
      if (!_current(generation)) return;
      setState(() {
        _completed = true;
        _pending = null;
        _signMessage = result is PerformedProcedureRecorded
            ? 'Ажилбар бүртгэгдлээ.'
            : 'Өмнөх хүсэлт бүртгэгдсэн байна. Түүхийг шинэчилж байна.';
      });
      widget.onPendingIntentChanged?.call(false);
      refresh = true;
    } catch (error) {
      if (!_current(generation)) return;
      setState(() {
        final status = error is ApiFailure ? error.status : null;
        _denied = _isDenied(error);
        _conflict = status == 409;
        if (_denied) _history = null;
        // A rejection of a retry is not proof the original never committed.
        if (!isRetry && const {400, 401, 403, 404}.contains(status)) {
          _pending = null;
        }
        _signMessage = _denied
            ? 'Ажилбар бүртгэх эрхгүй эсвэл үзлэг олдсонгүй.'
            : _conflict
            ? 'Хүсэлт зөрчилтэй байна. Дахин бүртгэл бүү үүсгэ.'
            : status == 400 && !isRetry
            ? 'Ажилбарын мэдээлэл буруу байна.'
            : 'Үр дүн тодорхойгүй байна. Түүхийг шалгах эсвэл ижил хүсэлтийг давтана уу.';
      });
      if (_pending == null) widget.onPendingIntentChanged?.call(false);
    } finally {
      if (_current(generation)) setState(() => _signing = false);
    }
    if (refresh && _current(generation)) await _refresh();
  }

  @override
  Widget build(BuildContext context) {
    if (!_validEncounter) {
      return const Text('Үзлэгийн мэдээлэл бүрэн биш байна.');
    }
    if (!widget.api.authenticated) {
      return const Text('Эмчийн эрхээр нэвтэрнэ үү.');
    }
    final pending = _pending;
    final history = _history;
    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          const Text('Гүйцэтгэсэн ажилбар'),
          Text(widget.bookedServiceLabel),
          if (_loading || _signing) const LinearProgressIndicator(),
          if (_readError case final message?) Text(message),
          if (_signMessage case final message?) Text(message),
          if (history != null) ...[
            if (history.items.isEmpty) const Text('Бүртгэл хараахан байхгүй.'),
            for (final item in history.items)
              Text(
                '${item.serviceLabel} · ${item.toothCode ?? 'Үзлэг'} · ${item.signedUtc.toIso8601String()}',
              ),
            if (history.hasMore)
              const Text('Өмнөх ажилбарын нэмэлт бүртгэл байна.'),
          ],
          TextButton(
            onPressed: _loading || _signing || _confirming ? null : _refresh,
            child: const Text('Түүх шинэчлэх'),
          ),
          if (!_denied && !_completed && pending == null) ...[
            Wrap(
              spacing: 8,
              children: [
                for (final scope in PerformedProcedureScope.values)
                  ChoiceChip(
                    label: Text(
                      scope == PerformedProcedureScope.visit
                          ? 'Үзлэг'
                          : 'Нэг шүд',
                    ),
                    selected: _scope == scope,
                    onSelected: _canStart
                        ? (_) => setState(() {
                            _scope = scope;
                            _signMessage = null;
                            _tooth.clear();
                          })
                        : null,
                  ),
              ],
            ),
            if (_scope == PerformedProcedureScope.singleTooth)
              TextField(
                controller: _tooth,
                enabled: _canStart,
                decoration: const InputDecoration(
                  labelText: 'FDI шүдний дугаар',
                ),
                maxLength: 2,
                keyboardType: TextInputType.number,
                inputFormatters: [FilteringTextInputFormatter.digitsOnly],
              ),
            FilledButton(
              onPressed: _canStart ? _confirm : null,
              child: const Text('Ажилбар бүртгэх'),
            ),
          ],
          if (pending != null && !_denied && !_conflict)
            FilledButton(
              onPressed: _loading || _signing || !widget.api.authenticated
                  ? null
                  : () => _sign(pending, isRetry: true),
              child: const Text('Ижил хүсэлтийг давтах'),
            ),
        ],
      ),
    );
  }
}
