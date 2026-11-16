import { Button } from "@/components/ui/button";
import { ProductName } from "@/config/brand";
import en from "@/i18n/en.json";

export default function App() {
  return (
    <main className="mx-auto max-w-3xl px-4 py-8">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-500">
        {en.home.eyebrow}
      </p>
      <h1 className="mt-2 text-2xl font-semibold tracking-tight">{ProductName}</h1>
      <p className="mt-2 max-w-xl text-sm leading-6 text-slate-600">{en.home.tagline}</p>
      <Button className="mt-6" variant="outline" size="sm" asChild>
        <a href="/health">{en.home.apiHealth}</a>
      </Button>
    </main>
  );
}
