import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexora_dental_mobile/live/models.dart';
import 'package:nexora_dental_mobile/live/staff_invitation_accept_screen.dart';

const _settings = ApiSettings(
  baseUrl: 'https://development.invalid',
  clinicId: 7,
);

void main() {
  testWidgets('Invitee accepts with personal password, no Director token',
      (tester) async {
    http.Request? sent;
    final client = MockClient((request) async {
      sent = request;
      return http.Response(jsonEncode({'message': 'Accepted'}), 200);
    });
    await tester.pumpWidget(MaterialApp(
      home: StaffInvitationAcceptScreen(
        settings: _settings,
        client: client,
      ),
    ));
    await tester.enterText(
        find.byType(TextFormField).at(0), 'Doctor@Example.invalid');
    await tester.enterText(
        find.byType(TextFormField).at(1), '12345678');
    await tester.enterText(
        find.byType(TextFormField).at(2), 'Personal-password-123456!');
    await tester.tap(find.widgetWithText(
        FilledButton, 'Бүртгэл баталгаажуулах'));
    await tester.pumpAndSettle();

    expect(sent!.method, 'POST');
    expect(sent!.url.path, '/api/staff-invitations/accept');
    expect(sent!.headers.containsKey('authorization'), isFalse);
    final data = jsonDecode(sent!.body);
    expect(data['clinicId'], 7);
    expect(data['email'], 'doctor@example.invalid');
    expect(data['code'], '12345678');
    expect(data['newPassword'], 'Personal-password-123456!');
    expect(data.containsKey('role'), isFalse);
    expect(find.textContaining('Бүртгэл амжилттай идэвхжлээ'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    client.close();
  });

  testWidgets('Invalid code cannot reach the API', (tester) async {
    var calls = 0;
    final client = MockClient((request) async {
      calls++;
      return http.Response('{}', 200);
    });
    await tester.pumpWidget(MaterialApp(
      home: StaffInvitationAcceptScreen(
        settings: _settings,
        client: client,
      ),
    ));
    await tester.enterText(
        find.byType(TextFormField).at(0), 'doctor@example.invalid');
    await tester.enterText(
        find.byType(TextFormField).at(1), '123');
    await tester.enterText(
        find.byType(TextFormField).at(2), 'Personal-password-123456!');
    await tester.tap(find.widgetWithText(
        FilledButton, 'Бүртгэл баталгаажуулах'));
    await tester.pumpAndSettle();
    expect(calls, 0);
    expect(find.textContaining('8 оронтой код оруулна уу'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    client.close();
  });
}
