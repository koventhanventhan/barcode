import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DataTable, type DataColumn } from "./DataTable";

type Row = { id: string; name: string; city: string };

const rows: Row[] = [
  { id: "1", name: "Amali", city: "Jaffna" },
  { id: "2", name: "Ravi", city: "Kandy" },
  { id: "3", name: "Nimal", city: "Galle" },
];

const columns: DataColumn<Row>[] = [
  {
    id: "name",
    header: "Name",
    sortValue: (row) => row.name,
    exportValue: (row) => row.name,
    cell: (row) => row.name,
  },
  {
    id: "city",
    header: "City",
    sortValue: (row) => row.city,
    exportValue: (row) => row.city,
    cell: (row) => row.city,
  },
];

function renderTable() {
  return render(
          <DataTable data={rows} columns={columns} filename="people.csv" pageSize={2} getRowId={(row) => row.id} />,
  );
}

describe("DataTable", () => {
  it("sorts, filters, paginates, toggles columns, and exports csv", async () => {
    const user = userEvent.setup();
    const createObjectURL = vi.fn(() => "blob:table");
    vi.stubGlobal("URL", { createObjectURL, revokeObjectURL: vi.fn() });
    renderTable();

    expect(screen.getByText("Amali")).toBeInTheDocument();
    expect(screen.queryByText("Nimal")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Next" }));
    expect(screen.getByText("Nimal")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Previous" }));

    await user.click(screen.getByRole("button", { name: "Name" }));
    const ordered = screen.getAllByRole("row").map((row) => row.textContent);
    expect(ordered[1]).toContain("Amali");

    await user.type(screen.getByLabelText("Filter rows"), "Kandy");
    expect(screen.getByText("Ravi")).toBeInTheDocument();
    expect(screen.queryByText("Amali")).not.toBeInTheDocument();
    await user.clear(screen.getByLabelText("Filter rows"));

    await user.click(screen.getByRole("button", { name: "Columns" }));
    await user.click(screen.getByRole("checkbox", { name: "City" }));
    expect(screen.queryByRole("button", { name: "City" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(createObjectURL).toHaveBeenCalled();
  });
});
