export type ServiceState = 'ready' | 'inactive' | 'notChecked' | 'checking' | 'permissionRequired'
  | 'reconnecting' | 'unavailable' | 'unknown';
export interface ServiceStatus { state: ServiceState; detail: string | null }
export type DesktopSession = 'windows' | 'macos' | 'x11' | 'wayland' | 'unknown';
export interface SystemEnvironment {
  osName: string | null;
  osVersion: string | null;
  systemArchitecture: string;
  appArchitecture: string;
  desktopSession: DesktopSession;
}
export interface SystemSnapshot {
  environment: SystemEnvironment;
  keyboard: ServiceStatus & { backend: 'enigoWindows' | 'enigoMacos' | 'enigoX11' | 'remoteDesktopPortal' | 'unknown' };
  audio: ServiceStatus;
  tray: ServiceStatus;
  mediaControls: ServiceStatus;
}
export interface SystemApi { getSnapshot(): Promise<SystemSnapshot> }
