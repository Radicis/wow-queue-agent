import notifee, {
  AndroidForegroundServiceType,
  AndroidImportance,
} from '@notifee/react-native';
import type { EventKind } from './protocol';

const SERVICE_ID = 'monitor-service';
const MONITOR_CHANNEL = 'monitor';
const ALERT_CHANNEL = 'alerts';

let stopTask: (() => void) | null = null;

// Must be called once at app startup (index.js), outside any component.
export function registerForegroundTask() {
  notifee.registerForegroundService(
    () =>
      new Promise<void>(resolve => {
        stopTask = resolve;
      }),
  );
  notifee.onBackgroundEvent(async () => {});
}

async function ensureChannels() {
  await notifee.createChannel({
    id: MONITOR_CHANNEL,
    name: 'Monitoring status',
    importance: AndroidImportance.LOW,
  });
  await notifee.createChannel({
    id: ALERT_CHANNEL,
    name: 'Queue alerts',
    importance: AndroidImportance.HIGH,
    vibration: true,
  });
}

export async function requestNotificationPermission(): Promise<boolean> {
  const settings = await notifee.requestPermission();
  return settings.authorizationStatus >= 1;
}

export async function startForeground(text: string) {
  await ensureChannels();
  await updateForeground(text);
}

// Re-displaying the same id updates the ongoing notification in place.
export async function updateForeground(text: string) {
  await notifee.displayNotification({
    id: SERVICE_ID,
    title: 'WoW Queue Monitor',
    body: text,
    android: {
      channelId: MONITOR_CHANNEL,
      asForegroundService: true,
      foregroundServiceTypes: [
        AndroidForegroundServiceType.FOREGROUND_SERVICE_TYPE_DATA_SYNC,
      ],
      ongoing: true,
      onlyAlertOnce: true,
      pressAction: { id: 'default', launchActivity: 'default' },
    },
  });
}

export async function stopForeground() {
  try {
    await notifee.stopForegroundService();
  } finally {
    stopTask?.();
    stopTask = null;
  }
}

const TITLES: Record<EventKind, string> = {
  LOGGED_IN: '✅ Logged in!',
  DISCONNECTED: '⚠️ Disconnected',
  ERROR: '❌ Login error',
  NO_CLIENT: 'WoW client closed',
  QUEUE_SOON: '⏳ Almost there',
  AGENT_OFFLINE: '📡 PC agent unreachable',
};

export async function showAlert(id: string, kind: EventKind, message: string) {
  await ensureChannels();
  await notifee.displayNotification({
    id,
    title: TITLES[kind] ?? kind,
    body: message,
    android: {
      channelId: ALERT_CHANNEL,
      importance: AndroidImportance.HIGH,
      pressAction: { id: 'default', launchActivity: 'default' },
    },
  });
}
