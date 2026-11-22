import { Badge } from "../primitives/badge";

export type StatusKind = "draft" | "applied" | "modified" | "drift";

const toneByStatus: Record<StatusKind, "warning" | "success" | "info" | "danger"> = {
  draft: "warning",
  applied: "success",
  modified: "info",
  drift: "danger"
};

const labelByStatus: Record<StatusKind, string> = {
  draft: "Draft",
  applied: "Applied",
  modified: "Modified",
  drift: "Drift"
};

export function StatusPill({ status }: { status: StatusKind }) {
  return <Badge tone={toneByStatus[status]}>{labelByStatus[status]}</Badge>;
}
