import { t } from "@/config/strings";
import { Check, Lock, ShieldCheck, Smartphone } from "lucide-react";
import { ProductName } from "@/config/brand";
import type { FeatureSlug } from "./catalog";
import { Img } from "./Img";

export function HeroProductCard() {
  return (
    <div className="rounded-lg border border-border bg-card p-3.5 text-foreground shadow-sm">
      <div className="flex items-center justify-between border-b border-border pb-2">
        <div>
          <p className="text-[11px] font-medium uppercase tracking-wider text-muted">{t("marketing.shotInstitute")}</p>
          <p className="font-heading text-sm font-semibold">{t("marketing.shotToday")}</p>
        </div>
        <span className="inline-flex items-center gap-1 rounded-full bg-brand-50 px-2 py-0.5 text-[11px] font-medium text-primary">
          <span className="h-1.5 w-1.5 rounded-full bg-primary" />
          Live
        </span>
      </div>
      <div className="mt-2.5 grid grid-cols-3 gap-2">
        <div className="rounded-md border border-border bg-background px-2.5 py-1.5">
          <p className="text-[11px] text-muted">{t("marketing.shotAttendance")}</p>
          <p className="tabular text-sm font-semibold">92%</p>
        </div>
        <div className="rounded-md border border-border bg-background px-2.5 py-1.5">
          <p className="text-[11px] text-muted">{t("marketing.shotFees")}</p>
          <p className="tabular text-sm font-semibold">LKR 128,500</p>
        </div>
        <div className="rounded-md border border-border bg-background px-2.5 py-1.5">
          <p className="text-[11px] text-muted">{t("marketing.shotSms")}</p>
          <p className="tabular text-sm font-semibold">184 / 1,500</p>
        </div>
      </div>
      <div className="mt-2.5 divide-y divide-border text-xs">
        <div className="flex items-center justify-between py-1.5">
          <span className="font-medium">Nimal Perera · <span className="tabular text-muted">S000001</span></span>
          <span className="rounded bg-brand-50 px-1.5 py-0.5 text-[11px] font-medium text-primary">Present</span>
        </div>
        <div className="flex items-center justify-between py-1.5">
          <span className="font-medium">மீனா ராஜேந்திரன் · <span className="tabular text-muted">S000006</span></span>
          <span className="rounded bg-secondary px-1.5 py-0.5 text-[11px] font-medium text-foreground">Late</span>
        </div>
      </div>
    </div>
  );
}

