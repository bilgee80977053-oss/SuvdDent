import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

import 'clinical_note_panel.dart';
import 'dental_api.dart';
import 'encounter_treatment_api.dart';
import 'live_flow_controller.dart' show newRequestId;
import 'models.dart';
import 'performed_procedure_panel.dart';

/// Explicit development-review entry. This is not part of default app routing.
/// The host supplies an approved synthetic HTTPS clinic, UTC review period and
/// an owned-by-this-entry transport per login. Nothing here enables the server
/// route or a release build. No credentials, IDs or clinical data are persisted.
class ClinicianReviewEntry extends StatefulWidget {
  const ClinicianReviewEntry({
    super.key,
    required this.settings,
    required this.clientFactory,
    required this.fromUtc,
    required this.untilUtc,
    this.enabled = false,
  });

  final ApiSettings settings;
  final http.Client Function() clientFactory;
  final DateTime fromUtc, untilUtc;
  final bool enabled;

  @override
  State<ClinicianReviewEntry> createState() => _ClinicianReviewEntryState();
}

class _ClinicianReviewEntryState extends State<ClinicianReviewEntry> {
  static const _size = 10;
  static const _maxPage = 100;
  final _identifier = TextEditingController();
  final _password = TextEditingController();

  EncounterTreatmentApi? _api;
  AssignedEncounterPage? _page;
  AssignedEncounter? _selected;
  bool _inFlight = false, _procedurePending = false, _notePending = false;
  int _reviewTab = 0;
  bool get _unresolved => _procedurePending || _notePending;
  int _epoch = 0;
  int _requestedPage = 1;
  String? _message;

  bool get _reviewEnabled => widget.enabled && !kReleaseMode;

  bool _utcMidnight(DateTime value) =>
      value.isUtc &&
      value.year > 0 &&
      value.year <= 9999 &&
      value == DateTime.utc(value.year, value.month, value.day);

  bool get _configured {
    final settings = widget.settings;
    final span = widget.untilUtc.difference(widget.fromUtc).inDays;
    return _reviewEnabled &&
        settings.error == null &&
        settings.clinicId > 0 &&
        settings.clinicId <= 2147483647 &&
        Uri.tryParse(settings.baseUrl)?.scheme == 'https' &&
        _utcMidnight(widget.fromUtc) &&
        _utcMidnight(widget.untilUtc) &&
        span >= 1 &&
        span <= 31;
  }

  bool _current(int epoch, EncounterTreatmentApi api) =>
      mounted && _epoch == epoch && identical(_api, api);

