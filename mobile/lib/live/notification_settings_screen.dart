import 'package:flutter/material.dart';

import 'live_flow_controller.dart';
import 'models.dart';

class NotificationSettingsScreen extends StatefulWidget {
  final LiveFlowController flow;
  const NotificationSettingsScreen({super.key, required this.flow});
  @override
  State<NotificationSettingsScreen> createState() =>
      _NotificationSettingsScreenState();
}

class _NotificationSettingsScreenState
    extends State<NotificationSettingsScreen> {
  late final AuthSession? originalSession;
  LiveFlowController get flow => widget.flow;
  @override
  void initState() {
    super.initState();
    originalSession = flow.session;
    flow.addListener(onChange);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && flow.session == originalSession) {
        flow.loadNotificationPreferences();
      }
    });
  }

  void onChange() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    flow.removeListener(onChange);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final validSession =
        originalSession != null && flow.session == originalSession;
    final preferences = validSession ? flow.notificationPreferences : null;
    final editable = validSession && preferences != null && !flow.busy;
    return Scaffold(
      appBar: AppBar(title: const Text('Мэдэгдлийн тохиргоо')),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 760),
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(20),
              child: !validSession
                  ? const Text(
                      'Нэвтрэх төлөв өөрчлөгдсөн. Буцаж дахин нэвтэрнэ үү.',
                    )
                  : Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const Text(
                          'Энэ эмнэлгийн мэдэгдэл авах зөвшөөрлөө сонгоно уу.',
                        ),
                        if (flow.busy) ...[
                          const LinearProgressIndicator(),
                          const Text('Тохиргоог боловсруулж байна…'),
                        ],
                        SwitchListTile(
                          title: const Text('Имэйл мэдэгдэл авах зөвшөөрөл'),
                          value: preferences != null && flow.emailConsent,
                          onChanged: editable
                              ? (v) => flow.changeNotificationConsent(email: v)
                              : null,
                        ),
                        SwitchListTile(
                          title: const Text('SMS мэдэгдэл авах зөвшөөрөл'),
                          value: preferences != null && flow.smsConsent,
                          onChanged: editable
                              ? (v) => flow.changeNotificationConsent(sms: v)
                              : null,
                        ),
                        if (preferences != null && !preferences.isEmailVerified)
                          const Text(
                            'Имэйл баталгаажаагүй тул имэйл мэдэгдэл илгээгдэхгүй.',
                          ),
                        if (preferences != null && !preferences.isPhoneVerified)
                          const Text(
                            'Утасны дугаар баталгаажаагүй тул SMS илгээгдэхгүй. Утас баталгаажуулах үйлдэл одоогоор аппад байхгүй.',
                          ),
                        const Padding(
                          padding: EdgeInsets.symmetric(vertical: 16),
                          child: Text(
                            'Зөвшөөрөл нь холбоо барих мэдээллийг баталгаажуулахгүй. Мэдэгдэл илгээхэд баталгаажсан хаяг, дугаар болон ажиллаж буй илгээх үйлчилгээ шаардлагатай.',
                          ),
                        ),
                        if (flow.notice != null)
                          Semantics(
                            liveRegion: true,
                            child: Text(flow.notice!),
                          ),
                        if (flow.error != null)
                          Semantics(
                            liveRegion: true,
                            child: Text(
                              flow.error!,
                              style: TextStyle(
                                color: Theme.of(context).colorScheme.error,
                              ),
                            ),
                          ),
                        FilledButton(
                          onPressed: editable
                              ? flow.saveNotificationPreferences
                              : null,
                          child: const Text('Тохиргоо хадгалах'),
                        ),
                        OutlinedButton(
                          onPressed: flow.busy
                              ? null
                              : flow.loadNotificationPreferences,
                          child: const Text('Тохиргоог шинэчлэх'),
                        ),
                      ],
                    ),
            ),
          ),
        ),
      ),
    );
  }
}
