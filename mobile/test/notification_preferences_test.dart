import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:nexora_dental_mobile/live/live_flow_controller.dart';
import 'package:nexora_dental_mobile/live/live_patient_flow.dart';
import 'package:nexora_dental_mobile/live/notification_settings_screen.dart';

import 'live_api_test.dart' as fixture;

const path = '/api/notification-preferences/me';
Map<String, dynamic> preferences({
  bool email = false,
  bool sms = false,
  bool phoneVerified = false,
}) => {
  'emailEnabled': email,
  'smsEnabled': sms,
  'isEmailVerified': true,
  'isPhoneVerified': phoneVerified,
};
Future<LiveFlowController> controller(
  FutureOr<http.Response> Function(http.Request) handler,
) async {
  final flow = LiveFlowController(fixture.makeApi(handler));
  await flow.login('synthetic@example.invalid', 'synthetic-password');
  return flow;
}

void main() {
  test('GET loads consent and verification separately; PUT only sends two consent flags', () async {
    var saved = preferences();
    final flow = await controller((r) {
      if (r.url.path == path) {
        expect(r.url.queryParameters, isEmpty);
        expect(r.headers['Authorization'], 'Bearer synthetic-test-token');
        if (r.method == 'PUT') {
          expect(jsonDecode(r.body), {
            'emailEnabled': true,
            'smsEnabled': true,
          });
          saved = preferences(email: true, sms: true);
        }
        return fixture.response(saved);
      }
      return fixture.standard(r);
    });
    addTearDown(flow.dispose);
    await flow.loadNotificationPreferences();
    expect(flow.emailConsent, isFalse);
    expect(flow.smsConsent, isFalse);
    flow.changeNotificationConsent(email: true, sms: true);
    await flow.saveNotificationPreferences();
    expect(flow.notice, 'Мэдэгдлийн тохиргоо хадгалагдлаа.');
    expect(flow.notificationPreferences!.isPhoneVerified, isFalse);
    expect(flow.smsConsent, isTrue);
    flow.changeNotificationConsent(email: false);
    await flow.loadNotificationPreferences();
    expect(flow.emailConsent, isTrue);
  });

  test(
    'failed initial load leaves switches unsaveable and permits refresh',
    () async {
      var fail = true, writes = 0;
      final flow = await controller((r) {
        if (r.url.path == path) {
          if (r.method == 'PUT') writes++;
          if (fail) throw http.ClientException('synthetic');
          return fixture.response(preferences());
        }
        return fixture.standard(r);
      });
      addTearDown(flow.dispose);
      await flow.loadNotificationPreferences();
      expect(flow.notificationPreferences, isNull);
      expect(flow.error, isNotNull);
      flow.changeNotificationConsent(email: true);
      await flow.saveNotificationPreferences();
      expect(writes, 0);
      fail = false;
      await flow.loadNotificationPreferences();
      expect(flow.notificationPreferences, isNotNull);
      expect(flow.error, isNull);
    },
  );

  test(
    'uncertain save reports no success and GET reconciles saved settings',
    () async {
      var saved = preferences(), puts = 0;
      final flow = await controller((r) {
        if (r.url.path == path) {
          if (r.method == 'PUT') {
            puts++;
            saved = preferences(email: true);
            throw http.ClientException('synthetic-lost-response');
          }
          return fixture.response(saved);
        }
        return fixture.standard(r);
      });
      addTearDown(flow.dispose);
      await flow.loadNotificationPreferences();
      flow.changeNotificationConsent(email: true);
      await flow.saveNotificationPreferences();
      expect(flow.notice, isNull);
      expect(flow.error, contains('Хадгалагдсан эсэх тодорхойгүй'));
      await flow.loadNotificationPreferences();
      expect(flow.emailConsent, isTrue);
      expect(puts, 1);
    },
  );

  test(
    'logout and new session discard stale preference load and clear drafts',
    () async {
      final pending = Completer<http.Response>();
      final entered = Completer<void>();
      final flow = await controller((r) {
        if (r.url.path == path) {
          entered.complete();
          return pending.future;
        }
        return fixture.standard(r);
      });
      addTearDown(flow.dispose);
      final loading = flow.loadNotificationPreferences();
      await entered.future;
      flow.logout();
      await flow.login('other@example.invalid', 'synthetic-password');
      pending.complete(fixture.response(preferences(email: true, sms: true)));
      await loading;
      expect(flow.notificationPreferences, isNull);
      expect(flow.emailConsent, isFalse);
      expect(flow.smsConsent, isFalse);
      expect(flow.notice, isNot('Мэдэгдлийн тохиргоо хадгалагдлаа.'));
    },
  );

  test('logout discards stale save acknowledgement', () async {
    final pending = Completer<http.Response>();
    final entered = Completer<void>();
    final flow = await controller((r) {
      if (r.url.path == path) {
        if (r.method == 'PUT') {
          entered.complete();
          return pending.future;
        }
        return fixture.response(preferences());
      }
      return fixture.standard(r);
    });
    addTearDown(flow.dispose);
    await flow.loadNotificationPreferences();
    flow.changeNotificationConsent(email: true);
    final saving = flow.saveNotificationPreferences();
    await entered.future;
    flow.logout();
    pending.complete(fixture.response(preferences(email: true)));
    await saving;
    expect(flow.notificationPreferences, isNull);
    expect(flow.notice, isNull);
    expect(flow.emailConsent, isFalse);
  });

  test('401 during refresh clears all consent data', () async {
    var expired = false;
    final flow = await controller((r) {
      if (r.url.path == path) {
        return fixture.response(
          expired ? {} : preferences(email: true),
          expired ? 401 : 200,
        );
      }
      return fixture.standard(r);
    });
    addTearDown(flow.dispose);
    await flow.loadNotificationPreferences();
    expired = true;
    await flow.loadNotificationPreferences();
    expect(flow.session, isNull);
    expect(flow.notificationPreferences, isNull);
    expect(flow.emailConsent, isFalse);
    expect(flow.smsConsent, isFalse);
  });

  testWidgets(
    'settings load, switches, save confirmation, SMS warning and refresh',
    (tester) async {
      var saved = preferences(), gets = 0, puts = 0;
      final flow = await controller((r) {
        if (r.url.path == path) {
          if (r.method == 'PUT') {
            puts++;
            final j = jsonDecode(r.body);
            saved = preferences(
              email: j['emailEnabled'] as bool,
              sms: j['smsEnabled'] as bool,
            );
          } else {
            gets++;
          }
          return fixture.response(saved);
        }
        return fixture.standard(r);
      });
      addTearDown(flow.dispose);
      await tester.pumpWidget(
        MaterialApp(home: NotificationSettingsScreen(flow: flow)),
      );
      await tester.pumpAndSettle();
      expect(gets, 1);
      expect(
        find.textContaining(
          'Утасны дугаар баталгаажаагүй тул SMS илгээгдэхгүй',
        ),
        findsOneWidget,
      );
      await tester.tap(find.byType(SwitchListTile).at(0));
      await tester.pumpAndSettle();
      await tester.tap(find.byType(SwitchListTile).at(1));
      await tester.pumpAndSettle();
      final save = find.widgetWithText(FilledButton, 'Тохиргоо хадгалах');
      await tester.ensureVisible(save);
      await tester.pumpAndSettle();
      await tester.tap(save);
      await tester.pumpAndSettle();
      expect(puts, 1);
      expect(find.text('Мэдэгдлийн тохиргоо хадгалагдлаа.'), findsOneWidget);
      expect(flow.notificationPreferences!.isPhoneVerified, isFalse);
      saved = preferences();
      final refresh = find.widgetWithText(OutlinedButton, 'Тохиргоог шинэчлэх');
      await tester.ensureVisible(refresh);
      await tester.pumpAndSettle();
      await tester.tap(refresh);
      await tester.pumpAndSettle();
      expect(gets, 2);
      expect(flow.emailConsent, isFalse);
      expect(flow.smsConsent, isFalse);
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets('loading disables switches and save, errors permit refresh', (
    tester,
  ) async {
    final pending = Completer<http.Response>();
    final flow = await controller((r) {
      if (r.url.path == path) return pending.future;
      return fixture.standard(r);
    });
    addTearDown(flow.dispose);
    await tester.pumpWidget(
      MaterialApp(home: NotificationSettingsScreen(flow: flow)),
    );
    await tester.pump();
    await tester.pump();
    expect(find.text('Тохиргоог боловсруулж байна…'), findsOneWidget);
    expect(
      tester
          .widget<SwitchListTile>(find.byType(SwitchListTile).first)
          .onChanged,
      isNull,
    );
    pending.complete(fixture.response({}, 500));
    await tester.pumpAndSettle();
    expect(flow.error, isNotNull);
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
    expect(
      tester.widget<OutlinedButton>(find.byType(OutlinedButton)).onPressed,
      isNotNull,
    );
    flow.logout();
    await tester.pumpAndSettle();
    expect(find.byType(SwitchListTile), findsNothing);
    expect(find.textContaining('Нэвтрэх төлөв өөрчлөгдсөн'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets(
    'LIVE profile and toolbar open settings and back preserves booking stage',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: LivePatientFlow(
            settings: fixture.settings,
            api: fixture.makeApi(
              (r) => r.url.path == path
                  ? fixture.response(preferences())
                  : fixture.standard(r),
            ),
          ),
        ),
      );
      Future<void> press(String label) async {
        final target = find.widgetWithText(FilledButton, label);
        await tester.ensureVisible(target);
        await tester.pumpAndSettle();
        await tester.tap(target);
        await tester.pumpAndSettle();
      }

      await press('Энэ эмнэлгээр үргэлжлүүлэх');
      await tester.enterText(
        find.byType(TextFormField).at(0),
        'synthetic@example.invalid',
      );
      await tester.enterText(
        find.byType(TextFormField).at(1),
        'synthetic-password',
      );
      await press('Нэвтрэх');
      await press('Миний профайлыг авах');
      final profileSettings = find.widgetWithText(
        FilledButton,
        'Мэдэгдлийн тохиргоо',
      );
      await tester.ensureVisible(profileSettings);
      await tester.pumpAndSettle();
      await tester.tap(profileSettings);
      await tester.pumpAndSettle();
      expect(find.byType(NotificationSettingsScreen), findsOneWidget);
      await tester.pageBack();
      await tester.pumpAndSettle();
      expect(find.text('Өвчтөний профайл'), findsOneWidget);
      await tester.tap(find.byTooltip('Мэдэгдлийн тохиргоо'));
      await tester.pumpAndSettle();
      expect(find.byType(NotificationSettingsScreen), findsOneWidget);
      await tester.pageBack();
      await tester.pumpAndSettle();
      await tester.pumpWidget(const SizedBox());
    },
  );
}
