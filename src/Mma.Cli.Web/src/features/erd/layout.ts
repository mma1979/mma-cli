import dagre from "@dagrejs/dagre";
import type { Edge, Node } from "@xyflow/react";
import type { ManifestEntity, ManifestRelation } from "../../api/types";

export type EntityNodeData = {
  entity: ManifestEntity;
  selected: boolean;
};

export type RelationEdgeData = {
  relation: ManifestRelation;
  selected: boolean;
};

export const NODE_WIDTH = 240;
const NODE_HEADER = 46;
const PROPERTY_ROW = 22;
const NODE_PADDING = 14;

export interface LayoutResult {
  nodes: Node<EntityNodeData>[];
  edges: Edge<RelationEdgeData>[];
}

export function nodeHeight(entity: ManifestEntity): number {
  return NODE_HEADER + NODE_PADDING + entity.properties.length * PROPERTY_ROW;
}

export function computeLayout(
  entities: ManifestEntity[],
  relations: ManifestRelation[],
  selectedEntity: string | null = null
): LayoutResult {
  if (entities.length === 0) {
    return { nodes: [], edges: [] };
  }

  const entityNames = new Set(entities.map((entity) => entity.name));

  const graph = new dagre.graphlib.Graph();
  graph.setDefaultEdgeLabel(() => ({}));
  graph.setGraph({
    rankdir: "TB",
    nodesep: 60,
    ranksep: 90,
    marginx: 24,
    marginy: 24
  });

  for (const entity of entities) {
    graph.setNode(entity.name, {
      width: NODE_WIDTH,
      height: nodeHeight(entity)
    });
  }

  const usableRelations = relations.filter(
    (relation) =>
      entityNames.has(relation.parentEntity) &&
      entityNames.has(relation.childEntity) &&
      relation.parentEntity !== relation.childEntity
  );

  for (const relation of usableRelations) {
    graph.setEdge(relation.parentEntity, relation.childEntity);
  }

  dagre.layout(graph);

  const nodes: Node<EntityNodeData>[] = entities.map((entity) => {
    const positioned = graph.node(entity.name) as
      | { x?: number; y?: number; width?: number; height?: number }
      | undefined;
    const width = positioned?.width ?? NODE_WIDTH;
    const height = positioned?.height ?? nodeHeight(entity);
    const centerX = positioned?.x ?? 0;
    const centerY = positioned?.y ?? 0;

    return {
      id: entity.name,
      type: "entity",
      position: { x: centerX - width / 2, y: centerY - height / 2 },
      data: { entity, selected: entity.name === selectedEntity },
      selected: entity.name === selectedEntity,
      width,
      height,
      draggable: true
    };
  });

  const edges: Edge<RelationEdgeData>[] = relations
    .filter(
      (relation) =>
        entityNames.has(relation.parentEntity) && entityNames.has(relation.childEntity)
    )
    .map((relation) => ({
      id: relation.name,
      source: relation.parentEntity,
      target: relation.childEntity,
      type: "relation",
      label: relation.foreignKey,
      data: { relation, selected: relation.name === selectedEntity },
      ...(relation.applied
        ? {}
        : { style: { strokeDasharray: "5 3" }, className: "erd-edge-draft" })
    }));

  return { nodes, edges };
}

export function entitiesOverlap(a: Node<EntityNodeData>, b: Node<EntityNodeData>): boolean {
  const aWidth = a.width ?? NODE_WIDTH;
  const bWidth = b.width ?? NODE_WIDTH;
  const aHeight = a.height ?? nodeHeight(a.data.entity);
  const bHeight = b.height ?? nodeHeight(b.data.entity);

  return (
    a.position.x < b.position.x + bWidth &&
    b.position.x < a.position.x + aWidth &&
    a.position.y < b.position.y + bHeight &&
    b.position.y < a.position.y + aHeight
  );
}
