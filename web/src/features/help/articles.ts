export type HelpArticle = {
  id: string;
  title: string;
  body: string;
};

export const helpArticleIds = [
  "attendance",
  "fees",
  "exams",
  "messages",
  "portal",
  "backups",
  "plans",
  "switches",
] as const;

export function filterHelpArticles(articles: HelpArticle[], query: string): HelpArticle[] {
  const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return articles;
  return articles.filter((article) => {
    const haystack = `${article.title} ${article.body}`.toLowerCase();
    return terms.every((term) => haystack.includes(term));
  });
}
