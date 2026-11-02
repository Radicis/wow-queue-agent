import React, { useEffect, useState, useSyncExternalStore } from 'react';
import {
  Alert,
  Linking,
  Pressable,
  SafeAreaView,
  ScrollView,
  StatusBar,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import {
  Camera,
  useCameraDevice,
  useCodeScanner,
} from 'react-native-vision-camera';
import { connection, startMonitoring, stopMonitoring } from './src/monitor';
import { requestNotificationPermission } from './src/notifications';
import { formatAge, formatEta, stateColor, stateTitle } from './src/format';
import { Pairing, parsePairing } from './src/protocol';
import { loadPairing, savePairing } from './src/storage';

export default function App() {
  const [pairing, setPairing] = useState<Pairing | null>(null);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    (async () => {
      await requestNotificationPermission();
      const p = await loadPairing();
      setPairing(p);
      setLoaded(true);
      if (p) {
        startMonitoring(p).catch(() => {});
      }
    })();
  }, []);

  const onPaired = async (p: Pairing) => {
    await savePairing(p);
    setPairing(p);
    await startMonitoring(p);
  };

  const onUnpair = async () => {
    await stopMonitoring();
    await savePairing(null);
    setPairing(null);
  };

  return (
    <SafeAreaView style={styles.root}>
      <StatusBar barStyle="light-content" />
      {!loaded ? null : pairing ? (
        <StatusScreen pairing={pairing} onUnpair={onUnpair} />
      ) : (
        <SetupScreen onPaired={onPaired} />
      )}
    </SafeAreaView>
  );
}

function StatusScreen({ pairing, onUnpair }: { pairing: Pairing; onUnpair: () => void }) {
  const snap = useSyncExternalStore(connection.subscribe, connection.getSnapshot);
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  const st = snap.state;
  const live = snap.status === 'connected' && st;
  const state = live ? st.state : 'UNKNOWN';
  const color = live ? stateColor(state) : '#6b7280';

  return (
    <ScrollView contentContainerStyle={styles.center}>
      <Text style={styles.link}>
        {snap.status === 'connected'
          ? `● Connected to ${snap.agent ?? pairing.name ?? 'PC'}`
          : snap.status === 'offline'
          ? '○ PC unreachable, retrying…'
          : '○ Connecting…'}
      </Text>

      <View style={[styles.card, { borderColor: color }]}>
        <Text style={[styles.stateTitle, { color }]}>
          {live ? stateTitle(state) : 'No data'}
        </Text>

        {live && st.state === 'QUEUE' && (
          <>
            <Text style={styles.big}>{formatEta(st.etaSeconds)}</Text>
            <Text style={styles.sub}>estimated time remaining</Text>
            {st.position !== undefined && (
              <Text style={styles.sub}>Position in queue: {st.position}</Text>
            )}
            {st.soon && <Text style={styles.soon}>Almost there!</Text>}
            <Text style={styles.dim}>Estimate updated {formatAge(st.etaUpdatedAt, now)}</Text>
          </>
        )}
        {live && st.state !== 'QUEUE' && (
          <Text style={styles.dim}>since {formatAge(st.since, now)}</Text>
        )}
      </View>

      <Text style={styles.dim}>
        You'll get a notification when you log in, disconnect, hit an error, or the queue is under
        10 minutes.
      </Text>

      <Pressable
        style={styles.secondary}
        onPress={() => Linking.sendIntent('android.settings.IGNORE_BATTERY_OPTIMIZATION_SETTINGS')}>
        <Text style={styles.secondaryText}>Allow background running (battery)</Text>
      </Pressable>
      <Pressable
        style={styles.secondary}
        onPress={() =>
          Alert.alert('Unpair?', 'Stops monitoring and forgets this PC.', [
            { text: 'Cancel', style: 'cancel' },
            { text: 'Unpair', style: 'destructive', onPress: onUnpair },
          ])
        }>
        <Text style={styles.secondaryText}>Unpair</Text>
      </Pressable>
    </ScrollView>
  );
}

