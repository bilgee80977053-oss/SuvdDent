import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental_mobile/app/demo_flow_controller.dart';

void reachServices(DemoFlowController flow) {
  flow.selectClinic(101);
  for (var i = 0; i < 4; i++) {
    expect(flow.continueFlow(), isTrue);
  }
  expect(flow.step, DemoStep.service);
}

void main() {
  test(
    'Walkthrough requires clinic, simulated verification and profile setup',
    () {
      final flow = DemoFlowController();
      addTearDown(flow.dispose);
      expect(flow.continueFlow(), isFalse);
      flow.selectClinic(101);
      flow.continueFlow(); // Authentication placeholder.
      flow.continueFlow(); // Verification placeholder.
      expect(flow.step, DemoStep.verification);
      expect(flow.demoEmailVerified, isFalse);
      expect(flow.demoProfileReady, isFalse);
      expect(flow.complete, isFalse);
      flow.continueFlow(); // Explicit simulated verification.
      expect(flow.demoEmailVerified, isTrue);
      expect(flow.demoProfileReady, isFalse);
      flow.continueFlow(); // Explicit profile placeholder.
      expect(flow.demoProfileReady, isTrue);
      expect(flow.continueFlow(), isFalse); // A service is still required.
    },
  );

  test(
    'Changing clinic clears simulated identity and every downstream selection',
    () {
      final flow = DemoFlowController();
      addTearDown(flow.dispose);
      reachServices(flow);
      flow.selectService(51);
      flow.continueFlow();
      flow.selectDoctor(31);
      flow.continueFlow();
      flow.selectSlot(1);
      flow.continueFlow();
      while (flow.step != DemoStep.clinic) {
        flow.back();
      }
      flow.selectClinic(202);
      expect(flow.demoEmailVerified, isFalse);
      expect(flow.demoProfileReady, isFalse);
      expect(flow.service, isNull);
      expect(flow.doctor, isNull);
      expect(flow.slot, isNull);
      expect(flow.complete, isFalse);
      expect(flow.services.map((s) => s.id), [61, 62]);
      flow.continueFlow();
      flow.continueFlow();
      expect(flow.step, DemoStep.verification);
      expect(flow.demoEmailVerified, isFalse);
    },
  );

  test('Foreign clinic choices are rejected and changing service invalidates doctor/slot', () {
    final flow = DemoFlowController();
    addTearDown(flow.dispose);
    reachServices(flow);
    expect(() => flow.selectService(61), throwsStateError);
    expect(flow.service, isNull);
    flow.selectService(52);
    flow.continueFlow();
    expect(flow.doctors.map((d) => d.id), [31]);
    expect(() => flow.selectDoctor(32), throwsStateError);
    flow.selectDoctor(31);
    flow.continueFlow();
    flow.selectSlot(1);
    expect(flow.slot!.startUtc.isUtc, isTrue);
    expect(flow.slot!.label(flow.clinic!), '09:00');
    flow.back();
    flow.back();
    flow.selectService(51);
    expect(flow.doctor, isNull);
    expect(flow.slot, isNull);
    expect(flow.doctors.map((d) => d.id), [31, 33]);
  });
}
