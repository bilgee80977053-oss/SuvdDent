import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'models.dart';

abstract interface class KeyValueStore {
  Future<String?> read(String key);
  Future<void> write(String key, String value);
  Future<void> delete(String key);
}

class DeviceSecureStore implements KeyValueStore {
  final FlutterSecureStorage storage;
  const DeviceSecureStore([this.storage = const FlutterSecureStorage()]);
  @override
  Future<String?> read(String key) => storage.read(key: key);
  @override
  Future<void> write(String key, String value) =>
      storage.write(key: key, value: value);
  @override
  Future<void> delete(String key) => storage.delete(key: key);
}

class StoredSession {
  final String token;
  final int accountId;
  const StoredSession(this.token, this.accountId);
}

class SessionStore {
  final KeyValueStore values;
  final String prefix;
  SessionStore(this.values, Uri origin)
    : prefix = 'nexora.${base64Url.encode(utf8.encode(origin.origin))}';
  Future<StoredSession?> session() async {
    final raw = await values.read('$prefix.session');
    if (raw == null) {
      return null;
    }
    try {
      final j = jsonObject(jsonDecode(raw));
      final token = j['token'] as String;
      final id = j['accountId'] as int;
      if (token.isEmpty || id <= 0) {
        throw const FormatException();
      }
      return StoredSession(token, id);
    } on FormatException {
      await clearSession();
      return null;
    } on TypeError {
      await clearSession();
      return null;
    }
  }

  Future<void> saveSession(String token, int id) => values.write(
    '$prefix.session',
    jsonEncode({'token': token, 'accountId': id}),
  );
  Future<void> clearSession() => values.delete('$prefix.session');
  Future<Json?> pending(int account) async {
    final raw = await values.read('$prefix.pending.$account');
    return raw == null ? null : jsonObject(jsonDecode(raw));
  }

  Future<void> savePending(int account, Json operation) =>
      values.write('$prefix.pending.$account', jsonEncode(operation));
  Future<void> clearPending(int account) =>
      values.delete('$prefix.pending.$account');
}
