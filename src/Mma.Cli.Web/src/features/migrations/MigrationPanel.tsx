import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Hammer, Database } from "lucide-react";
import { api } from "../../api/client";
import { errorMessage } from "../../api/problem";
import type { JobAccepted } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { Input } from "../../components/primitives/input";
import { Select } from "../../components/primitives/select";
import { useToast } from "../../components/primitives/toast";

export interface MigrationPanelProps {
  projectId: number;
  onJobStarted: (jobId: string) => void;
}

export function MigrationPanel({ projectId, onJobStarted }: MigrationPanelProps) {
  const toast = useToast();
  const [configuration, setConfiguration] = useState("Debug");
  const [migrationName, setMigrationName] = useState("");

  const build = useMutation({
    mutationFn: () =>
      api.post<JobAccepted>(`/api/v1/projects/${projectId}/build`, { configuration }),
    onSuccess: (result) => {
      toast.push(`Build started (${configuration})`, "info");
      onJobStarted(result.jobId);
    },
    onError: (error) => toast.push(errorMessage(error), "error")
  });

  const migrate = useMutation({
    mutationFn: () =>
      api.post<JobAccepted>(`/api/v1/projects/${projectId}/migrations`, {
        name: migrationName.trim()
      }),
    onSuccess: (result) => {
      toast.push("Migration started", "info");
      onJobStarted(result.jobId);
    },
    onError: (error) => toast.push(errorMessage(error), "error")
  });

  return (
    <section
      aria-label="Build and migrations"
      className="flex flex-wrap items-end gap-2 border-t border-border bg-card px-3 py-2"
    >
      <label className="space-y-1">
        <span className="block text-[10px] uppercase text-muted-foreground">Configuration</span>
        <Select
          value={configuration}
          onChange={(event) => setConfiguration(event.target.value)}
          className="h-7 w-24 text-[11px]"
          data-testid="build-configuration"
        >
          <option value="Debug">Debug</option>
          <option value="Release">Release</option>
        </Select>
      </label>

      <Button
        size="sm"
        variant="outline"
        onClick={() => build.mutate()}
        disabled={build.isPending}
        data-testid="build-button"
      >
        <Hammer size={12} /> {build.isPending ? "Starting…" : "Build"}
      </Button>

      <div className="mx-1 h-6 w-px bg-border" aria-hidden />

      <label className="space-y-1">
        <span className="block text-[10px] uppercase text-muted-foreground">Migration name</span>
        <Input
          value={migrationName}
          onChange={(event) => setMigrationName(event.target.value)}
          placeholder="InitialCreate"
          className="h-7 w-44 text-[11px]"
          data-testid="migration-name"
        />
      </label>

      <Button
        size="sm"
        variant="outline"
        onClick={() => migrate.mutate()}
        disabled={migrate.isPending || !migrationName.trim()}
        data-testid="migration-button"
      >
        <Database size={12} /> {migrate.isPending ? "Starting…" : "Add migration"}
      </Button>
    </section>
  );
}
