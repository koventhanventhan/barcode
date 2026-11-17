import { describe, expect, it } from "vitest";
import { filterHelpArticles, type HelpArticle } from "./articles";

const articles: HelpArticle[] = [
  { id: "fees", title: "Fee invoices", body: "Monthly invoices and receipts." },
  { id: "attendance", title: "Barcode attendance", body: "Scan a student card to mark present." },
];

describe("filterHelpArticles", () => {
  it("returns every article when the query is blank", () => {
    expect(filterHelpArticles(articles, "  ")).toHaveLength(2);
  });

  it("matches all words in the title or body", () => {
    expect(filterHelpArticles(articles, "scan present").map((article) => article.id)).toEqual(["attendance"]);
    expect(filterHelpArticles(articles, "invoice missing")).toEqual([]);
  });
});
