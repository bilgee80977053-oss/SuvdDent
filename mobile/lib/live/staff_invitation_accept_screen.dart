import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

import 'models.dart';

/// Public, tokenless onboarding. This screen never displays the invitation
/// code to a Director or stores the password in local storage.
class StaffInvitationAcceptScreen extends StatefulWidget {
  final ApiSettings settings;
  final http.Client? client;

  const StaffInvitationAcceptScreen({
    super.key,
    required this.settings,
    this.client,
  });

  @override
  State<StaffInvitationAcceptScreen> createState() =>
      _StaffInvitationAcceptScreenState();
}

class _StaffInvitationAcceptScreenState
    extends State<StaffInvitationAcceptScreen> {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _code = TextEditingController();
  final _password = TextEditingController();
  late final http.Client _client;
  bool _busy = false, _completed = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _client = widget.client ?? http.Client();
  }

  @override
  void dispose() {
    _email.dispose();
    _code.dispose();
    _password.dispose();
    if (widget.client == null) _client.close();
    super.dispose();
  }

  Future<void> _accept() async {
    if (_busy || !_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final response = await _client.post(
        Uri.parse(widget.settings.baseUrl)
            .replace(path: '/api/staff-invitations/accept'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({
          'clinicId': widget.settings.clinicId,
          'email': _email.text.trim().toLowerCase(),
          'code': _code.text.trim(),
          'newPassword': _password.text,
        }),
      ).timeout(const Duration(seconds: 20));
      if (!mounted) return;
      if (response.statusCode == 200) {
        _password.clear();
        _code.clear();
        setState(() => _completed = true);
      } else {
        setState(() => _error = response.statusCode == 429
            ? 'Хэт олон оролдлого хийсэн. Түр хүлээгээд дахин оролдоно уу.'
            : 'Баталгаажуулах код буруу, хугацаа дууссан эсвэл мэдээлэл тохирохгүй байна.');
      }
    } on TimeoutException {
      if (mounted) {
        setState(() => _error =
            'Серверийн хариу удааширлаа. Бүртгэл идэвхжсэн эсэхийг шалгана уу.');
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Сервертэй холбогдож чадсангүй.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Suvd Dent · Эмчийн урилга')),
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 560),
          child: ListView(
            padding: const EdgeInsets.all(24),
            children: [
              Text('Эмчийн бүртгэл баталгаажуулах',
                  style: Theme.of(context).textTheme.headlineSmall),
              const SizedBox(height: 14),
              if (_completed)
                const Card(
                  child: Padding(
                    padding: EdgeInsets.all(20),
                    child: Text('Бүртгэл амжилттай идэвхжлээ. '
                        'Өөрийн имэйл, нууц үгээр нэвтэрч болно. '
                        'Эмчийн профайлыг захирал тусад нь баталгаажуулж нийтэлнэ.'),
                  ),
                )
              else
                Form(
                  key: _form,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const Text('Эмнэлгээс ирсэн 8 оронтой код болон '
                          'өөрийн шинэ нууц үгийг оруулна уу. '
                          'Нууц үгээ захиралд эсвэл бусдад бүү дамжуулаарай.'),
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _email,
                        enabled: !_busy,
                        keyboardType: TextInputType.emailAddress,
                        decoration: const InputDecoration(labelText: 'Урилга ирсэн имэйл'),
                        validator: (v) => v == null || v.trim().isEmpty
                            ? 'Имэйл шаардлагатай.'
                            : null,
                      ),
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _code,
                        enabled: !_busy,
                        keyboardType: TextInputType.number,
                        maxLength: 8,
                        decoration: const InputDecoration(labelText: '8 оронтой код'),
                        validator: (v) =>
                            !RegExp(r'^\d{8}$').hasMatch(v?.trim() ?? '')
                                ? '8 оронтой код оруулна уу.'
                                : null,
                      ),
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _password,
                        enabled: !_busy,
                        obscureText: true,
                        decoration: const InputDecoration(
                          labelText: 'Шинэ нууц үг (12–128 тэмдэгт)',
                        ),
                        validator: (v) =>
                            v == null || v.length < 12 || v.length > 128
                                ? '12–128 тэмдэгттэй нууц үг оруулна уу.'
                                : null,
                      ),
                      const SizedBox(height: 16),
                      if (_error != null)
                        Text(_error!,
                            style: TextStyle(
                                color: Theme.of(context).colorScheme.error)),
                      FilledButton(
                        onPressed: _busy ? null : _accept,
                        child: const Text('Бүртгэл баталгаажуулах'),
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
