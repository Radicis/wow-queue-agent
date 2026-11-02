import type { WowState } from './protocol';

export function formatEta(seconds?: number): string {
  if (seconds === undefined) {
    return 'unknown';
  }
  if (seconds < 90) {
    return '< 1 min';
  }
  const totalMin = Math.round(seconds / 60);
  if (totalMin < 60) {
    return `${totalMin} min`;
  }
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return `${h}h ${String(m).padStart(2, '0')}m`;
}

export function formatAge(fromIso: string | undefined, now: number): string {
  if (!fromIso) {
    return '';
  }
  const s = Math.max(0, Math.round((now - Date.parse(fromIso)) / 1000));
  if (s < 60) {
    return `${s}s ago`;
  }
  if (s < 3600) {
    return `${Math.floor(s / 60)}m ago`;
  }
  return `${Math.floor(s / 3600)}h ago`;
}

export function stateTitle(state: WowState): string {
  switch (state) {
    case 'LOGGED_IN':
      return 'Logged in';
    case 'QUEUE':
      return 'In queue';
    case 'DISCONNECTED':
      return 'Disconnected';
    case 'ERROR':
      return 'Login error';
    case 'NO_CLIENT':
      return 'WoW not running';
    default:
      return 'Waiting for data';
  }
}

export function stateColor(state: WowState): string {
  switch (state) {
    case 'LOGGED_IN':
      return '#2e9d4f';
    case 'QUEUE':
      return '#d98e04';
    case 'DISCONNECTED':
    case 'ERROR':
      return '#c0392b';
    default:
      return '#6b7280';
  }
}
