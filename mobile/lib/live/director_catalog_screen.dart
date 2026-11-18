import 'package:flutter/material.dart';

import '../app/app_theme.dart';
import 'director_catalog_api.dart';
import 'director_doctor_dialog.dart';
import 'dental_api.dart';
import 'models.dart';

/// Separate Director-only administration; patient tokens cannot access this UI.
class DirectorCatalogScreen extends StatefulWidget {
  final ApiSettings settings;
  final DirectorCatalogApi? api;

  const DirectorCatalogScreen({super.key, required this.settings, this.api});

  @override
  State<DirectorCatalogScreen> createState() => _DirectorCatalogScreenState();
}

class _DirectorCatalogScreenState extends State<DirectorCatalogScreen> {
  late final DirectorCatalogApi api;
  final _identifier = TextEditingController();
  final _password = TextEditingController();
  final _formKey = GlobalKey<FormState>();
  bool _busy = false;
  bool _signedIn = false;
  String? _error;
  String? _notice;
  List<DirectorService> _services = const [];
  List<DirectorDoctor> _doctors = const [];
  bool _doctorsLoaded = false;

  @override
  void initState() {
    super.initState();
    api = widget.api ?? DirectorCatalogApi(widget.settings);
  }

  @override
  void dispose() {
    _identifier.dispose();
    _password.dispose();
    api.close();
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
      _notice = null;
    });
    try {
      await action();
    } on ApiFailure catch (ex) {
      if (mounted) setState(() => _error = ex.message);
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Үйлдлийг гүйцэтгэж чадсангүй. Дахин шалгана уу.');
      }
    } finally {
      if (mounted) {
        setState(() {
          _busy = false;
          if (!api.authenticated) {
            _signedIn = false;
            _services = const [];
            _doctors = const [];
            _doctorsLoaded = false;
          }
        });
      }
    }
  }

  Future<void> _login() async {
    if (!_formKey.currentState!.validate()) return;
    await _run(() async {
      await api.login(_identifier.text.trim(), _password.text);
      _password.clear();
      final rows = await api.services();
      if (!mounted) return;
      setState(() {
        _services = rows;
        _signedIn = true;
      });
    });
  }

  Future<void> _reload() async {
    await _run(() async {
      final rows = await api.services();
      if (mounted) setState(() => _services = rows);
    });
  }

  void _logout() {
    api.logout();
    _identifier.clear();
    _password.clear();
    setState(() {
      _signedIn = false;
      _services = const [];
      _doctors = const [];
      _doctorsLoaded = false;
      _notice = null;
      _error = null;
    });
  }

  Future<void> _loadDoctors() async {
    await _run(() async {
      final rows = await api.doctors();
      if (!mounted) return;
      setState(() {
        _doctors = rows;
        _doctorsLoaded = true;
      });
    });
  }

  Future<void> _editDoctor(DirectorDoctor doctor, {required bool hours}) async {
    if (!_signedIn || _busy) return;
    final saved = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (_) => hours
          ? DirectorHoursDialog(api: api, doctor: doctor)
          : DirectorDoctorDialog(api: api, doctor: doctor),
    );
    if (!mounted || saved != true) return;
    await _loadDoctors();
    if (mounted && _error == null) {
      setState(() => _notice = hours
          ? 'Эмчийн ажлын цаг шинэчлэгдлээ.'
          : 'Эмчийн мэдээлэл шинэчлэгдлээ.');
    }
  }

  static const _dayNames = [
    'Ням', 'Даваа', 'Мягмар', 'Лхагва',
    'Пүрэв', 'Баасан', 'Бямба',
  ];

  String _workHour(DirectorWorkingHour hour) {
    String clock(int minutes) =>
        '${(minutes ~/ 60).toString().padLeft(2, '0')}:${(minutes % 60).toString().padLeft(2, '0')}';
    final day = hour.dayOfWeek >= 0 && hour.dayOfWeek < _dayNames.length
        ? _dayNames[hour.dayOfWeek]
        : 'Өдөр ${hour.dayOfWeek}';
    return '$day ${clock(hour.startMinute)}–${clock(hour.endMinute)}';
  }

  Widget _doctorRoster() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const SizedBox(height: 28),
      Text('Эмч, ажлын хуваарь',
          style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      const Text(
        'Энд зөвхөн серверт бүртгэлтэй эмч, ажлын цагийг харуулна. '
        'Энэ дэлгэцээс эмч нэмж, хуваарь өөрчлөхгүй.',
      ),
      const SizedBox(height: 12),
      OutlinedButton.icon(
        onPressed: _busy ? null : _loadDoctors,
        icon: const Icon(Icons.medical_services_outlined),
        label: Text(_doctorsLoaded
            ? 'Эмчийн мэдээлэл шинэчлэх'
            : 'Эмчийн жагсаалт харах'),
      ),
      if (_doctorsLoaded) ...[
        const SizedBox(height: 12),
        if (_doctors.isEmpty)
          const Card(
            child: Padding(
              padding: EdgeInsets.all(18),
              child: Text('Эмчийн бүртгэл одоогоор алга. '
                  'Захирал эмчийн бүртгэл, цагийн хуваарийг баталгаажуулах шаардлагатай.'),
            ),
          ),
        for (final doctor in _doctors) ...[
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(doctor.fullName,
                      style: Theme.of(context).textTheme.titleMedium),
                  const SizedBox(height: 5),
                  Text(doctor.specialty),
                  const SizedBox(height: 5),
                  Text(doctor.isActive
                      ? (doctor.isPublished ? 'Нийтлэгдсэн' : 'Нийтлээгүй')
                      : 'Идэвхгүй'),
                  const SizedBox(height: 8),
                  if (doctor.workingHours.isEmpty)
                    const Text('Ажлын цаг тохируулаагүй')
                  else
                    for (final hour in doctor.workingHours)
                      Text(_workHour(hour)),
                  const SizedBox(height: 12),
                  Wrap(
                    spacing: 8,
                    children: [
                      OutlinedButton(
                        onPressed: _busy ? null : () => _editDoctor(doctor, hours: false),
                        child: const Text('Эмчийн мэдээлэл засах'),
                      ),
                      OutlinedButton(
                        onPressed: _busy ? null : () => _editDoctor(doctor, hours: true),
                        child: const Text('Ажлын цаг засах'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 8),
        ],
      ],
    ],
  );

  Future<void> _edit(DirectorService? existing) async {
    if (!_signedIn || _busy) return;
    final saved = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _DirectorServiceDialog(api: api, existing: existing),
    );
    if (!mounted || saved != true) return;
    await _reload();
    if (mounted && _error == null) {
      setState(() => _notice = 'Үйлчилгээний бүртгэл шинэчлэгдлээ.');
    }
  }

  Future<void> _toggle(DirectorService service) async {
    if (_busy || !_signedIn) return;
    final confirm = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(service.isActive ? 'Үйлчилгээг идэвхгүй болгох уу?' : 'Үйлчилгээг идэвхжүүлэх үү?'),
        content: Text(service.name),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Болих'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Баталгаажуулах'),
          ),
        ],
      ),
    );
    if (!mounted || confirm != true) return;
    await _run(() async {
      await api.setActive(service, !service.isActive);
      final rows = await api.services();
      if (mounted) setState(() => _services = rows);
    });
  }

  Widget _loginForm() => Form(
    key: _formKey,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Text('Зөвхөн Suvd Dent-ийн эрх бүхий захирал нэвтэрнэ.'),
        const SizedBox(height: 18),
        TextFormField(
          controller: _identifier,
          enabled: !_busy,
          decoration: const InputDecoration(labelText: 'Захирлын утас эсвэл имэйл'),
          validator: (value) => value == null || value.trim().isEmpty
              ? 'Нэвтрэх нэрээ оруулна уу.'
              : null,
        ),
        const SizedBox(height: 12),
        TextFormField(
          controller: _password,
          obscureText: true,
          enabled: !_busy,
          decoration: const InputDecoration(labelText: 'Нууц үг'),
          validator: (value) => value == null || value.isEmpty
              ? 'Нууц үгээ оруулна уу.'
              : null,
        ),
        const SizedBox(height: 20),
        FilledButton.icon(
          onPressed: _busy ? null : _login,
          icon: const Icon(Icons.lock_open_outlined),
          label: const Text('Захирлаар нэвтрэх'),
        ),
      ],
    ),
  );

  Widget _catalog() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text(
        'Зөвхөн эмнэлгийн баталгаажуулсан үйлчилгээ, үнэ оруулна уу. '
        'Туршилтын үйлчилгээ нь бодит өвчтөнд харагдаж болзошгүй.',
      ),
      const SizedBox(height: 16),
      FilledButton.icon(
        onPressed: _busy ? null : () => _edit(null),
        icon: const Icon(Icons.add),
        label: const Text('Шинэ үйлчилгээ бүртгэх'),
      ),
      OutlinedButton.icon(
        onPressed: _busy ? null : _reload,
        icon: const Icon(Icons.refresh),
        label: const Text('Жагсаалтыг шинэчлэх'),
      ),
      const SizedBox(height: 20),
      if (_services.isEmpty)
        const Card(
          child: Padding(
            padding: EdgeInsets.all(18),
            child: Text('Үйлчилгээ одоогоор бүртгэгдээгүй байна.'),
          ),
        ),
      for (final service in _services) ...[
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(service.name, style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 6),
                Text(service.description),
                const SizedBox(height: 8),
                Text(
                  service.minPrice <= 0
                      ? 'Үнэ баталгаажаагүй'
                      : service.maxPrice == null || service.maxPrice == service.minPrice
                          ? '${service.minPrice} ₮'
                          : '${service.minPrice}–${service.maxPrice} ₮',
                ),
                const SizedBox(height: 8),
                Text(service.isActive ? 'Идэвхтэй' : 'Идэвхгүй',
                    style: TextStyle(color: service.isActive ? AppColors.green : AppColors.muted)),
                const SizedBox(height: 10),
                Wrap(
                  spacing: 8,
                  children: [
                    OutlinedButton(
                      onPressed: _busy ? null : () => _edit(service),
                      child: const Text('Засах'),
                    ),
                    OutlinedButton(
                      onPressed: _busy ? null : () => _toggle(service),
                      child: Text(service.isActive ? 'Идэвхгүй болгох' : 'Идэвхжүүлэх'),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 10),
      ],
      _doctorRoster(),
    ],
  );

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Suvd Dent · Захирлын хэсэг'),
      actions: [
        if (_signedIn)
          IconButton(
            tooltip: 'Гарах',
            icon: const Icon(Icons.logout),
            onPressed: _busy ? null : _logout,
          ),
      ],
    ),
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 760),
          child: ListView(
            padding: const EdgeInsets.all(24),
            children: [
              Text('Үйлчилгээний удирдлага',
                  style: Theme.of(context).textTheme.headlineMedium),
              const SizedBox(height: 18),
              if (_busy) const LinearProgressIndicator(),
              if (_error != null)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 10),
                  child: Text(_error!,
                      style: TextStyle(color: Theme.of(context).colorScheme.error)),
                ),
              if (_notice != null)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 10),
                  child: Text(_notice!),
                ),
              _signedIn ? _catalog() : _loginForm(),
            ],
          ),
        ),
      ),
    ),
  );
}