  @override
  void didUpdateWidget(ClinicianReviewEntry oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.enabled != oldWidget.enabled ||
        widget.settings.clinicId != oldWidget.settings.clinicId ||
        widget.settings.baseUrl != oldWidget.settings.baseUrl ||
        widget.fromUtc != oldWidget.fromUtc ||
        widget.untilUtc != oldWidget.untilUtc ||
        widget.fromUtc.isUtc != oldWidget.fromUtc.isUtc ||
        widget.untilUtc.isUtc != oldWidget.untilUtc.isUtc) {
      _clearSession();
    }
  }

  @override
  void dispose() {
    _epoch++;
    _api?.close();
    _identifier.dispose();
    _password.dispose();
    super.dispose();
  }

  void _clearSession() {
    _epoch++;
    _api?.close();
    _api = null;
    _identifier.clear();
    _password.clear();
    _page = null;
    _selected = null;
    _requestedPage = 1;
    _inFlight = _procedurePending = _notePending = false;
    _reviewTab = 0;
    _message = null;
  }

  Future<void> _login() async {
    if (_inFlight || !_configured || _api?.authenticated == true) return;
    final identifier = _identifier.text.trim();
    final password = _password.text;
    if (identifier.isEmpty || password.isEmpty) {
      setState(() => _message = 'Нэвтрэх нэр, нууц үгээ оруулна уу.');
      return;
    }
    _clearSession();
    // Password is never retained by the form after submitting.
    final epoch = _epoch;
    final EncounterTreatmentApi api;
    try {
      api = EncounterTreatmentApi(
        widget.settings,
        client: widget.clientFactory(),
      );
    } catch (_) {
      setState(() => _message = 'Туршилтын холболтыг эхлүүлж чадсангүй.');
      return;
    }
    setState(() {
      _api = api;
      _inFlight = true;
    });
    try {
      await api.login(identifier, password);
      if (!_current(epoch, api)) return;
    } catch (_) {
      if (!_current(epoch, api)) return;
      setState(() {
        _clearSession();
        _message = 'Тухайн эмнэлгийн эмчийн эрхийг баталгаажуулж чадсангүй.';
      });
      return;
    }
    setState(() => _inFlight = false);
    await _load(1);
  }

  Future<void> _load(int page) async {
    final api = _api;
    if (api == null ||
        !api.authenticated ||
        _inFlight ||
        page < 1 ||
        page > _maxPage) {
      return;
    }
    final epoch = _epoch;
    setState(() {
      _inFlight = true;
      _requestedPage = page;
      _page = null; // Stale records cannot remain selectable during refresh.
      _message = null;
    });
    try {
      final result = await api.listAssignedEncounters(
        fromUtc: widget.fromUtc,
        untilUtc: widget.untilUtc,
        page: page,
        pageSize: _size,
      );
      if (_current(epoch, api)) setState(() => _page = result);
    } catch (error) {
      if (!_current(epoch, api)) return;
      setState(() {
        final status = error is ApiFailure ? error.status : null;
        if (status == 401) _clearSession();
        _message = const {401, 403, 404}.contains(status)
            ? 'Хуваарилсан үзлэгт хандах эрхгүй эсвэл туршилтын горим идэвхгүй байна.'
            : 'Хуваарилсан үзлэгийг ачаалж чадсангүй.';
      });
    } finally {
      if (_current(epoch, api)) setState(() => _inFlight = false);
    }
  }

  String _date(DateTime utc) => utc.toIso8601String().split('T').first;

  @override
  Widget build(BuildContext context) {
    if (!_reviewEnabled) {
      return const Scaffold(body: Text('Эмчийн хяналтын горим идэвхгүй.'));
    }
    if (!_configured) {
      return const Scaffold(
        body: Text(
          'HTTPS туршилтын эмнэлэг болон 1–31 хоногийн UTC хүрээ тохируулна уу.',
        ),
      );
    }
    final api = _api;
    final selected = _selected;
    final authenticated = api?.authenticated == true;
    return PopScope(
      canPop: !_unresolved,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Эмчийн хяналт · зөвхөн туршилт'),
          actions: [
            if (api != null)
              TextButton(
                onPressed: () => setState(_clearSession),
                child: const Text('Гарах'),
              ),
          ],
        ),
        body: Padding(
          padding: const EdgeInsets.all(16),
          child: authenticated && selected != null
              ? Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    TextButton(
                      onPressed: _unresolved
                          ? null
                          : () => setState(() {
                              _selected = null;
                              _reviewTab = 0;
                            }),
                      child: const Text('Хуваарилсан үзлэг рүү буцах'),
                    ),
                    if (_unresolved)
                      const Text(
                        'Үзлэг солихын өмнө тэмдэглэл эсвэл ажилбарын хүсэлтийг тулган баталгаажуулна уу.',
                      ),
                    SegmentedButton<int>(
                      segments: const [
                        ButtonSegment(value: 0, label: Text('Ажилбар')),
                        ButtonSegment(value: 1, label: Text('Тэмдэглэл')),
                      ],
                      selected: {_reviewTab},
                      onSelectionChanged: (values) {
                        FocusManager.instance.primaryFocus?.unfocus();
                        setState(() => _reviewTab = values.single);
                      },
                    ),
                    Expanded(
                      child: IndexedStack(
                        index: _reviewTab,
                        sizing: StackFit.expand,
                        children: [
                          ExcludeFocus(
                            excluding: _reviewTab != 0,
                            child: PerformedProcedurePanel(
                              key: ValueKey(selected.appointmentId),
                              api: api!,
                              appointmentId: selected.appointmentId,
                              bookedServiceId: selected.serviceId,
                              bookedServiceLabel: selected.serviceLabel,
                              onPendingIntentChanged: (value) {
                                if (mounted && identical(_selected, selected)) {
                                  setState(() => _procedurePending = value);
                                }
                              },
                            ),
                          ),
                          ExcludeFocus(
                            excluding: _reviewTab != 1,
                            child: SingleChildScrollView(
                              child: ClinicalNotePanel(
                                key: ValueKey(selected.appointmentId),
                                api: api,
                                appointmentId: selected.appointmentId,
                                createAddendumRequestId: newRequestId,
                                onPendingIntentChanged: (value) {
                                  if (mounted &&
                                      identical(_selected, selected)) {
                                    setState(() => _notePending = value);
                                  }
                                },
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                )
              : SingleChildScrollView(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '${_date(widget.fromUtc)} – ${_date(widget.untilUtc)} (UTC, сүүлийн өдөр орохгүй)',
                      ),
                      if (_inFlight) const LinearProgressIndicator(),
                      if (_message case final value?) Text(value),
                      if (authenticated)
                        ..._assigned()
                      else ...[
                        TextField(
                          controller: _identifier,
                          enabled: !_inFlight,
                          decoration: const InputDecoration(
                            labelText: 'Эмчийн нэвтрэх нэр',
                          ),
                          autocorrect: false,
                          enableSuggestions: false,
                        ),
                        TextField(
                          controller: _password,
                          enabled: !_inFlight,
                          decoration: const InputDecoration(
                            labelText: 'Нууц үг',
                          ),
                          obscureText: true,
                          autocorrect: false,
                          enableSuggestions: false,
                        ),
                        FilledButton(
                          onPressed: _inFlight ? null : _login,
                          child: const Text('Эмчээр нэвтрэх'),
                        ),
                      ],
                    ],
                  ),
                ),
        ),
      ),
    );
  }

  List<Widget> _assigned() {
    final page = _page;
    return [
      const Text('Хуваарилсан үзлэг'),
      if (page != null && page.items.isEmpty)
        const Text('Энэ хуудсанд хуваарилсан үзлэг алга.'),
      for (final item in page?.items ?? const <AssignedEncounter>[])
        ListTile(
          title: Text(item.serviceLabel),
          subtitle: Text(item.startUtc.toIso8601String()),
          trailing: const Icon(Icons.chevron_right),
          onTap: () => setState(() {
            _procedurePending = _notePending = false;
            _reviewTab = 0;
            _selected = item;
          }),
        ),
      Wrap(
        spacing: 8,
        children: [
          TextButton(
            onPressed: _inFlight ? null : () => _load(_requestedPage),
            child: const Text('Үзлэг шинэчлэх'),
          ),
          TextButton(
            onPressed: _inFlight || _requestedPage == 1
                ? null
                : () => _load(_requestedPage - 1),
            child: const Text('Өмнөх хуудас'),
          ),
          TextButton(
            onPressed:
                _inFlight ||
                    page?.items.length != _size ||
                    _requestedPage == _maxPage
                ? null
                : () => _load(_requestedPage + 1),
            child: const Text('Дараах хуудас'),
          ),
        ],
      ),
    ];
  }
}
