import { CircleHelp, X } from "lucide-react";
import { useState } from "react";
import { Link } from "react-router-dom";
import { t } from "@/config/strings";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/features/auth/AuthProvider";
import { cn } from "@/lib/utils";
import { filterHelpArticles, helpArticleIds, type HelpArticle } from "./articles";

export function HelpWidget({ className = "" }: { className?: string }) {
  const auth = useAuth();
  const [open, setOpen] = useState(false);
  const canSupport =
    Boolean(auth.user)
    && auth.user?.role !== "SuperAdmin"
    && auth.user?.role !== "Parent"
    && auth.hasPermission("settings.manage");
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  const articles: HelpArticle[] = helpArticleIds.map((id) => ({
    id,
    title: t(`help.articles.${id}.title`),
    body: t(`help.articles.${id}.body`),
  }));
  const matches = filterHelpArticles(articles, query);
  const current = matches.find((article) => article.id === selected) ?? null;

  return (
    <div className={cn("fixed bottom-4 right-4 z-30", className)}>
      {open ? (
        <section
          aria-label={t("help.title")}
          className="mb-2 flex h-[min(28rem,70vh)] w-[min(22rem,calc(100vw-2rem))] flex-col rounded-lg border border-border bg-card"
        >
          <header className="flex items-center justify-between border-b border-border px-3 py-2">
            <h2 className="text-sm font-semibold">{t("help.title")}</h2>
            <button type="button" className="rounded-md p-1" aria-label={t("common.close")} onClick={() => setOpen(false)}>
              <X className="h-4 w-4" aria-hidden="true" />
            </button>
          </header>
          <div className="border-b border-border p-3">
            <label className="sr-only" htmlFor="help-search">
              {t("help.search")}
            </label>
            <Input
              id="help-search"
              value={query}
              placeholder={t("help.search")}
              onChange={(event) => {
                setQuery(event.target.value);
                setSelected(null);
              }}
            />
          </div>
          <div className="min-h-0 flex-1 overflow-auto p-3">
            {current ? (
              <article>
                <button type="button" className="text-[13px] text-primary" onClick={() => setSelected(null)}>
                  {t("help.back")}
                </button>
                <h3 className="mt-2 text-sm font-semibold">{current.title}</h3>
                <p className="mt-2 text-[13px] leading-5">{current.body}</p>
              </article>
            ) : matches.length === 0 ? (
              <p className="text-[13px] text-muted">{t("help.empty")}</p>
            ) : (
              <ul className="grid gap-1">
                {matches.map((article) => (
                  <li key={article.id}>
                    <button
                      type="button"
                      className="w-full rounded-md px-2 py-2 text-left text-[13px] hover:bg-secondary"
                      onClick={() => setSelected(article.id)}
                    >
                      {article.title}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
          {canSupport ? (
            <footer className="border-t border-border px-3 py-2">
              <Link
                to="/app/support"
                onClick={() => setOpen(false)}
                className="text-[13px] font-medium text-primary hover:underline"
              >
                {t("support.openSupportPage")}
              </Link>
            </footer>
          ) : null}
        </section>
      ) : null}
      <button
        type="button"
        className="ml-auto flex h-10 items-center gap-2 rounded-md border border-border bg-card px-3 text-sm font-medium"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <CircleHelp className="h-4 w-4" aria-hidden="true" />
        {t("help.open")}
      </button>
    </div>
  );
}