class _DirectorServiceDialog extends StatefulWidget {
  final DirectorCatalogApi api;
  final DirectorService? existing;
  const _DirectorServiceDialog({required this.api, this.existing});

  @override
  State<_DirectorServiceDialog> createState() => _DirectorServiceDialogState();
}

class _DirectorServiceDialogState extends State<_DirectorServiceDialog> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _name;
  late final TextEditingController _description;
  late final TextEditingController _min;
  late final TextEditingController _max;
  late final TextEditingController _aftercare;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final s = widget.existing;
    _name = TextEditingController(text: s?.name ?? '');
    _description = TextEditingController(text: s?.description ?? '');
    _min = TextEditingController(text: s?.minPrice.toString() ?? '');
    _max = TextEditingController(text: s?.maxPrice?.toString() ?? '');
    _aftercare = TextEditingController(text: s?.aftercareText ?? '');
  }

  @override
  void dispose() {
    for (final controller in [_name, _description, _min, _max, _aftercare]) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    if (_busy || !_form.currentState!.validate()) return;
    final low = int.parse(_min.text.trim());
    final high = _max.text.trim().isEmpty ? null : int.parse(_max.text.trim());
    if (high != null && high < low) {
      setState(() => _error = 'Үнийн дээд хэмжээ доод хэмжээнээс бага байна.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.saveService(
        id: widget.existing?.id,
        name: _name.text,
        description: _description.text,
        minPrice: low,
        maxPrice: high,
        aftercareText: _aftercare.text,
      );
      if (mounted) Navigator.of(context).pop(true);
    } on ApiFailure catch (ex) {
      if (mounted) setState(() => _error = ex.message);
    } catch (_) {
      if (mounted) setState(() => _error = 'Хадгалалт баталгаажаагүй. Жагсаалтыг шалгана уу.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String? _priceValidator(String? value, {bool optional = false}) {
    if (optional && (value == null || value.trim().isEmpty)) return null;
    final number = int.tryParse(value?.trim() ?? '');
    if (number == null || number < 0 || number > 1000000000) {
      return '0–1,000,000,000 хооронд бүхэл тоо оруулна уу.';
    }
    return null;
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.existing == null ? 'Үйлчилгээ бүртгэх' : 'Үйлчилгээ засах'),
    content: SizedBox(
      width: 520,
      child: SingleChildScrollView(
        child: Form(
          key: _form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Text('Зөвхөн албан ёсны мэдээлэл оруулна уу. '
                  'Үнэ тодорхойгүй бол 0 гэж оруулна (үнэгүй гэсэн үг биш).'),
              const SizedBox(height: 12),
              TextFormField(
                controller: _name,
                enabled: !_busy,
                maxLength: 160,
                decoration: const InputDecoration(labelText: 'Үйлчилгээний нэр'),
                validator: (v) => v == null || v.trim().length < 2
                    ? 'Хоёроос дээш тэмдэгт оруулна уу.'
                    : null,
              ),
              TextFormField(
                controller: _description,
                enabled: !_busy,
                maxLength: 1500,
                minLines: 2,
                maxLines: 4,
                decoration: const InputDecoration(labelText: 'Тайлбар'),
                validator: (v) => v == null || v.trim().isEmpty
                    ? 'Тайлбар шаардлагатай.'
                    : null,
              ),
              TextFormField(
                controller: _min,
                enabled: !_busy,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(labelText: 'Үндсэн үнэ (₮)'),
                validator: (v) => _priceValidator(v),
              ),
              TextFormField(
                controller: _max,
                enabled: !_busy,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(labelText: 'Үнийн дээд хэмжээ (заавал биш)'),
                validator: (v) => _priceValidator(v, optional: true),
              ),
              TextFormField(
                controller: _aftercare,
                enabled: !_busy,
                maxLength: 2500,
                minLines: 2,
                maxLines: 4,
                decoration: const InputDecoration(labelText: 'Дараах арчилгааны заавар'),
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
