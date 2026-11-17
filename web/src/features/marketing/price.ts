export function formatMonthlyPrice(price: number | null | undefined, currency: string): string | null {
  if (price == null || Number.isNaN(price)) return null;
  const amount = new Intl.NumberFormat("en-LK", { maximumFractionDigits: 0 }).format(price);
  return `${currency} ${amount}`;
}