function SetupScreen({ onPaired }: { onPaired: (p: Pairing) => void }) {
  const [scanning, setScanning] = useState(false);
  const [host, setHost] = useState('');
  const [port, setPort] = useState('47821');

  const scan = async () => {
    const status = await Camera.requestCameraPermission();
    if (status === 'granted') {
      setScanning(true);
    } else {
      Alert.alert('Camera permission needed', 'Allow camera access to scan the QR code.');
    }
  };

  const manual = () => {
    const p = Number(port);
    if (!host.trim() || !Number.isInteger(p)) {
      Alert.alert('Missing details', 'Enter the PC IP address and port.');
      return;
    }
    onPaired({ hosts: [host.trim()], port: p });
  };

  if (scanning) {
    return (
      <Scanner
        onCancel={() => setScanning(false)}
        onScanned={p => {
          setScanning(false);
          onPaired(p);
        }}
      />
    );
  }

  return (
    <ScrollView contentContainerStyle={styles.center}>
      <Text style={styles.stateTitle}>Pair with your PC</Text>
      <Text style={styles.dim}>
        Run WowQueueAgent.exe on the PC (same Wi‑Fi), double-click its tray icon, and scan the QR
        code.
      </Text>
      <Pressable style={styles.primary} onPress={scan}>
        <Text style={styles.primaryText}>Scan QR code</Text>
      </Pressable>

      <Text style={[styles.dim, { marginTop: 24 }]}>Or enter manually</Text>
      <TextInput
        style={styles.input}
        placeholder="PC IP address, e.g. 192.168.1.20"
        placeholderTextColor="#6b7280"
        value={host}
        onChangeText={setHost}
        autoCapitalize="none"
        keyboardType="numbers-and-punctuation"
      />
      <TextInput
        style={styles.input}
        placeholder="Port"
        placeholderTextColor="#6b7280"
        value={port}
        onChangeText={setPort}
        keyboardType="number-pad"
      />
      <Pressable style={styles.secondary} onPress={manual}>
        <Text style={styles.secondaryText}>Connect</Text>
      </Pressable>
    </ScrollView>
  );
}

function Scanner({
  onScanned,
  onCancel,
}: {
  onScanned: (p: Pairing) => void;
  onCancel: () => void;
}) {
  const device = useCameraDevice('back');
  const [handled, setHandled] = useState(false);
  const scanner = useCodeScanner({
    codeTypes: ['qr'],
    onCodeScanned: codes => {
      if (handled) {
        return;
      }
      const p = codes[0]?.value ? parsePairing(codes[0].value) : null;
      if (p) {
        setHandled(true);
        onScanned(p);
      }
    },
  });

  return (
    <View style={styles.root}>
      {device ? (
        <Camera style={StyleSheet.absoluteFill} device={device} isActive codeScanner={scanner} />
      ) : (
        <Text style={styles.dim}>No camera available</Text>
      )}
      <Pressable style={[styles.secondary, styles.cancel]} onPress={onCancel}>
        <Text style={styles.secondaryText}>Cancel</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: '#111827' },
  center: { padding: 20, alignItems: 'stretch', gap: 14 },
  link: { color: '#9ca3af', textAlign: 'center' },
  card: {
    borderWidth: 2,
    borderRadius: 16,
    padding: 24,
    alignItems: 'center',
    backgroundColor: '#1f2937',
    gap: 6,
  },
  stateTitle: { color: '#f9fafb', fontSize: 26, fontWeight: '700', textAlign: 'center' },
  big: { color: '#f9fafb', fontSize: 64, fontWeight: '800' },
  sub: { color: '#d1d5db', fontSize: 16 },
  soon: { color: '#fbbf24', fontSize: 18, fontWeight: '700', marginTop: 6 },
  dim: { color: '#9ca3af', textAlign: 'center' },
  primary: { backgroundColor: '#2563eb', padding: 16, borderRadius: 12, alignItems: 'center' },
  primaryText: { color: 'white', fontWeight: '700', fontSize: 16 },
  secondary: { backgroundColor: '#374151', padding: 14, borderRadius: 12, alignItems: 'center' },
  secondaryText: { color: '#e5e7eb', fontWeight: '600' },
  input: { backgroundColor: '#1f2937', color: 'white', borderRadius: 10, padding: 12 },
  cancel: { position: 'absolute', bottom: 32, left: 24, right: 24 },
});
