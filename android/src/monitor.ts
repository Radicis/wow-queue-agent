import { ConnectionManager, Snapshot } from './connection';
import { formatEta, stateTitle } from './format';
import { showAlert, startForeground, stopForeground, updateForeground } from './notifications';
import { Pairing } from './protocol';

export const connection = new ConnectionManager(e => {
  showAlert(e.id, e.kind, e.message).catch(() => {});
});

let lastText = '';

function summarize(s: Snapshot): string {
  if (s.status === 'offline') {
    return 'Reconnecting to PC…';
  }
  if (s.status !== 'connected' || !s.state) {
    return 'Connecting…';
  }
  const st = s.state;
  if (st.state === 'QUEUE') {
    const pos = st.position !== undefined ? `position ${st.position}, ` : '';
    return `In queue: ${pos}~${formatEta(st.etaSeconds)} left`;
  }
  return stateTitle(st.state);
}

let unsubscribe: (() => void) | null = null;

export async function startMonitoring(pairing: Pairing) {
  await startForeground('Connecting…');
  lastText = 'Connecting…';
  unsubscribe?.();
  unsubscribe = connection.subscribe(() => {
    const text = summarize(connection.getSnapshot());
    if (text !== lastText) {
      lastText = text;
      updateForeground(text).catch(() => {});
    }
  });
  await connection.start(pairing);
}

export async function stopMonitoring() {
  unsubscribe?.();
  unsubscribe = null;
  connection.stop();
  await stopForeground();
}
