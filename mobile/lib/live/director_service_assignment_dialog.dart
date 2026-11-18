import 'package:flutter/material.dart';

import 'dental_api.dart';
import 'director_catalog_api.dart';

class DirectorServiceAssignmentDialog extends StatefulWidget {
  final DirectorCatalogApi api;
  final DirectorDoctor doctor;
  final List<DirectorService> clinicServices;

  const DirectorServiceAssignmentDialog({
    super.key,
    required this.api,
    required this.doctor,
    required this.clinicServices,
  });

  @override
  State<DirectorServiceAssignmentDialog> createState() =>
      _DirectorServiceAssignmentDialogState();
}

class _DirectorServiceAssignmentDialogState
    extends State<DirectorServiceAssignmentDialog> {
  final _form = GlobalKey<FormState>();
  final _duration = TextEditingController();
  final _before = TextEditingController(text: '0');
  final _after = TextEditingController(text: '0');
  DirectorDoctorServiceCatalog? _snapshot;
  int? _serviceId;
  bool _active = false, _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void dispose() {
    _duration.dispose();
    _before.dispose();
    _after.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    if (_busy || !mounted) return;
    setState(() {
      _busy = true;
      _error = null;
      _snapshot = null;
      _serviceId = null;
    });
    try {
      final snapshot = await widget.api.doctorServices(widget.doctor.id);
      if (mounted) setState(() => _snapshot = snapshot);
    } on ApiFailure catch (error) {
      if (mounted) setState(() => _error = error.message);
    } catch (_) {
      if (mounted) setState(() => _error = 'Холболтыг ачаалж чадсангүй.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _select(int? id) {
    if (_busy) return;
    final rows = _snapshot?.services.where((s) => s.serviceId == id);
    final current = rows == null || rows.isEmpty ? null : rows.first;
    setState(() {
      _serviceId = id;
      _duration.text = current?.durationMinutes.toString() ?? '';
      _before.text = current?.bufferBeforeMinutes.toString() ?? '0';
      _after.text = current?.bufferAfterMinutes.toString() ?? '0';
      _active = current?.isActive ?? false;
      _error = null;
    });
  }

  String? _minutes(String? input, int min, int max) {
    final value = int.tryParse(input?.trim() ?? '');
    return value == null || value < min || value > max
        ? '$min–$max минут оруулна уу.'
        : null;
  }

  Future<void> _save() async {
    final snapshot = _snapshot;
    final id = _serviceId;
    if (_busy || snapshot == null || id == null ||
        !_form.currentState!.validate()) return;

    final duration = int.parse(_duration.text.trim());
    final before = int.parse(_before.text.trim());
    final after = int.parse(_after.text.trim());
    if (duration + before + after > 1440) {
      setState(() => _error =
          'Үйлчилгээ болон завсарлагын нийлбэр 1440 минутаас их байна.');
      return;
    }
    final item = DirectorDoctorService(
      serviceId: id,
      durationMinutes: duration,
      bufferBeforeMinutes: before,
      bufferAfterMinutes: after,
      isActive: _active,
    );
    final next = [
      for (final existing in snapshot.services)
        if (existing.serviceId != id) existing,
      item,
    ];
    setState(() { _busy = true; _error = null; });
    try {
      await widget.api.saveDoctorServices(snapshot, next);
      if (mounted) Navigator.pop(context, true);
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() => _error = failure.status == 409
            ? 'Мэдээлэл өөрчлөгдсөн байна. Дахин ачаалаад шалгана уу.'
            : failure.message);
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Хадгалалтын үр дүн тодорхойгүй. Шинэчлээд шалгана уу.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final catalog = widget.clinicServices.where((s) =>
        s.isActive ||
        (_snapshot?.services.any((link) => link.serviceId == s.id) ?? false))
        .toList();
    return AlertDialog(
      title: Text('Эмчийн үйлчилгээ · ${widget.doctor.fullName}'),
      content: SizedBox(
        width: 520,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text('Баталгаажсан үйлчилгээг сонгож, минутын '
                  'хугацаа болон завсарлагыг тохируулна. '
                  'Шинэ холбоос анх идэвхгүй байна.'),
              const SizedBox(height: 12),
              if (_busy) const LinearProgressIndicator(),
              if (_snapshot == null && !_busy)
                OutlinedButton(
                  onPressed: _load,
                  child: const Text('Холболтыг дахин ачаалах'),
                ),
              if (_snapshot != null) ...[
                if (catalog.isEmpty)
                  const Text('Үйлчилгээний каталог хоосон байна.')
                else ...[
                  DropdownButtonFormField<int>(
                    value: _serviceId,
                    decoration: const InputDecoration(labelText: 'Үйлчилгээ'),
                    items: [
                      for (final service in catalog)
                        DropdownMenuItem<int>(
                          value: service.id,
                          child: Text(service.name),
                        ),
                    ],
                    onChanged: _busy ? null : _select,
                  ),
                  if (_serviceId != null)
                    Form(
                      key: _form,
                      child: Column(
                        children: [
                          const SizedBox(height: 12),
                          TextFormField(
                            controller: _duration,
                            enabled: !_busy,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'Үйлчилгээний хугацаа (минут)',
                            ),
                            validator: (v) => _minutes(v, 1, 1440),
                          ),
                          const SizedBox(height: 10),
                          TextFormField(
                            controller: _before,
                            enabled: !_busy,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'Өмнөх завсарлага (минут)',
                            ),
                            validator: (v) => _minutes(v, 0, 1439),
                          ),
                          const SizedBox(height: 10),
                          TextFormField(
                            controller: _after,
                            enabled: !_busy,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'Дараах завсарлага (минут)',
                            ),
                            validator: (v) => _minutes(v, 0, 1439),
                          ),
                          SwitchListTile(
                            title: const Text('Үйлчилгээг идэвхжүүлэх'),
                            value: _active,
                            onChanged: _busy ? null : (value) =>
                                setState(() => _active = value),
                          ),
                        ],
                      ),
                    ),
                ],
              ],
              if (_error != null) ...[
                const SizedBox(height: 12),
                Text(_error!,
                    style: TextStyle(
                        color: Theme.of(context).colorScheme.error)),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _busy ? null : () => Navigator.pop(context, false),
          child: const Text('Болих'),
        ),
        FilledButton(
          onPressed: _busy || _serviceId == null ? null : _save,
          child: const Text('Хадгалах'),
        ),
      ],
    );
  }
}
