import 'package:flutter/material.dart';

import 'dental_api.dart';
import 'director_catalog_api.dart';

/// Only links an existing active Doctor-role clinic account to a draft profile.
/// This screen never grants roles, creates login credentials, or publishes staff.
class DirectorCreateDoctorDialog extends StatefulWidget {
  final DirectorCatalogApi api;

  const DirectorCreateDoctorDialog({super.key, required this.api});

  @override
  State<DirectorCreateDoctorDialog> createState() =>
      _DirectorCreateDoctorDialogState();
}

class _DirectorCreateDoctorDialogState
    extends State<DirectorCreateDoctorDialog> {
  final _form = GlobalKey<FormState>();
  final _specialty = TextEditingController();
  final _startYear = TextEditingController();
  final _biography = TextEditingController();
  List<EligibleDoctorAccount>? _eligible;
  int? _userId;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void dispose() {
    _specialty.dispose();
    _startYear.dispose();
    _biography.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    if (_busy || !mounted) return;
    setState(() {
      _busy = true;
      _error = null;
      _eligible = null;
      _userId = null;
    });
    try {
      final eligible = await widget.api.eligibleDoctors();
      if (mounted) setState(() => _eligible = eligible);
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Эмчийн эрхтэй хэрэглэгчдийн жагсаалт ачаалагдсангүй.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _save() async {
    if (_busy || _userId == null || !_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.createDoctorProfile(
        userId: _userId!,
        specialty: _specialty.text,
        practiceStartYear: int.parse(_startYear.text.trim()),
        biography: _biography.text.trim().isEmpty ? null : _biography.text.trim(),
      );
      if (mounted) Navigator.of(context).pop(true);
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() => _error = failure.status == 409
            ? 'Энэ эмчийн профайл аль хэдийн бүртгэгдсэн байна. Жагсаалтыг шинэчилнэ үү.'
            : failure.message);
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Бүртгэлийн үр дүн тодорхойгүй. Жагсаалтыг шалгана уу.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final eligible = _eligible;
    return AlertDialog(
      title: const Text('Эмчийн профайл бүртгэх'),
      content: SizedBox(
        width: 520,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text(
                'Зөвхөн энэ эмнэлэгт өмнө нь Doctor эрхтэй болсон '
                'ажилтныг сонгоно. Нэвтрэх эрх үүсгэхгүй. '
                'Профайл өвчтөнд шууд нийтлэгдэхгүй.',
              ),
              const SizedBox(height: 14),
              if (_busy) const LinearProgressIndicator(),
              if (eligible == null && !_busy)
                OutlinedButton(
                  onPressed: _load,
                  child: const Text('Жагсаалт дахин ачаалах'),
                ),
              if (eligible != null && eligible.isEmpty)
                const Text('Профайлгүй, Doctor эрхтэй идэвхтэй ажилтан алга. '
                    'Эхлээд ажилтны эрхийн бүртгэлийг баталгаажуулах шаардлагатай.'),
              if (eligible != null && eligible.isNotEmpty)
                Form(
                  key: _form,
                  child: Column(
                    children: [
                      DropdownButtonFormField<int>(
                        initialValue: _userId,
                        decoration: const InputDecoration(
                          labelText: 'Эмчийн хэрэглэгч',
                        ),
                        items: [
                          for (final doctor in eligible)
                            DropdownMenuItem<int>(
                              value: doctor.userId,
                              child: Text(doctor.fullName),
                            ),
                        ],
                        onChanged: _busy
                            ? null
                            : (value) => setState(() => _userId = value),
                        validator: (value) =>
                            value == null ? 'Эмчээ сонгоно уу.' : null,
                      ),
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _specialty,
                        enabled: !_busy,
                        maxLength: 200,
                        decoration: const InputDecoration(
                          labelText: 'Баталгаажсан мэргэжил',
                        ),
                        validator: (value) =>
                            value == null || value.trim().isEmpty
                                ? 'Мэргэжил шаардлагатай.'
                                : null,
                      ),
                      TextFormField(
                        controller: _startYear,
                        enabled: !_busy,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(
                          labelText: 'Ажиллаж эхэлсэн он',
                        ),
                        validator: (value) {
                          final year = int.tryParse(value?.trim() ?? '');
                          return year == null ||
                                  year < 1900 ||
                                  year > DateTime.now().year
                              ? 'Бодит он оруулна уу.'
                              : null;
                        },
                      ),
                      TextFormField(
                        controller: _biography,
                        enabled: !_busy,
                        minLines: 2,
                        maxLines: 4,
                        maxLength: 4000,
                        decoration: const InputDecoration(
                          labelText: 'Товч танилцуулга (заавал биш)',
                        ),
                      ),
                    ],
                  ),
                ),
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
          onPressed: _busy || eligible == null || eligible.isEmpty || _userId == null
              ? null
              : _save,
          child: const Text('Бүртгэх'),
        ),
      ],
    );
  }
}
