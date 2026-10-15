export interface OnlineMidiItem {
  id: string;
  title: string;
  artist: string;
  uploader: string;
  durationSeconds: number;
  bpm: number;
  trackCount: number;
  noteCount: number;
  fileSizeBytes: number;
  downloadCount: number;
  viewCount: number;
  category: string;
  previewAudioUrl?: string;
  downloadUrl: string;
  isDownloaded?: boolean;
}

export type MidiProviderType = 'MidiShow' | 'NanoMidi' | 'LocalCache';

export interface CategoryOption {
  id: string;
  name: string;
}

export interface SortOption {
  id: string;
  name: string;
  icon?: string;
}
