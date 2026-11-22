import { BaseEdge, EdgeLabelRenderer, getSmoothStepPath } from "@xyflow/react";
import type { Edge, EdgeProps } from "@xyflow/react";
import type { RelationEdgeData } from "./layout";

export function RelationEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  data,
  markerEnd
}: EdgeProps<Edge<RelationEdgeData>>) {
  const [path, labelX, labelY] = getSmoothStepPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    borderRadius: 8
  });

  const applied = data?.relation.applied ?? true;

  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        {...(markerEnd ? { markerEnd } : {})}
        style={applied ? { strokeWidth: 1.5 } : { strokeDasharray: "5 3", strokeWidth: 1.5 }}
      />
      <EdgeLabelRenderer>
        <span
          style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
          className="pointer-events-none absolute rounded border border-border bg-card px-1 py-0.5 font-mono text-[9px] text-muted-foreground"
        >
          {data?.relation.foreignKey}
        </span>
      </EdgeLabelRenderer>
    </>
  );
}
