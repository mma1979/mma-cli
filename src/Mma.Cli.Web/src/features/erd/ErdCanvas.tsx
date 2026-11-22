import { useCallback, useEffect, useMemo } from "react";
import {
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState
} from "@xyflow/react";
import type { Edge, Node } from "@xyflow/react";
import { GitBranch, Plus, X } from "lucide-react";
import type { ManifestEntity, ManifestRelation } from "../../api/types";
import { computeLayout } from "./layout";
import type { EntityNodeData, RelationEdgeData } from "./layout";
import { EntityNode } from "./EntityNode";
import { RelationEdge } from "./RelationEdge";
import { Button } from "../../components/primitives/button";
import { EmptyState } from "../../components/shared/EmptyState";

const nodeTypes = { entity: EntityNode };
const edgeTypes = { relation: RelationEdge };

export interface ErdCanvasProps {
  entities: ManifestEntity[];
  relations: ManifestRelation[];
  selectedEntity: string | null;
  addRelationMode: boolean;
  relationParent: string | null;
  onSelectEntity: (name: string) => void;
  onAddEntity: () => void;
  onStartAddRelation: () => void;
  onCancelAddRelation: () => void;
}

function Canvas({
  entities,
  relations,
  selectedEntity,
  addRelationMode,
  relationParent,
  onSelectEntity,
  onAddEntity,
  onStartAddRelation,
  onCancelAddRelation
}: ErdCanvasProps) {
  const layout = useMemo(
    () => computeLayout(entities, relations, selectedEntity),
    [entities, relations, selectedEntity]
  );

  const [nodes, setNodes, onNodesChange] = useNodesState<Node<EntityNodeData>>(layout.nodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge<RelationEdgeData>>(layout.edges);

  useEffect(() => {
    setNodes(layout.nodes);
    setEdges(layout.edges);
  }, [layout, setNodes, setEdges]);

  const handleNodeClick = useCallback(
    (_event: unknown, node: Node<EntityNodeData>) => {
      onSelectEntity(node.id);
    },
    [onSelectEntity]
  );

  if (entities.length === 0) {
    return (
      <div className="flex h-full items-center justify-center p-8">
        <EmptyState
          title="No entities yet"
          description="Add your first entity to start shaping the domain model."
          action={
            <Button onClick={onAddEntity}>
              <Plus size={14} /> Add entity
            </Button>
          }
        />
      </div>
    );
  }

  return (
    <div className="relative h-full" data-testid="erd-canvas">
      <div className="absolute right-3 top-3 z-10 flex items-center gap-2">
        {addRelationMode ? (
          <div className="flex items-center gap-2 rounded-md border border-primary/40 bg-card px-2 py-1 text-[11px]">
            <GitBranch size={12} className="text-primary" aria-hidden />
            <span>{relationParent ? `Child of ${relationParent}` : "Select the parent entity"}</span>
            <Button
              size="icon"
              variant="ghost"
              aria-label="Cancel relation"
              onClick={onCancelAddRelation}
            >
              <X size={12} />
            </Button>
          </div>
        ) : (
          <Button size="sm" variant="outline" onClick={onStartAddRelation}>
            <GitBranch size={13} /> Add relation
          </Button>
        )}
        <Button size="sm" onClick={onAddEntity}>
          <Plus size={13} /> Add entity
        </Button>
      </div>

      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onNodeClick={handleNodeClick}
        nodesConnectable={false}
        fitView
        minZoom={0.2}
        maxZoom={1.6}
      >
        <Background variant={BackgroundVariant.Dots} gap={18} size={1} />
        <Controls showInteractive={false} />
        <MiniMap pannable zoomable className="!bg-card" />
      </ReactFlow>
    </div>
  );
}

export function ErdCanvas(props: ErdCanvasProps) {
  return (
    <ReactFlowProvider>
      <Canvas {...props} />
    </ReactFlowProvider>
  );
}
