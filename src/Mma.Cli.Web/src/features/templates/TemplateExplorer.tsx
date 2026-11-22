import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, Save } from "lucide-react";
import { api } from "../../api/client";
import { ApiProblem } from "../../api/problem";
import { queryKeys } from "../../api/queryKeys";
import type { TemplateList, UpdateTemplateResult } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { Textarea } from "../../components/primitives/textarea";
import { ErrorBanner } from "../../components/shared/ErrorBanner";
import { Spinner } from "../../components/shared/Spinner";

export function TemplateExplorer() {
  const { projectId: rawId } = useParams<{ projectId: string }>();
  const projectId = Number.parseInt(rawId ?? "", 10);
  const navigate = useNavigate();
  const [selected, setSelected] = useState<{ mapper: string; name: string } | null>(null);
  const [content, setContent] = useState("");
  const [saved, setSaved] = useState<UpdateTemplateResult | null>(null);
  const templates = useQuery({
    queryKey: queryKeys.templates(projectId),
    queryFn: ({ signal }) => api.get<TemplateList>(`/api/v1/projects/${projectId}/templates`, signal),
    enabled: Number.isFinite(projectId),
    retry: 0
  });
  const save = useMutation({
    mutationFn: () => {
      if (!selected) throw new Error("Select a template first");
      return api.put<UpdateTemplateResult>(
        `/api/v1/projects/${projectId}/templates/${encodeURIComponent(selected.mapper)}/${encodeURIComponent(selected.name)}`,
        { content }
      );
    },
    onSuccess: (result) => setSaved(result)
  });

  if (!Number.isFinite(projectId)) return <ErrorBanner message="Invalid project id" />;
  if (templates.isPending) return <Spinner label="Loading templates…" />;
  if (templates.isError || !templates.data) {
    return <ErrorBanner message="Could not load templates" detail={templates.error.message} onRetry={() => void templates.refetch()} />;
  }
  const current = templates.data.templates.find((item) => item.mapper === selected?.mapper && item.name === selected?.name);
  const templateError = save.error instanceof ApiProblem && save.error.errorCode === "TEMPLATE_ERROR"
    ? save.error.detail ?? save.error.message
    : save.error?.message;

  return (
    <section className="flex h-full min-h-0 flex-col">
      <header className="flex items-center gap-3 border-b border-border px-4 py-3">
        <Button size="icon" variant="ghost" aria-label="Back to project" onClick={() => navigate(`/projects/${projectId}`)}><ArrowLeft size={15} /></Button>
        <div><h1 className="text-sm font-semibold">Templates</h1><p className="text-xs text-muted-foreground">Active mapper: {templates.data.activeMapper}</p></div>
      </header>
      <div className="grid min-h-0 flex-1 gap-4 p-4 md:grid-cols-[240px_minmax(0,1fr)]">
        <nav aria-label="Available templates" className="space-y-1 overflow-auto">
          {templates.data.templates.map((item) => (
            <button key={`${item.mapper}:${item.name}`} type="button" className="flex w-full items-center justify-between rounded border border-border px-3 py-2 text-left text-xs hover:bg-muted focus-visible:outline focus-visible:ring-2" onClick={() => { setSelected({ mapper: item.mapper, name: item.name }); setContent(item.content); setSaved(null); save.reset(); }}>
              <span>{item.name}</span>{item.isCustomOverride ? <span className="text-muted-foreground">Override</span> : null}
            </button>
          ))}
          {templates.data.templates.length === 0 ? <p className="text-xs text-muted-foreground">No templates available.</p> : null}
        </nav>
        {selected ? (
          <div className="flex min-h-0 flex-col gap-3">
            <h2 className="text-sm font-medium">{selected.name}</h2>
            {templateError ? <ErrorBanner message="Template could not be saved" detail={templateError} /> : null}
            <label className="sr-only" htmlFor="template-content">Template content</label>
            <Textarea id="template-content" className="min-h-48 flex-1 font-mono text-xs" value={content} onChange={(event) => { setContent(event.target.value); setSaved(null); }} />
            <Button className="self-start" disabled={save.isPending} onClick={() => save.mutate()}><Save size={14} />{save.isPending ? "Saving…" : "Save override"}</Button>
            {saved ? <section aria-label="Rendered template preview" className="min-h-0 overflow-auto rounded border border-border p-3"><h3 className="mb-2 text-xs font-semibold">Rendered preview</h3><pre className="whitespace-pre-wrap font-mono text-xs">{saved.renderedPreview}</pre></section> : null}
            {current?.isCustomOverride ? <p className="text-[11px] text-muted-foreground">This template has a custom override.</p> : null}
          </div>
        ) : <p className="text-sm text-muted-foreground">Select a template to inspect or override it.</p>}
      </div>
    </section>
  );
}
