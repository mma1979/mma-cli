import { lazy, Suspense, useEffect } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Link, Route, Routes } from "react-router-dom";
import { AppShell } from "./components/layout/AppShell";
import { ToastProvider } from "./components/primitives/toast";
import { Spinner } from "./components/shared/Spinner";
import { Button } from "./components/primitives/button";
import { useAuthStore } from "./stores/authStore";

const WorkspaceHub = lazy(() => import("./features/projects/WorkspaceHub").then((module) => ({ default: module.WorkspaceHub })));
const SolutionStudio = lazy(() => import("./features/studio/SolutionStudio").then((module) => ({ default: module.SolutionStudio })));
const TemplateExplorer = lazy(() => import("./features/templates/TemplateExplorer").then((module) => ({ default: module.TemplateExplorer })));
const DbImportWizard = lazy(() => import("./features/db-import/DbImportWizard").then((module) => ({ default: module.DbImportWizard })));

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 0, refetchOnWindowFocus: false } }
});

function AuthenticatedRoutes() {
  const status = useAuthStore((state) => state.status);
  const bootstrap = useAuthStore((state) => state.bootstrap);

  useEffect(() => {
    void bootstrap();
  }, [bootstrap]);

  if (status === "unknown") {
    return <Spinner label="Starting MMA workspace…" />;
  }

  if (status === "anonymous") {
    return (
      <section className="space-y-3 p-8" role="alert">
        <p className="text-sm text-danger">Authentication is required to open this workspace.</p>
        <Button variant="outline" onClick={() => void bootstrap()}>Retry authentication</Button>
      </section>
    );
  }

  return (
    <AppShell>
      <Suspense fallback={<Spinner label="Loading workspace view…" />}>
        <Routes>
          <Route path="/" element={<WorkspaceHub />} />
          <Route path="/projects/:projectId" element={<SolutionStudio />} />
          <Route path="/projects/:projectId/templates" element={<TemplateExplorer />} />
          <Route path="/projects/:projectId/db-import" element={<DbImportWizard />} />
          <Route path="/settings" element={<SettingsPlaceholder />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </Suspense>
    </AppShell>
  );
}

function SettingsPlaceholder() {
  return (
    <section className="mx-auto max-w-2xl space-y-2 p-8">
      <h1 className="text-lg font-semibold">Settings</h1>
      <p className="text-sm text-muted-foreground">Provider &amp; MCP settings arrive in Phase 5</p>
    </section>
  );
}

function NotFound() {
  return (
    <section className="mx-auto max-w-2xl space-y-2 p-8">
      <h1 className="text-lg font-semibold">Page not found</h1>
      <p className="text-sm text-muted-foreground">The requested workspace page does not exist.</p>
      <Link className="text-sm text-primary underline focus-visible:outline" to="/">Return to workspaces</Link>
    </section>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <BrowserRouter>
          <AuthenticatedRoutes />
        </BrowserRouter>
      </ToastProvider>
    </QueryClientProvider>
  );
}
