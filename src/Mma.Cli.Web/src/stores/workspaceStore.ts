import { create } from "zustand";

export type ViewMode = "erd" | "table";

interface WorkspaceState {
  projectId: number | null;
  activeView: ViewMode;
  selectedEntity: string | null;
  selectedRelation: string | null;
  inspectorOpen: boolean;
  planOpen: boolean;
  terminalJobId: string | null;
  addRelationMode: boolean;
  relationParent: string | null;

  setProject: (projectId: number) => void;
  setView: (view: ViewMode) => void;
  selectEntity: (name: string | null) => void;
  selectRelation: (name: string | null) => void;
  setInspectorOpen: (open: boolean) => void;
  setPlanOpen: (open: boolean) => void;
  openTerminal: (jobId: string) => void;
  closeTerminal: () => void;
  startAddRelation: () => void;
  setRelationParent: (name: string | null) => void;
  cancelAddRelation: () => void;
}

export const useWorkspaceStore = create<WorkspaceState>((set, get) => ({
  projectId: null,
  activeView: "erd",
  selectedEntity: null,
  selectedRelation: null,
  inspectorOpen: false,
  planOpen: false,
  terminalJobId: null,
  addRelationMode: false,
  relationParent: null,

  setProject: (projectId: number): void => {
    if (get().projectId === projectId) {
      return;
    }
    set({
      projectId,
      selectedEntity: null,
      selectedRelation: null,
      inspectorOpen: false,
      planOpen: false,
      terminalJobId: null,
      addRelationMode: false,
      relationParent: null
    });
  },

  setView: (activeView: ViewMode): void => set({ activeView }),
  selectEntity: (selectedEntity: string | null): void =>
    set({ selectedEntity, selectedRelation: null }),
  selectRelation: (selectedRelation: string | null): void =>
    set({ selectedRelation, selectedEntity: null }),
  setInspectorOpen: (inspectorOpen: boolean): void => set({ inspectorOpen }),
  setPlanOpen: (planOpen: boolean): void => set({ planOpen }),
  openTerminal: (terminalJobId: string): void => set({ terminalJobId }),
  closeTerminal: (): void => set({ terminalJobId: null }),
  startAddRelation: (): void => set({ addRelationMode: true, relationParent: null }),
  setRelationParent: (relationParent: string | null): void => set({ relationParent }),
  cancelAddRelation: (): void => set({ addRelationMode: false, relationParent: null })
}));
