import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../app/widgets.dart';
import 'session.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});
  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}
class _LoginScreenState extends ConsumerState<LoginScreen> {
  final form = GlobalKey<FormState>();
  final identifier = TextEditingController(), password = TextEditingController();
  bool busy = false, obscure = true;
  String? error;
  Future<void> submit() async {
    if (busy || !form.currentState!.validate()) { return; }
    setState(() { busy = true; error = null; });
    try { await ref.read(sessionProvider.notifier).login(identifier.text, password.text); }
    catch (e) { if (mounted) { setState(() => error = safeMessage(e)); } }
    finally { password.clear(); if (mounted) { setState(() => busy = false); } }
  }
  @override
  void dispose() { identifier.dispose(); password.dispose(); super.dispose(); }
  @override
  Widget build(BuildContext context) => AppPage(title: 'Nexora Dental', child: SingleChildScrollView(
    padding: const EdgeInsets.all(24), child: Form(key: form, child: Column(crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [const SizedBox(height: 28), Icon(Icons.health_and_safety_outlined, size: 72, color: Theme.of(context).colorScheme.primary),
        const SizedBox(height: 20), Text('Nexora Dental-д тавтай морил', textAlign: TextAlign.center, style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 8), const Text('Бүртгэлтэй имэйл эсвэл утас, нууц үгээрээ нэвтэрнэ үү.', textAlign: TextAlign.center),
        const SizedBox(height: 32),
        TextFormField(key: const Key('login.identifier'), controller: identifier, enabled: !busy,
          keyboardType: TextInputType.emailAddress, textInputAction: TextInputAction.next,
          decoration: const InputDecoration(labelText: 'Имэйл эсвэл утас', prefixIcon: Icon(Icons.person_outline)),
          validator: (v) => v == null || v.trim().isEmpty ? 'Нэвтрэх нэрээ оруулна уу.' : null),
        const SizedBox(height: 16),
        TextFormField(key: const Key('login.password'), controller: password, enabled: !busy, obscureText: obscure,
          autocorrect: false, enableSuggestions: false, textInputAction: TextInputAction.done, onFieldSubmitted: (_) => submit(),
          decoration: InputDecoration(labelText: 'Нууц үг', prefixIcon: const Icon(Icons.lock_outline),
            suffixIcon: IconButton(tooltip: obscure ? 'Нууц үг харуулах' : 'Нууц үг нуух', onPressed: () => setState(() => obscure = !obscure), icon: Icon(obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined))),
          validator: (v) => v == null || v.isEmpty ? 'Нууц үгээ оруулна уу.' : null),
        if (error != null) Padding(padding: const EdgeInsets.only(top: 16), child: Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
        const SizedBox(height: 24), FilledButton(key: const Key('login.submit'), onPressed: busy ? null : submit,
          child: Padding(padding: const EdgeInsets.all(12), child: busy ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Нэвтрэх'))),
      ]))));
}
class StartupScreen extends ConsumerWidget {
  const StartupScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(sessionProvider);
    return AppPage(title: 'Nexora Dental', child: session.hasError
      ? ErrorPanel(error: session.error, retry: () => ref.invalidate(sessionProvider))
      : const Center(child: CircularProgressIndicator()));
  }
}
