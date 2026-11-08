import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/models.dart';
import '../../core/secure_store.dart';

final apiUrlProvider = Provider<Uri>(
  (ref) => validateBaseUrl(const String.fromEnvironment('API_BASE_URL')),
);
final storeProvider = Provider<SessionStore>(
  (ref) => SessionStore(const DeviceSecureStore(), ref.watch(apiUrlProvider)),
);
final apiProvider = Provider<NexoraApi>(
  (ref) => ApiClient(ref.watch(apiUrlProvider)),
);
final sessionProvider = AsyncNotifierProvider<SessionController, Account?>(
  SessionController.new,
);

class SessionController extends AsyncNotifier<Account?> {
  @override
  Future<Account?> build() async {
    final api = ref.read(apiProvider);
    final store = ref.read(storeProvider);
    api.onUnauthorized = expire;
    ref.onDispose(() => api.onUnauthorized = null);
    final saved = await store.session();
    api.token = saved?.token;
    if (saved == null) {
      return null;
    }
    try {
      final account = await api.me();
      if (account.id != saved.accountId) {
        await store.clearSession();
        api.token = null;
        return null;
      }
      return account;
    } on ApiFailure catch (e) {
      if (e.status == 401) {
        await store.clearSession();
        api.token = null;
        return null;
      }
      rethrow; // Keep the stored session for an explicit retry after a network outage.
    }
  }

  Future<void> login(String identifier, String password) async {
    final api = ref.read(apiProvider);
    final result = await api.login(identifier.trim(), password);
    try {
      await ref
          .read(storeProvider)
          .saveSession(result.token, result.account.id);
    } catch (_) {
      api.token = null;
      throw const ApiFailure('STORAGE_ERROR');
    }
    api.token = result.token;
    state = AsyncData(result.account);
  }

  Future<void> logout() async {
    try {
      await ref.read(storeProvider).clearSession();
    } catch (_) {
      throw const ApiFailure('STORAGE_ERROR');
    }
    ref.read(apiProvider).token = null;
    state = const AsyncData(null);
  }

  Future<void> expire() async {
    ref.read(apiProvider).token = null;
    try {
      await ref.read(storeProvider).clearSession();
    } catch (_) {
      /* Revoked JWT cannot authorize requests. */
    }
    if (ref.mounted) {
      state = const AsyncData(null);
    }
  }
}
