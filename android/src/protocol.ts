export type WowState =
  | 'UNKNOWN'
  | 'NO_CLIENT'
  | 'LOGGED_IN'
  | 'QUEUE'
  | 'DISCONNECTED'
  | 'ERROR';

export type EventKind =
  | 'LOGGED_IN'
  | 'DISCONNECTED'
  | 'ERROR'
  | 'NO_CLIENT'
  | 'QUEUE_SOON'
  | 'AGENT_OFFLINE';

export interface StateMessage {
  type: 'state';
  v: number;
  state: WowState;
  position?: number;
  etaSeconds?: number;
  etaUpdatedAt?: string;
  soon: boolean;
  since: string;
  observedAt: string;
}

export interface EventMessage {
  type: 'event';
  v: number;
  id: string;
  kind: EventKind;
  message: string;
  at: string;
}

export interface HelloMessage {
  type: 'hello';
  v: number;
  agent: string;
  bootId: string;
}

export type AgentMessage = StateMessage | EventMessage | HelloMessage;

export interface Pairing {
  hosts: string[];
  port: number;
  name?: string;
}

export function parseMessage(raw: string): AgentMessage | null {
  try {
    const m = JSON.parse(raw);
    if (m && (m.type === 'state' || m.type === 'event' || m.type === 'hello')) {
      return m as AgentMessage;
    }
  } catch {}
  return null;
}

// Accepts the JSON shown in the Windows agent's pairing window / QR code.
export function parsePairing(raw: string): Pairing | null {
  try {
    const p = JSON.parse(raw);
    if (
      Array.isArray(p.hosts) &&
      p.hosts.length > 0 &&
      p.hosts.every((h: unknown) => typeof h === 'string') &&
      Number.isInteger(p.port)
    ) {
      return {
        hosts: p.hosts,
        port: p.port,
        name: typeof p.name === 'string' ? p.name : undefined,
      };
    }
  } catch {}
  return null;
}
