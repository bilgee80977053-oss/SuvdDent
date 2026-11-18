import 'package:flutter/material.dart';

import 'dental_api.dart';
import 'director_catalog_api.dart';

/// The authenticated Director edits a real clinic Doctor profile. The backend
/// checks tenant, role and optimistic concurrency using the profile version.
class DirectorDoctorDialog extends StatefulWidget {
  final DirectorCatalogApi api;
  final DirectorDoctor doctor;
  const DirectorDoctorDialog({
    super.key,
    required this.api,
    required this.doctor,
  });

  @override
  State<DirectorDoctorDialog> createState() => _DirectorDoctorDialogState();
}

class _DirectorDoctorDialogState extends State<DirectorDoctorDialog> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _specialty;
  late final TextEditingController _startYear;
  late final TextEditingController _biography;
  late bool _published, _active;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final doctor = widget.doctor;
    _specialty = TextEditingController(text: doctor.specialty);
    _startYear = TextEditingController(text: doctor.practiceStartYear.toString());
    _biography = TextEditingController(text: doctor.biography ?? '');
    _published = doctor.isPublished;
    _active = doctor.isActive;
  }

  @override
  void dispose() {
    _specialty.dispose();
    _startYear.dispose();
    _biography.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_busy || !_form.currentState!.validate()) return;
    setState(() { _busy = true; _error = null; });
    try {
      await widget.api.updateDoctorProfile(
        widget.doctor,
        specialty: _specialty.text,
        practiceStartYear: int.parse(_startYear.text.trim()),
        biography: _biography.text.trim().isEmpty ? null : _biography.text.trim(),
        isPublished: _published,
        isActive: _active,
      );
      if (mounted) Navigator.pop(context, true);
    } on ApiFailure catch (error) {
      if (mounted) {
        setState(() => _error = error.status == 409
            ? 'Эмчийн мэдээлэл өөрчлөгдсөн байна. Жагсаалтыг шинэчлээд дахин оролдоно уу.'
            : error.message);
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'Хадгалалт баталгаажаагүй. Дахин шалгана уу.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text('Эмч: ${widget.doctor.fullName}'),
    content: SizedBox(
      width: 520,
      child: SingleChildScrollView(
        child: Form(
          key: _form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Text('Зөвхөн баталгаажсан мэргэжлийн мэдээлэл оруулна уу. '
                  'Нийтлэхээс өмнө эмчийн зөвшөөрлийг шалгана уу.'),
              const SizedBox(height: 14),
              TextFormField(
                controller: _specialty,
                enabled: !_busy,
                maxLength: 200,
                decoration: const InputDecoration(labelText: 'Мэргэжил'),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Мэргэжил шаардлагатай.'
                    : null,
              ),
              TextFormField(
                controller: _startYear,
                enabled: !_busy,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(labelText: 'Ажиллаж эхэлсэн он'),
                validator: (value) {
                  final year = int.tryParse(value?.trim() ?? '');
                  return year == null || year < 1900 || year > DateTime.now().year
                      ? 'Бодит он оруулна уу.'
                      : null;
                },
              ),
              TextFormField(
                controller: _biography,
                enabled: !_busy,
                maxLength: 4000,
                minLines: 2,
                maxLines: 4,
                decoration: const InputDecoration(labelText: 'Товч танилцуулга'),
              ),
              SwitchListTile(
                title: const Text('Эмчийн мэдээллийг нийтлэх'),
                value: _published,
                onChanged: _busy ? null : (value) => setState(() => _published = value),
              ),
              SwitchListTile(
                title: const Text('Идэвхтэй эмч'),
                value: _active,
                onChanged: _busy ? null : (value) => setState(() => _active = value),
              ),
              if (_error != null)
                Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
          ),
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: _busy ? null : () => Navigator.pop(context, false),
        child: const Text('Болих'),
      ),
      FilledButton(
        onPressed: _busy ? null : _save,
        child: const Text('Хадгалах'),
      ),
    ],
  );
}

/// One daily interval per day is a safe simple editor. Multiple existing
/// intervals are displayed but cannot be silently overwritten by this editor.
class DirectorHoursDialog extends StatefulWidget {
  final DirectorCatalogApi api;
  final DirectorDoctor doctor;
  const DirectorHoursDialog({
    super.key,
    required this.api,
    required this.doctor,
  });

  @override
  State<DirectorHoursDialog> createState() => _DirectorHoursDialogState();
}

