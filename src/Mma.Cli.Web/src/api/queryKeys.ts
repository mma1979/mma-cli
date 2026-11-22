export const queryKeys = {
  health: ["health"] as const,
  workspaces: ["workspaces"] as const,
  project: (id: number) => ["project", id] as const,
  templates: (id: number) => ["templates", id] as const,
  plan: (id: number) => ["plan", id] as const
};
