import AsyncStorage from '@react-native-async-storage/async-storage';
import { Pairing, parsePairing } from './protocol';

const PAIRING_KEY = 'pairing.v1';
const SEEN_KEY = 'seenEvents.v1';

export async function loadPairing(): Promise<Pairing | null> {
  const raw = await AsyncStorage.getItem(PAIRING_KEY);
  return raw ? parsePairing(raw) : null;
}

export async function savePairing(p: Pairing | null): Promise<void> {
  if (p) {
    await AsyncStorage.setItem(PAIRING_KEY, JSON.stringify(p));
  } else {
    await AsyncStorage.removeItem(PAIRING_KEY);
  }
}

export async function loadSeenEvents(): Promise<string[]> {
  try {
    return JSON.parse((await AsyncStorage.getItem(SEEN_KEY)) ?? '[]');
  } catch {
    return [];
  }
}

export async function saveSeenEvents(ids: string[]): Promise<void> {
  await AsyncStorage.setItem(SEEN_KEY, JSON.stringify(ids.slice(-50)));
}
