export function formatMonthlyPrice(price: number | null | undefined, currency: string): string | null {
  if (price == null || Number.isNaN(price)) return null;
  const amount = new Intl.NumberFormat("en-LK", { maximumFractionDigits: 0 }).format(price);
  return `${currency} ${amount}`;
}

/** Whole months of the monthly price that the yearly price does not charge. */
export function monthsFree(monthly: number | null | undefined, yearly: number | null | undefined): number | null {
  if (monthly == null || yearly == null || monthly <= 0) return null;
  const free = (monthly * 12 - yearly) / monthly;
  const rounded = Math.round(free);
  return rounded > 0 ? rounded : null;
}

export function monthsFreeBadge(
  plans: Array<{ priceMonthlyLkr: number | null; priceYearlyLkr: number | null }>,
): { count: number; varied: boolean } | null {
  const counts = plans
    .map((plan) => monthsFree(plan.priceMonthlyLkr, plan.priceYearlyLkr))
    .filter((count): count is number => count != null);
  if (counts.length === 0) return null;
  const varied = counts.some((count) => count !== counts[0]);
  return { count: varied ? Math.max(...counts) : counts[0], varied };
}

export type AddonPrice = { kind: "none" } | { kind: "exact"; amount: number } | { kind: "from"; amount: number };

export function addonPrice(values: Array<number | null | undefined>): AddonPrice {
  const present = values.filter((value): value is number => value != null && !Number.isNaN(value));
  if (present.length === 0) return { kind: "none" };
  const min = Math.min(...present);
  const max = Math.max(...present);
  return min === max ? { kind: "exact", amount: min } : { kind: "from", amount: min };
}

const moneyPattern = /^(?:\d+)(?:\.\d{1,2})?$/;

/** Blank becomes null. Invalid input becomes NaN. */
export function parseOptionalMoney(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;
  if (!moneyPattern.test(trimmed)) return Number.NaN;
  const value = Number(trimmed);
  if (value > 100_000_000) return Number.NaN;
  return value;
}