class _DirectorHoursDialogState extends State<DirectorHoursDialog> {
  static const days = [
    'Ням', 'Даваа', 'Мягмар', 'Лхагва',
    'Пүрэв', 'Баасан', 'Бямба',
  ];
  final Map<int, bool> _enabled = {};
  final Map<int, TextEditingController> _starts = {};
  final Map<int, TextEditingController> _ends = {};
  bool _busy = false;
  String? _error;
  bool _multiInterval = false;

  static String _clock(int minutes) =>
      '${(minutes ~/ 60).toString().padLeft(2, '0')}:${(minutes % 60).toString().padLeft(2, '0')}';

  static int? _minute(String text) {
    if (!RegExp(r'^([01][0-9]|2[0-4]):[0-5][0-9]$').hasMatch(text)) {
      return null;
    }
    final parts = text.split(':').map(int.parse).toList();
    final result = parts[0] * 60 + parts[1];
    return result <= 1440 && (parts[0] != 24 || parts[1] == 0)
        ? result
        : null;
  }

  @override
  void initState() {
    super.initState();
    for (var day = 0; day < 7; day++) {
      final existing = widget.doctor.workingHours
          .where((hour) => hour.dayOfWeek == day).toList();
      if (existing.length > 1) _multiInterval = true;
      _enabled[day] = existing.isNotEmpty;
      _starts[day] = TextEditingController(
          text: existing.isEmpty ? '' : _clock(existing.first.startMinute));
      _ends[day] = TextEditingController(
          text: existing.isEmpty ? '' : _clock(existing.first.endMinute));
    }
  }

  @override
  void dispose() {
    for (final controller in [..._starts.values, ..._ends.values]) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    if (_busy || _multiInterval) return;
    final hours = <DirectorWorkingHour>[];
    for (var day = 0; day < 7; day++) {
      if (_enabled[day] != true) continue;
      final start = _minute(_starts[day]!.text.trim());
      final end = _minute(_ends[day]!.text.trim());
      if (start == null || end == null || start >= end ||
          start >= 1440 || end <= 0) {
        setState(() => _error = '${days[day]} гарагийн цагийг HH:MM хэлбэрээр оруулна уу.');
        return;
      }
      hours.add(DirectorWorkingHour(
          dayOfWeek: day, startMinute: start, endMinute: end));
    }
    setState(() { _busy = true; _error = null; });
    try {
      await widget.api.updateDoctorHours(widget.doctor, hours);
      if (mounted) Navigator.pop(context, true);
    } on ApiFailure catch (error) {
      if (mounted) {
        setState(() => _error = error.status == 409
            ? 'Хуваарь өөрчлөгдсөн байна. Жагсаалтыг шинэчлээд дахин оролдоно уу.'
            : error.message);
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'Хуваарийн хадгалалт баталгаажаагүй.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text('Ажлын хуваарь · ${widget.doctor.fullName}'),
    content: SizedBox(
      width: 520,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text('Цагийг эмнэлгийн орон нутгийн цагаар, HH:MM хэлбэрээр оруулна. '
                'Хадгалах нь өмнөх долоо хоногийн хуваарийг бүхэлд нь шинэчилнэ.'),
            if (_multiInterval)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 16),
                child: Text('Нэг өдөрт олон цагийн интервал бүртгэлтэй байна. '
                    'Өгөгдлийг алдагдуулахгүйн тулд энэ энгийн дэлгэцээр засахыг хориглов.'),
              )
            else
              for (var day = 0; day < 7; day++) ...[
                SwitchListTile(
                  title: Text(days[day]),
                  value: _enabled[day] ?? false,
                  onChanged: _busy ? null : (v) => setState(() => _enabled[day] = v),
                ),
                if (_enabled[day] == true)
                  Row(
                    children: [
                      Expanded(child: TextField(
                        controller: _starts[day],
                        enabled: !_busy,
                        decoration: const InputDecoration(labelText: 'Эхлэх', hintText: '09:00'),
                      )),
                      const SizedBox(width: 10),
                      Expanded(child: TextField(
                        controller: _ends[day],
                        enabled: !_busy,
                        decoration: const InputDecoration(labelText: 'Дуусах', hintText: '18:00'),
                      )),
                    ],
                  ),
              ],
            if (_error != null)
              Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
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
        onPressed: _busy || _multiInterval ? null : _save,
        child: const Text('Хадгалах'),
      ),
    ],
  );
}
