import { create } from "zustand";

interface DiffState {
  selectedFile: string | null;
  selectFile: (path: string | null) => void;
  reset: () => void;
}

export const useDiffStore = create<DiffState>((set) => ({
  selectedFile: null,
  selectFile: (selectedFile: string | null): void => set({ selectedFile }),
  reset: (): void => set({ selectedFile: null })
}));
