import {
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
  type VisibilityState,
} from "@tanstack/react-table";
import { ChevronDown, Download } from "lucide-react";
import { useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { buildCsv, downloadCsv } from "@/lib/csv";

export type DataColumn<T> = {
  id: string;
  header: string;
  cell: (row: T) => ReactNode;
  sortValue?: (row: T) => string | number;
  exportValue?: (row: T) => string;
  enableHiding?: boolean;
};

export function DataTable<T>({
  data,
  columns,
  filename,
  pageSize = 10,
  getRowId,
  enableExport = true,
}: {
  data: T[];
  columns: DataColumn<T>[];
  filename: string;
  pageSize?: number;
  getRowId?: (row: T) => string;
  enableExport?: boolean;
}) {
  const { t } = useTranslation();
  const [sorting, setSorting] = useState<SortingState>([]);
  const [globalFilter, setGlobalFilter] = useState("");
  const [columnVisibility, setColumnVisibility] = useState<VisibilityState>({});
  const [columnsOpen, setColumnsOpen] = useState(false);

  const defs = useMemo<ColumnDef<T>[]>(
    () =>
      columns.map((column) => ({
        id: column.id,
        header: column.header,
        accessorFn: (row: T) => column.sortValue?.(row) ?? column.exportValue?.(row) ?? "",
        cell: ({ row }) => column.cell(row.original),
        enableHiding: column.enableHiding !== false,
        enableSorting: true,
      })),
    [columns],
  );

  const table = useReactTable({
    data,
    columns: defs,
    state: { sorting, globalFilter, columnVisibility },
    onSortingChange: setSorting,
    onGlobalFilterChange: setGlobalFilter,
    onColumnVisibilityChange: setColumnVisibility,
    globalFilterFn: (row, _columnId, filterValue) => {
      const haystack = columns
        .map((column) => column.exportValue?.(row.original) ?? String(column.sortValue?.(row.original) ?? ""))
        .join(" ")
        .toLowerCase();
      return haystack.includes(String(filterValue).toLowerCase());
    },
    getRowId: getRowId ? (row) => getRowId(row) : undefined,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    initialState: { pagination: { pageSize } },
  });

  function exportRows() {
    const visible = table.getVisibleLeafColumns();
    const headers = visible.map((column) => {
      const source = columns.find((item) => item.id === column.id);
      return source?.header ?? column.id;
    });
    const rows = table.getFilteredRowModel().rows.map((row) =>
      visible.map((column) => {
        const source = columns.find((item) => item.id === column.id);
        return source?.exportValue?.(row.original) ?? String(source?.sortValue?.(row.original) ?? "");
      }),
    );
    downloadCsv(filename, buildCsv(headers, rows));
  }

  const pageCount = Math.max(table.getPageCount(), 1);

  return (
    <div className="grid gap-3">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <Input
          value={globalFilter}
          onChange={(event) => setGlobalFilter(event.target.value)}
          placeholder={t("table.search")}
          aria-label={t("table.search")}
          className="sm:max-w-xs"
        />
        <div className="flex gap-2">
          <div className="relative">
            <Button type="button" variant="outline" size="sm" onClick={() => setColumnsOpen((open) => !open)}>
              <ChevronDown className="h-4 w-4" aria-hidden="true" />
              {t("table.columns")}
            </Button>
            {columnsOpen ? (
              <div className="absolute right-0 z-20 mt-1 w-48 rounded-md border border-border bg-card p-2">
                {table.getAllLeafColumns().map((column) => (
                  <label key={column.id} className="flex items-center gap-2 px-1 py-1 text-[13px]">
                    <input
                      type="checkbox"
                      checked={column.getIsVisible()}
                      onChange={column.getToggleVisibilityHandler()}
                    />
                    {columns.find((item) => item.id === column.id)?.header ?? column.id}
                  </label>
                ))}
              </div>
            ) : null}
          </div>
          {enableExport ? (
            <Button type="button" variant="outline" size="sm" onClick={exportRows}>
              <Download className="h-4 w-4" aria-hidden="true" />
              {t("table.export")}
            </Button>
          ) : null}
        </div>
      </div>
      <div className="overflow-x-auto rounded-lg border border-border bg-card">
        <table className="w-full min-w-[640px] border-collapse text-left text-sm">
          <thead className="sticky top-0 bg-secondary text-xs uppercase tracking-wide text-muted">
            {table.getHeaderGroups().map((group) => (
              <tr key={group.id}>
                {group.headers.map((header) => {
                  const sorted = header.column.getIsSorted();
                  return (
                    <th key={header.id} className="border-b border-border px-3 py-2 font-medium" aria-sort={sorted === "asc" ? "ascending" : sorted === "desc" ? "descending" : "none"}>
                      <button type="button" className="inline-flex items-center gap-1" onClick={header.column.getToggleSortingHandler()}>
                        {flexRender(header.column.columnDef.header, header.getContext())}
                      </button>
                    </th>
                  );
                })}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.length === 0 ? (
              <tr>
                <td className="px-3 py-8 text-center text-muted" colSpan={columns.length}>
                  {t("table.empty")}
                </td>
              </tr>
            ) : (
              table.getRowModel().rows.map((row) => (
                <tr key={row.id} className="border-b border-border last:border-0">
                  {row.getVisibleCells().map((cell) => (
                    <td key={cell.id} className="px-3 py-2 align-middle">
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      <div className="flex items-center justify-between gap-2 text-[13px] text-muted">
        <p>{t("table.rows", { count: table.getFilteredRowModel().rows.length })}</p>
        <div className="flex items-center gap-2">
          <Button type="button" variant="outline" size="sm" onClick={() => table.previousPage()} disabled={!table.getCanPreviousPage()}>
            {t("table.previous")}
          </Button>
          <span className="tabular">{t("table.page", { page: table.getState().pagination.pageIndex + 1, pages: pageCount })}</span>
          <Button type="button" variant="outline" size="sm" onClick={() => table.nextPage()} disabled={!table.getCanNextPage()}>
            {t("table.next")}
          </Button>
        </div>
      </div>
    </div>
  );
}
