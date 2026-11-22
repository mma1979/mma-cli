import type { ReactNode } from "react";
import { StatusBar } from "./StatusBar";
import { TopBar } from "./TopBar";

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex h-full min-h-0 flex-col">
      <TopBar />
      <main className="min-h-0 flex-1 overflow-hidden">{children}</main>
      <StatusBar />
    </div>
  );
}
