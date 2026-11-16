import 'package:flutter/material.dart';

import 'live_flow_controller.dart';
import 'models.dart';

/// Edits only the authenticated patient's profile through PUT /api/Patients/me.
/// Contact editing here does not verify phone or email ownership.
class ProfileSettingsScreen extends StatefulWidget {
  final LiveFlowController flow;
  const ProfileSettingsScreen({super.key, required this.flow});

  @override
  State<ProfileSettingsScreen> createState() => _ProfileSettingsScreenState();
}

class _ProfileSettingsScreenState extends State<ProfileSettingsScreen> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _address = TextEditingController();
  DateTime? _birthDate;
  bool _birthDateProvided = false;
  late final AuthSession? _originalSession;
  late final int? _originalPatientId;

  LiveFlowController get flow => widget.flow;
  bool get _authenticated =>
      _originalSession != null &&
      identical(flow.session, _originalSession) &&
      flow.patient?.id == _originalPatientId;
  bool get _canEdit => _authenticated && !flow.busy;

  @override
  void initState() {
    super.initState();
    _originalSession = flow.session;
    _originalPatientId = flow.patient?.id;
    _loadFields();
    flow.addListener(_changed);
  }

  void _loadFields() {
    final profile = flow.patient;
    if (profile == null) return;
    _name.text = profile.fullName;
    _phone.text = profile.phone;
    _address.text = profile.address ?? '';
    final raw = profile.dateOfBirth;
    _birthDateProvided = raw != null && raw.trim().isNotEmpty;
    _birthDate = _birthDateProvided
        ? DateTime.tryParse(raw!.substring(0, raw.length < 10 ? raw.length : 10))
        : null;
  }

  void _changed() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    flow.removeListener(_changed);
    _name.dispose();
    _phone.dispose();
    _address.dispose();
    super.dispose();
  }

  static String _date(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-'
      '${date.month.toString().padLeft(2, '0')}-'
      '${date.day.toString().padLeft(2, '0')}';

  Future<void> _chooseDate() async {
    if (!_canEdit) return;
    final now = DateTime.now();
    final selected = await showDatePicker(
      context: context,
      initialDate: _birthDate ?? DateTime(now.year - 20, now.month, now.day),
      firstDate: DateTime(1900),
      lastDate: now,
      helpText: 'Төрсөн огноо сонгох',
    );
    if (!mounted || !_canEdit || selected == null) return;
    setState(() {
      _birthDate = selected;
      _birthDateProvided = true;
    });
  }

  Future<void> _save() async {
    if (!_canEdit || !_form.currentState!.validate()) return;
    final session = _originalSession;
    final patientId = _originalPatientId;
    await flow.updateProfile(
      fullName: _name.text.trim(),
      phone: _phone.text.trim(),
      dateOfBirth: _birthDateProvided && _birthDate != null
          ? _date(_birthDate!)
          : null,
      address: _address.text.trim().isEmpty ? null : _address.text.trim(),
    );
    if (!mounted || !identical(flow.session, session) ||
        flow.patient?.id != patientId) {
      return;
    }
    // Success is only reported after the server acknowledges the update.
    // If the response is lost, refresh the saved profile before retrying.
  }

  @override
  Widget build(BuildContext context) {
    if (!_authenticated) {
      return Scaffold(
        appBar: AppBar(title: const Text('Профайлын тохиргоо')),
        body: const Center(
          child: Padding(
            padding: EdgeInsets.all(24),
            child: Text('Нэвтрэх төлөв өөрчлөгдсөн. Дахин нэвтэрнэ үү.'),
          ),
        ),
      );
    }
    return Scaffold(
      appBar: AppBar(title: const Text('Профайлын тохиргоо')),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 680),
            child: ListView(
              padding: const EdgeInsets.all(22),
              children: [
                Text('Миний мэдээлэл',
                    style: Theme.of(context).textTheme.headlineSmall),
                const SizedBox(height: 8),
                const Text(
                  'Өөрийн эмнэлгийн бүртгэл дэх нэр, холбоо барих утас, '
                  'төрсөн огноо, хаягаа шинэчилнэ. Утас солих нь '
                  'тухайн дугаарыг автоматаар баталгаажуулахгүй.',
                ),
                const SizedBox(height: 22),
                Form(
                  key: _form,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      TextFormField(
                        controller: _name,
                        enabled: _canEdit,
                        maxLength: 120,
                        textCapitalization: TextCapitalization.words,
                        decoration: const InputDecoration(labelText: 'Овог, нэр'),
                        validator: (value) =>
                            value == null || value.trim().isEmpty
                                ? 'Нэрээ оруулна уу.'
                                : null,
                      ),
                      const SizedBox(height: 10),
                      TextFormField(
                        controller: _phone,
                        enabled: _canEdit,
                        maxLength: 8,
                        keyboardType: TextInputType.phone,
                        decoration: const InputDecoration(labelText: 'Утасны дугаар'),
                        validator: (value) =>
                            value != null && RegExp(r'^\d{8}$').hasMatch(value.trim())
                                ? null
                                : '8 оронтой утасны дугаар оруулна уу.',
                      ),
                      const SizedBox(height: 10),
                      TextFormField(
                        controller: _address,
                        enabled: _canEdit,
                        maxLength: 250,
                        maxLines: 2,
                        decoration: const InputDecoration(
                            labelText: 'Хаяг (заавал биш)'),
                      ),
                      const SizedBox(height: 12),
                      OutlinedButton.icon(
                        onPressed: _canEdit ? _chooseDate : null,
                        icon: const Icon(Icons.calendar_today_outlined),
                        label: Text(_birthDate != null
                            ? 'Төрсөн огноо: ${_date(_birthDate!)}'
                            : 'Төрсөн огноо сонгох (заавал биш)'),
                      ),
                      if (_birthDateProvided)
                        TextButton(
                          onPressed: _canEdit
                              ? () => setState(() {
                                  _birthDate = null;
                                  _birthDateProvided = false;
                                })
                              : null,
                          child: const Text('Төрсөн огноог арилгах'),
                        ),
                      const SizedBox(height: 22),
                      if (flow.busy) ...[
                        const LinearProgressIndicator(),
                        const SizedBox(height: 10),
                        const Text('Хадгалж байна…'),
                      ],
                      if (flow.error != null) ...[
                        Text(flow.error!,
                            style: TextStyle(
                                color: Theme.of(context).colorScheme.error)),
                        const SizedBox(height: 12),
                      ],
                      if (flow.notice != null) ...[
                        Text(flow.notice!, key: const ValueKey('profile-save-status')),
                        const SizedBox(height: 12),
                      ],
                      FilledButton.icon(
                        onPressed: _canEdit ? _save : null,
                        icon: const Icon(Icons.save_outlined),
                        label: const Text('Өөрчлөлт хадгалах'),
                      ),
                      OutlinedButton(
                        onPressed: _canEdit
                            ? () async {
                                await flow.loadProfile();
                                if (mounted && _authenticated && flow.error == null) {
                                  setState(_loadFields);
                                }
                              }
                            : null,
                        child: const Text('Серверээс дахин ачаалах'),
                      ),
                      const SizedBox(height: 12),
                      const Text(
                        'Нэвтрэх имэйл, баталгаажуулалт, нууц үг '
                        'энэ дэлгэцээс өөрчлөгдөхгүй.',
                        style: TextStyle(fontSize: 12),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
