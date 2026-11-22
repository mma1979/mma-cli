import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PropertyEditor } from "./PropertyEditor";
import { SUPPORTED_PROPERTY_TYPES } from "../../api/types";

describe("PropertyEditor", () => {
  it("limits the type selector to the supported types", () => {
    render(<PropertyEditor entityName="Order" existingNames={[]} onSubmit={vi.fn()} onCancel={vi.fn()} />);
    const options = within(screen.getByTestId("property-type")).getAllByRole("option").map((option) => (option as HTMLOptionElement).value);
    expect(options).toEqual([...SUPPORTED_PROPERTY_TYPES]);
  });

  it.each(["0", "-1"]) ("rejects max length %s", async (maxLength) => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<PropertyEditor entityName="Order" existingNames={[]} onSubmit={onSubmit} onCancel={vi.fn()} />);
    await user.type(screen.getByTestId("property-name"), "Title");
    await user.type(screen.getByTestId("property-max-length"), maxLength);
    await user.click(screen.getByTestId("property-submit"));
    expect(screen.getByRole("alert")).toHaveTextContent("greater than zero");
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("omits required when unset and includes explicit false", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<PropertyEditor entityName="Order" existingNames={[]} onSubmit={onSubmit} onCancel={vi.fn()} />);
    await user.type(screen.getByTestId("property-name"), "Title");
    await user.selectOptions(screen.getByTestId("property-required"), "false");
    await user.click(screen.getByTestId("property-submit"));
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ name: "Title", required: false }));
  });
});
