import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import { describe, expect, it } from "vitest";
import i18n from "@/i18n";
import { ImportReportView } from "./ImportStudentsPage";

describe("ImportReportView", () => {
  it("lists the spreadsheet row that failed validation", () => {
    render(
      <I18nextProvider i18n={i18n}>
        <ImportReportView
          report={{
            imported: 1,
            failed: 1,
            errors: [{ rowNumber: 3, field: "fullName", message: "Enter a student name up to 200 characters." }],
          }}
        />
      </I18nextProvider>,
    );

    expect(screen.getByText("1 students imported.")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("fullName")).toBeInTheDocument();
    expect(screen.getByText("Enter a student name up to 200 characters.")).toBeInTheDocument();
  });
});
