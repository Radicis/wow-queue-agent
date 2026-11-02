import { formatEta, formatAge } from '../src/format';
import { parseMessage, parsePairing } from '../src/protocol';

describe('formatEta', () => {
  it('formats ranges', () => {
    expect(formatEta(undefined)).toBe('unknown');
    expect(formatEta(60)).toBe('< 1 min');
    expect(formatEta(540)).toBe('9 min');
    expect(formatEta(3900)).toBe('1h 05m');
  });
});

describe('formatAge', () => {
  it('formats seconds and minutes', () => {
    const now = Date.parse('2026-01-01T00:10:00Z');
    expect(formatAge('2026-01-01T00:09:30Z', now)).toBe('30s ago');
    expect(formatAge('2026-01-01T00:05:00Z', now)).toBe('5m ago');
    expect(formatAge(undefined, now)).toBe('');
  });
});

describe('protocol parsing', () => {
  it('parses state messages and rejects junk', () => {
    expect(parseMessage('{"type":"state","state":"QUEUE"}')?.type).toBe('state');
    expect(parseMessage('{"type":"nope"}')).toBeNull();
    expect(parseMessage('not json')).toBeNull();
  });

  it('parses pairing payloads', () => {
    const ok = parsePairing('{"v":1,"hosts":["192.168.1.2"],"port":47821,"name":"PC"}');
    expect(ok).toEqual({ hosts: ['192.168.1.2'], port: 47821, name: 'PC' });
    expect(parsePairing('{"hosts":[],"port":1}')).toBeNull();
    expect(parsePairing('{"hosts":["a"],"port":"1"}')).toBeNull();
  });
});
