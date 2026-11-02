import Zeroconf from 'react-native-zeroconf';
import {
  EventMessage,
  Pairing,
  StateMessage,
  parseMessage,
} from './protocol';
import { loadSeenEvents, saveSeenEvents, savePairing } from './storage';

export type LinkStatus = 'idle' | 'connecting' | 'connected' | 'offline';

export interface Snapshot {
  status: LinkStatus;
  state?: StateMessage;
  lastMessageAt?: number;
  agent?: string;
  host?: string;
}

const STALE_MS = 10_000;
const OFFLINE_ALERT_MS = 30_000;
const WATCHDOG_MS = 3_000;
const MAX_BACKOFF_MS = 15_000;

export class ConnectionManager {
  private snap: Snapshot = { status: 'idle' };
  private listeners = new Set<() => void>();
  private pairing: Pairing | null = null;
  private ws: WebSocket | null = null;
  private hostIndex = 0;
  private failures = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private watchdog: ReturnType<typeof setInterval> | null = null;
  private seen = new Set<string>();
  private everConnected = false;
  private offlineSince: number | null = null;
  private offlineAlerted = false;
  private scanning = false;

  constructor(private onEvent: (e: Pick<EventMessage, 'id' | 'kind' | 'message' | 'at'>) => void) {}

  getSnapshot = (): Snapshot => this.snap;

  subscribe = (cb: () => void) => {
    this.listeners.add(cb);
    return () => {
      this.listeners.delete(cb);
    };
  };

  private set(patch: Partial<Snapshot>) {
    this.snap = { ...this.snap, ...patch };
    this.listeners.forEach(l => l());
  }

  async start(pairing: Pairing) {
    this.stop();
    this.pairing = pairing;
    this.hostIndex = 0;
    this.failures = 0;
    this.everConnected = false;
    this.seen = new Set(await loadSeenEvents());
    this.set({ status: 'connecting', state: undefined, lastMessageAt: undefined });
    this.watchdog = setInterval(() => this.checkHealth(), WATCHDOG_MS);
    this.open();
  }

  stop() {
    this.pairing = null;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    if (this.watchdog) {
      clearInterval(this.watchdog);
      this.watchdog = null;
    }
    const ws = this.ws;
    this.ws = null;
    if (ws) {
      ws.onclose = null;
      ws.close();
    }
    this.set({ status: 'idle', state: undefined, host: undefined });
  }

  private open() {
    const p = this.pairing;
    if (!p) {
      return;
    }
    const host = p.hosts[this.hostIndex % p.hosts.length];
    const ws = new WebSocket(`ws://${host}:${p.port}/ws`);
    this.ws = ws;
    if (this.snap.status !== 'offline') {
      this.set({ status: 'connecting', host });
    }

    ws.onopen = () => {
      this.failures = 0;
      this.everConnected = true;
      this.offlineSince = null;
      this.offlineAlerted = false;
      this.set({ status: 'connected', host, lastMessageAt: Date.now() });
    };
    ws.onmessage = ev => {
      const msg = parseMessage(String(ev.data));
      if (!msg) {
        return;
      }
      this.set({ lastMessageAt: Date.now() });
      if (msg.type === 'hello') {
        this.set({ agent: msg.agent });
      } else if (msg.type === 'state') {
        this.set({ state: msg });
      } else {
        this.handleEvent(msg);
      }
    };
    ws.onerror = () => {};
    ws.onclose = () => {
      if (this.ws !== ws) {
        return;
      }
      this.ws = null;
      this.markOffline();
      this.scheduleReconnect();
    };
  }

  private scheduleReconnect() {
    if (!this.pairing || this.reconnectTimer) {
      return;
    }
    this.failures++;
    this.hostIndex++;
    // After a full round of failures the PC's IP may have changed; look it up via mDNS.
    if (this.failures % (this.pairing.hosts.length * 2) === 0) {
      this.rediscover();
    }
    const delay = Math.min(1000 * 2 ** Math.min(this.failures, 4), MAX_BACKOFF_MS);
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      this.open();
    }, delay);
  }

  private markOffline() {
    if (this.snap.status !== 'idle') {
      this.set({ status: 'offline' });
    }
    if (this.everConnected && this.offlineSince === null) {
      this.offlineSince = Date.now();
    }
  }

  private checkHealth() {
    const { status, lastMessageAt } = this.snap;
    if (status === 'connected' && lastMessageAt && Date.now() - lastMessageAt > STALE_MS) {
      // Half-open socket: the agent stopped sending heartbeats.
      this.ws?.close();
    }
    if (
      this.offlineSince !== null &&
      !this.offlineAlerted &&
      Date.now() - this.offlineSince > OFFLINE_ALERT_MS
    ) {
      this.offlineAlerted = true;
      this.onEvent({
        id: `offline:${this.offlineSince}`,
        kind: 'AGENT_OFFLINE',
        message: 'Lost contact with the PC agent',
        at: new Date().toISOString(),
      });
    }
  }

  private handleEvent(e: EventMessage) {
    if (this.seen.has(e.id)) {
      return;
    }
    this.seen.add(e.id);
    saveSeenEvents([...this.seen]).catch(() => {});
    this.onEvent(e);
  }

  private rediscover() {
    if (this.scanning || !this.pairing) {
      return;
    }
    this.scanning = true;
    const zc = new Zeroconf();
    const done = () => {
      this.scanning = false;
      zc.stop();
      zc.removeDeviceListeners();
    };
    const timer = setTimeout(done, 8000);
    zc.on('resolved', (svc: { name?: string; port?: number; addresses?: string[] }) => {
      const p = this.pairing;
      if (!p || !svc.addresses?.length) {
        return;
      }
      if (p.name && svc.name && svc.name.toLowerCase() !== p.name.toLowerCase()) {
        return;
      }
      const hosts = svc.addresses.filter(a => /^\d+\.\d+\.\d+\.\d+$/.test(a));
      if (hosts.length === 0) {
        return;
      }
      clearTimeout(timer);
      done();
      const updated: Pairing = { ...p, hosts, port: svc.port ?? p.port };
      this.pairing = updated;
      this.hostIndex = 0;
      savePairing(updated).catch(() => {});
    });
    zc.on('error', () => {
      clearTimeout(timer);
      done();
    });
    zc.scan('wowqueue', 'tcp', 'local.');
  }
}
