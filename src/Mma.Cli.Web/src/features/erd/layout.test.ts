import { describe, expect, it } from "vitest";
import { computeLayout, entitiesOverlap } from "./layout";
import type { ManifestEntity, ManifestRelation } from "../../api/types";

const entity = (name: string): ManifestEntity => ({
  name,
  pkType: "Guid",
  hasService: true,
  hasApi: true,
  applied: false,
  properties: []
});

const relation = (parentEntity: string, childEntity: string): ManifestRelation => ({
  name: `${parentEntity}${childEntity}`,
  parentEntity,
  childEntity,
  foreignKey: `${parentEntity}Id`,
  foreignKeyType: "Guid",
  relationType: "1:N",
  deleteBehavior: "Cascade",
  applied: false
});

describe("computeLayout", () => {
  it("positions nodes without overlap and links existing nodes", () => {
    const result = computeLayout([entity("Order"), entity("OrderItem")], [relation("Order", "OrderItem")]);
    expect(result.nodes).toHaveLength(2);
    const [first, second] = result.nodes;
    if (!first || !second) throw new Error("Expected two positioned entities");
    expect(entitiesOverlap(first, second)).toBe(false);
    expect(result.edges.map(({ source, target }) => [source, target])).toEqual([["Order", "OrderItem"]]);
    expect(result.edges[0]?.style?.strokeDasharray).toBe("5 3");
  });

  it("handles self-relations without throwing", () => {
    const result = computeLayout([entity("Node")], [relation("Node", "Node")]);
    expect(result.nodes).toHaveLength(1);
    expect(result.edges).toHaveLength(1);
  });
});
