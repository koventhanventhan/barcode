import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ImportReportView } from "./ImportStudentsPage";

describe("ImportReportView", () => {
  it("lists the spreadsheet row that failed validation", () => {
    render(
              <ImportReportView
          report={{
            imported: 1,
            failed: 1,
            errors: [{ rowNumber: 3, field: "fullName", message: "Enter a student name up to 200 characters." }],
          }}
        />,
    );

    expect(screen.getByText("1 students imported.")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("fullName")).toBeInTheDocument();
    expect(screen.getByText("Enter a student name up to 200 characters.")).toBeInTheDocument();
  });
});