export function FeatureMockup({ slug }: { slug: FeatureSlug }) {
  if (slug === "parent-portal") {
    return (
      <div className="mx-auto w-full max-w-[300px] rounded-[24px] border-2 border-foreground/15 bg-card p-2.5">
        <div className="mx-auto mb-2 h-1.5 w-14 rounded-full bg-border" />
        <div className="overflow-hidden rounded-[16px] border border-border bg-background">
          <div className="border-b border-border bg-card px-3 py-2 flex items-center justify-between">
            <div>
              <p className="text-xs font-semibold">{ProductName}</p>
              <p className="text-[11px] text-muted">Sunil Perera · +94 77 123 4567</p>
            </div>
            <Smartphone className="h-4 w-4 stroke-[1.5] text-primary" aria-hidden="true" />
          </div>
          <Img
            src="/images/screens/parent-mobile.png"
            alt="EduFlow Parent Portal mobile dashboard for Nimal Perera"
            width={360}
            height={680}
            className="rounded-none border-0"
            imgClassName="max-h-[420px] object-cover object-top"
          />
        </div>
      </div>
    );
  }

  if (slug === "attendance") {
    return (
      <BrowserFrame title="demo.eduflow.lk/app/attendance">
        <Img
          src="/images/screens/scanner.png"
          alt="EduFlow barcode scanner screen marking Nimal Perera present"
          width={1440}
          height={900}
          className="rounded-none border-0"
        />
      </BrowserFrame>
    );
  }

  if (slug === "backup") {
    return (
      <BrowserFrame title="demo.eduflow.lk/app/settings/backups">
        <div className="grid gap-4 bg-background p-5 text-sm">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border pb-3">
            <div>
              <p className="font-heading text-sm font-semibold">Royal Vidyalaya — Archive Snapshots</p>
              <p className="text-xs text-muted">Tenant-scoped JSON snapshot, CSV tables, and uploaded file manifest</p>
            </div>
            <span className="inline-flex items-center gap-1 rounded-md bg-brand-50 px-2.5 py-1 text-xs font-medium text-primary">
              <ShieldCheck className="h-3.5 w-3.5 stroke-[1.5]" aria-hidden="true" />
              AutoBackup ON
            </span>
          </div>
          <div className="grid gap-2 text-xs">
            {[
              { file: "backup-demo-2026-11-17.zip", rows: "1,428 records", status: "Verified" },
              { file: "backup-demo-2026-11-10.zip", rows: "1,390 records", status: "Retained (30d)" },
            ].map((row) => (
              <div key={row.file} className="flex items-center justify-between rounded-md border border-border bg-card px-3 py-2.5">
                <div>
                  <p className="font-mono font-medium">{row.file}</p>
                  <p className="tabular text-muted">{row.rows}</p>
                </div>
                <span className="rounded bg-brand-50 px-2 py-0.5 font-medium text-primary">{row.status}</span>
              </div>
            ))}
          </div>
        </div>
      </BrowserFrame>
    );
  }

  if (slug === "fees") {
    return (
      <BrowserFrame title="demo.eduflow.lk/app/fees">
        <div className="grid gap-4 bg-background p-5 text-sm">
          <div className="grid grid-cols-3 gap-2">
            <div className="rounded-md border border-border bg-card p-3">
              <p className="text-[11px] text-muted">Collected (Nov)</p>
              <p className="tabular mt-1 font-heading text-base font-bold">LKR 128,500</p>
            </div>
            <div className="rounded-md border border-border bg-card p-3">
              <p className="text-[11px] text-muted">Overdue invoices</p>
              <p className="tabular mt-1 font-heading text-base font-bold">4</p>
            </div>
            <div className="rounded-md border border-border bg-card p-3">
              <p className="text-[11px] text-muted">Next receipt</p>
              <p className="tabular mt-1 font-mono text-sm font-semibold">RCP-000143</p>
            </div>
          </div>
          <div className="overflow-x-auto rounded-md border border-border bg-card">
            <table className="w-full text-left text-xs">
              <thead className="border-b border-border bg-secondary/50 text-muted">
                <tr>
                  <th className="px-3 py-2 font-medium">Invoice</th>
                  <th className="px-3 py-2 font-medium">Student</th>
                  <th className="px-3 py-2 font-medium">Amount</th>
                  <th className="px-3 py-2 font-medium">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                <tr>
                  <td className="px-3 py-2 font-mono">INV-000001</td>
                  <td className="px-3 py-2">Nimal Perera</td>
                  <td className="px-3 py-2 tabular">LKR 3,500</td>
                  <td className="px-3 py-2"><span className="rounded bg-brand-50 px-1.5 py-0.5 text-primary">Paid</span></td>
                </tr>
                <tr>
                  <td className="px-3 py-2 font-mono">INV-000002</td>
                  <td className="px-3 py-2">Amaya Silva</td>
                  <td className="px-3 py-2 tabular">LKR 3,150</td>
                  <td className="px-3 py-2"><span className="rounded bg-secondary px-1.5 py-0.5">PayHere</span></td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </BrowserFrame>
    );
  }

  if (slug === "exams") {
    return (
      <BrowserFrame title="demo.eduflow.lk/app/exams">
        <div className="grid gap-4 bg-background p-5 text-sm">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <p className="font-heading text-sm font-semibold">First Term Test · 2026 O/L Combined Maths</p>
              <p className="text-xs text-muted">Published · Level Rank enabled</p>
            </div>
            <span className="rounded bg-brand-50 px-2 py-0.5 text-xs font-medium text-primary">Report Cards PDF</span>
          </div>
          <div className="overflow-x-auto rounded-md border border-border bg-card">
            <table className="w-full text-left text-xs">
              <thead className="border-b border-border bg-secondary/50 text-muted">
                <tr>
                  <th className="px-3 py-2 font-medium">Rank</th>
                  <th className="px-3 py-2 font-medium">Student</th>
                  <th className="px-3 py-2 font-medium">Marks</th>
                  <th className="px-3 py-2 font-medium">Grade</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border tabular">
                <tr>
                  <td className="px-3 py-2 font-semibold">#1</td>
                  <td className="px-3 py-2 font-sans">Nimal Perera (S000001)</td>
                  <td className="px-3 py-2">86 / 100</td>
                  <td className="px-3 py-2 font-semibold text-primary">A</td>
                </tr>
                <tr>
                  <td className="px-3 py-2 font-semibold">#2</td>
                  <td className="px-3 py-2 font-sans">Amaya Silva (S000002)</td>
                  <td className="px-3 py-2">82 / 100</td>
                  <td className="px-3 py-2 font-semibold text-primary">A</td>
                </tr>
                <tr>
                  <td className="px-3 py-2 font-semibold">#3</td>
                  <td className="px-3 py-2 font-sans">කසුන් ජයවර්ධන (S000003)</td>
                  <td className="px-3 py-2">78 / 100</td>
                  <td className="px-3 py-2 font-semibold text-primary">A</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </BrowserFrame>
    );
  }

  return (
    <BrowserFrame title="demo.eduflow.lk/app/settings/features">
      <div className="grid gap-3 bg-background p-5 text-sm">
        <div className="flex items-center justify-between border-b border-border pb-2">
          <p className="font-heading text-sm font-semibold">Feature & Notification Controls</p>
          <span className="tabular text-xs text-muted">Quiet hours 21:00–06:00 LKT</span>
        </div>
        <div className="grid gap-2 text-xs">
          {[
            { name: "Attendance SMS alerts", on: true, locked: false },
            { name: "Fee reminder SMS (3 days before)", on: true, locked: false },
            { name: "Parent Portal mobile access", on: true, locked: false },
            { name: "WhatsApp Cloud API messaging", on: false, locked: true },
          ].map((item) => (
            <div key={item.name} className="flex items-center justify-between rounded-md border border-border bg-card px-3 py-2">
              <span className="font-medium">{item.name}</span>
              {item.locked ? (
                <span className="inline-flex items-center gap-1 text-muted">
                  <Lock className="h-3.5 w-3.5 stroke-[1.5]" aria-hidden="true" />
                  Enterprise
                </span>
              ) : (
                <span className="inline-flex items-center gap-1 rounded bg-brand-50 px-2 py-0.5 font-medium text-primary">
                  <Check className="h-3.5 w-3.5 stroke-[1.5]" aria-hidden="true" />
                  ON
                </span>
              )}
            </div>
          ))}
        </div>
      </div>
    </BrowserFrame>
  );
}

function BrowserFrame({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <figure className="overflow-hidden rounded-lg border border-border bg-card">
      <div className="flex items-center gap-2 border-b border-border bg-secondary/60 px-3 py-2">
        <span className="h-2.5 w-2.5 rounded-full bg-border" />
        <span className="h-2.5 w-2.5 rounded-full bg-border" />
        <span className="h-2.5 w-2.5 rounded-full bg-border" />
        <span className="ml-2 truncate font-mono text-[11px] text-muted">{title}</span>
      </div>
      {children}
    </figure>
  );
}
