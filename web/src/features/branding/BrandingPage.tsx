import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormField, fieldError } from "@/components/form-field";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { applyBrandToDocument, brandPresets, resolveThemeMode } from "@/lib/color";
import { api } from "@/lib/api";
import type { Branding } from "@/lib/types";
import { useBranding } from "./BrandingProvider";
import { ImageCropDialog } from "./ImageCropDialog";

const schema = z.object({
  instituteDisplayName: z.string().min(2).max(200),
  tagline: z.string().max(300),
  primaryColor: z.string().regex(/^#[0-9A-Fa-f]{6}$/),
  accentColor: z.string().regex(/^#[0-9A-Fa-f]{6}$/),
  fontPreference: z.enum(["Inter", "System"]),
  themeMode: z.enum(["light", "dark", "system"]),
  removeProductBranding: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

export function BrandingPage({ platform = false }: { platform?: boolean }) {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const live = useBranding();
  const [resetOpen, setResetOpen] = useState(false);
  const [cropFile, setCropFile] = useState<File | null>(null);
  const [cropKind, setCropKind] = useState<"logo" | "favicon" | "background">("logo");
  const base = platform ? "/api/super-admin/branding" : "/api/settings/branding";
  const query = useQuery({
    queryKey: ["branding-edit", platform],
    queryFn: () => api<Branding>(base),
  });
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      instituteDisplayName: "",
      tagline: "",
      primaryColor: "#1F4E46",
      accentColor: "#C45C26",
      fontPreference: "Inter",
      themeMode: "light",
      removeProductBranding: false,
    },
    values: query.data
      ? {
          instituteDisplayName: query.data.instituteDisplayName,
          tagline: query.data.tagline ?? "",
          primaryColor: query.data.primaryColor,
          accentColor: query.data.accentColor,
          fontPreference: query.data.fontPreference === "System" ? "System" : "Inter",
          themeMode:
            query.data.themeMode === "dark" || query.data.themeMode === "system"
              ? query.data.themeMode
              : "light",
          removeProductBranding: query.data.removeProductBranding,
        }
      : undefined,
  });
  const values = form.watch();

  function preview(values: FormValues) {
    const mode = live.themeOverride ?? resolveThemeMode(values.themeMode, window.matchMedia("(prefers-color-scheme: dark)").matches);
    try {
      applyBrandToDocument({
        primaryColor: values.primaryColor,
        accentColor: values.accentColor,
        fontPreference: values.fontPreference,
        mode,
      });
    } catch {
      /* wait until the hex is complete */
    }
  }

  const save = useMutation({
    mutationFn: (values: FormValues) =>
      api<Branding>(base, {
        method: "PUT",
        body: JSON.stringify({
          ...values,
          tagline: values.tagline || null,
          logoUrl: query.data?.logoUrl ?? null,
          faviconUrl: query.data?.faviconUrl ?? null,
          loginBackgroundUrl: query.data?.loginBackgroundUrl ?? null,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("branding.saved"));
      await queryClient.invalidateQueries({ queryKey: ["public-branding"] });
      await queryClient.invalidateQueries({ queryKey: ["branding-edit"] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  const reset = useMutation({
    mutationFn: () => api<Branding>(`${base}/reset`, { method: "POST" }),
    onSuccess: async () => {
      toast.success(t("branding.resetDone"));
      setResetOpen(false);
      await queryClient.invalidateQueries({ queryKey: ["public-branding"] });
      await queryClient.invalidateQueries({ queryKey: ["branding-edit"] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  async function upload(kind: "logo" | "favicon" | "background", blob: Blob) {
    const body = new FormData();
    body.append("file", blob, `${kind}.png`);
    body.append("kind", kind);
    await api<Branding>(`${base}/assets`, { method: "POST", body });
    toast.success(t("branding.saved"));
    await queryClient.invalidateQueries({ queryKey: ["public-branding"] });
    await queryClient.invalidateQueries({ queryKey: ["branding-edit"] });
  }

  if (query.isLoading || !query.data) return <PageSkeleton />;

  return (
    <div>
      <PageHeader
        title={platform ? t("branding.platformTitle") : t("branding.title")}
        description={platform ? t("branding.platformDescription") : t("branding.description")}
        crumbs={[{ label: t("nav.settings") }, { label: t("nav.branding") }]}
        actions={
          <Button type="button" variant="outline" onClick={() => setResetOpen(true)}>
            {t("common.reset")}
          </Button>
        }
      />
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
        <form className="grid gap-4 rounded-lg border border-border bg-card p-4" onSubmit={form.handleSubmit((next) => save.mutate(next))}>
          <FormField label={t("branding.displayName")} htmlFor="displayName" error={fieldError(form, "instituteDisplayName")}>
            <Input id="displayName" {...form.register("instituteDisplayName")} />
          </FormField>
          <FormField label={t("branding.tagline")} htmlFor="tagline" error={fieldError(form, "tagline")}>
            <Input id="tagline" {...form.register("tagline")} />
          </FormField>
          <div className="grid gap-3 sm:grid-cols-2">
            <ColorField
              label={t("branding.primary")}
              value={values.primaryColor}
              onChange={(color) => {
                form.setValue("primaryColor", color, { shouldValidate: true });
                preview({ ...form.getValues(), primaryColor: color });
              }}
              error={fieldError(form, "primaryColor")}
            />
            <ColorField
              label={t("branding.accent")}
              value={values.accentColor}
              onChange={(color) => {
                form.setValue("accentColor", color, { shouldValidate: true });
                preview({ ...form.getValues(), accentColor: color });
              }}
              error={fieldError(form, "accentColor")}
            />
          </div>
          <div>
            <p className="mb-2 text-[13px] font-medium">{t("branding.presets")}</p>
            <div className="flex flex-wrap gap-2">
              {brandPresets.map((preset) => (
                <button
                  key={preset.id}
                  type="button"
                  className="inline-flex items-center gap-2 rounded-md border border-border px-2 py-1 text-xs"
                  onClick={() => {
                    form.setValue("primaryColor", preset.primary);
                    form.setValue("accentColor", preset.accent);
                    preview({ ...form.getValues(), primaryColor: preset.primary, accentColor: preset.accent });
                  }}
                >
                  <span className="h-4 w-4 rounded-sm" style={{ background: preset.primary }} />
                  <span className="h-4 w-4 rounded-sm" style={{ background: preset.accent }} />
                  {t(`branding.preset.${preset.id}`, { defaultValue: preset.id })}
                </button>
              ))}
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <FormField label={t("branding.font")} htmlFor="font">
              <select id="font" className="h-9 rounded-md border border-border bg-card px-2" {...form.register("fontPreference")}>
                <option value="Inter">{t("branding.fontInter")}</option>
                <option value="System">{t("branding.fontSystem")}</option>
              </select>
            </FormField>
            <FormField label={t("branding.theme")} htmlFor="theme">
              <select id="theme" className="h-9 rounded-md border border-border bg-card px-2" {...form.register("themeMode")}>
                <option value="light">{t("branding.themeLight")}</option>
                <option value="dark">{t("branding.themeDark")}</option>
                <option value="system">{t("branding.themeSystem")}</option>
              </select>
            </FormField>
          </div>
          <label className="flex items-start gap-2 text-sm">
            <input
              type="checkbox"
              className="mt-1"
              disabled={!query.data.canRemoveProductBranding}
              {...form.register("removeProductBranding")}
            />
            <span>
              {t("branding.removeBranding")}
              <span className="mt-0.5 block text-xs text-muted">{t("branding.removeBrandingHint")}</span>
            </span>
          </label>
          <div className="grid gap-3 sm:grid-cols-3">
            <AssetButton label={t("branding.logo")} url={query.data.logoUrl} onPick={(file) => { setCropKind("logo"); setCropFile(file); }} />
            <AssetButton label={t("branding.favicon")} url={query.data.faviconUrl} onPick={(file) => { setCropKind("favicon"); setCropFile(file); }} />
            <AssetButton label={t("branding.background")} url={query.data.loginBackgroundUrl} onPick={(file) => { setCropKind("background"); setCropFile(file); }} />
          </div>
          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? t("common.saving") : t("common.save")}
          </Button>
        </form>
        <aside className="rounded-lg border border-border bg-card p-4">
          <p className="text-xs font-medium uppercase tracking-wide text-muted">{t("branding.preview")}</p>
          <div className="mt-3 overflow-hidden rounded-md border border-border">
            <div className="px-3 py-4 text-primary-foreground" style={{ background: values.primaryColor }}>
              <p className="text-sm font-semibold">{values.instituteDisplayName}</p>
              {values.tagline ? <p className="mt-1 text-xs opacity-90">{values.tagline}</p> : null}
            </div>
            <div className="grid gap-2 p-3">
              <button type="button" className="h-9 rounded-md text-sm text-primary-foreground" style={{ background: values.primaryColor }}>
                {t("branding.previewButton")}
              </button>
              <button type="button" className="h-9 rounded-md text-sm text-white" style={{ background: values.accentColor }}>
                {t("branding.accent")}
              </button>
            </div>
          </div>
        </aside>
      </div>
      <ImageCropDialog
        file={cropFile}
        onCancel={() => setCropFile(null)}
        onConfirm={(blob) => {
          setCropFile(null);
          void upload(cropKind, blob).catch((error: unknown) => {
            toast.error(error instanceof Error ? error.message : t("errors.generic"));
          });
        }}
      />
      <ConfirmDialog
        open={resetOpen}
        title={t("common.reset")}
        description={t("branding.resetConfirm")}
        confirmLabel={t("common.reset")}
        pending={reset.isPending}
        onOpenChange={setResetOpen}
        onConfirm={() => reset.mutate()}
      />
    </div>
  );
}

function ColorField({
  label,
  value,
  onChange,
  error,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
}) {
  return (
    <FormField label={label} error={error}>
      <div className="flex gap-2">
        <input aria-label={label} type="color" value={safeColor(value)} onChange={(event) => onChange(event.target.value.toUpperCase())} className="h-9 w-12 rounded-md border border-border bg-card" />
        <Input value={value} onChange={(event) => onChange(event.target.value)} />
      </div>
    </FormField>
  );
}

function AssetButton({ label, url, onPick }: { label: string; url: string | null; onPick: (file: File) => void }) {
  const { t } = useTranslation();
  return (
    <label className="grid gap-1 text-[13px]">
      <span className="font-medium">{label}</span>
      {url ? <img src={url} alt="" className="h-12 w-full rounded-md border border-border object-contain" /> : null}
      <span className="inline-flex h-8 cursor-pointer items-center justify-center rounded-md border border-border">{t("branding.upload")}</span>
      <input
        type="file"
        accept="image/png,image/jpeg,image/webp"
        className="sr-only"
        onChange={(event) => {
          const file = event.target.files?.[0];
          if (file) onPick(file);
          event.target.value = "";
        }}
      />
    </label>
  );
}

function safeColor(value: string): string {
  return /^#[0-9A-Fa-f]{6}$/.test(value) ? value : "#1F4E46";
}
