import 'package:flutter/material.dart';

import 'dental_api.dart';
import 'director_catalog_api.dart';

/// A Director can invite an existing real employee by email. The invite
/// creates an inactive Doctor account; the invitee sets their own password.
class DirectorStaffInviteDialog extends StatefulWidget {
  final DirectorCatalogApi api;

  const DirectorStaffInviteDialog({super.key, required this.api});

  @override
  State<DirectorStaffInviteDialog> createState() =>
      _DirectorStaffInviteDialogState();
}

class _DirectorStaffInviteDialogState extends State<DirectorStaffInviteDialog> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    if (_busy || !_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.inviteDoctor(
        fullName: _name.text,
        email: _email.text,
      );
      if (mounted) Navigator.of(context).pop(true);
    } on ApiFailure catch (error) {
      if (mounted) {
        setState(() => _error = error.status == 409
            ? 'Энэ имэйлээр бүртгэл байна эсвэл урилга илгээх боломжгүй.'
            : error.message);
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'Урилгын илгээлтийг баталгаажуулж чадсангүй.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Эмчид урилга илгээх'),
    content: SizedBox(
      width: 510,
      child: SingleChildScrollView(
        child: Form(
          key: _form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text(
                'Зөвхөн эмнэлэгт ажиллахаар баталгаажсан хүний нэр, '
                'өөрийнх нь имэйлийг оруулна уу. Урилга 15 минут хүчинтэй. '
                'Нууц үгийг эмч өөрөө тохируулна. Профайл нийтлэгдэхгүй.',
              ),
              const SizedBox(height: 12),
              TextFormField(
                controller: _name,
                maxLength: 200,
                enabled: !_busy,
                decoration: const InputDecoration(labelText: 'Эмчийн бүтэн нэр'),
                validator: (v) => v == null || v.trim().length < 2
                    ? 'Бүтэн нэр шаардлагатай.'
                    : null,
              ),
              TextFormField(
                controller: _email,
                maxLength: 254,
                enabled: !_busy,
                keyboardType: TextInputType.emailAddress,
                decoration: const InputDecoration(labelText: 'Эмчийн имэйл'),
                validator: (v) {
                  final email = v?.trim() ?? '';
                  return !RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(email)
                      ? 'Зөв имэйл оруулна уу.'
                      : null;
                },
              ),
              if (_error != null)
                Text(_error!,
                    style: TextStyle(color: Theme.of(context).colorScheme.error)),
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
        onPressed: _busy ? null : _send,
        child: const Text('Урилга илгээх'),
      ),
    ],
  );
}
