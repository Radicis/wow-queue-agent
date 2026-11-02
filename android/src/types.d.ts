declare module 'react-native-zeroconf' {
  export default class Zeroconf {
    scan(type?: string, protocol?: string, domain?: string): void;
    stop(): void;
    removeDeviceListeners(): void;
    on(event: string, listener: (...args: any[]) => void): this;
  }
}
