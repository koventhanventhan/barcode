import { useEffect, useState } from "react";
import { monogramFor } from "@/lib/subdomain";
import { cn } from "@/lib/utils";

export function BrandLogo({
  logoUrl,
  displayName,
  size = "sm",
  inverted = false,
  className,
}: {
  logoUrl?: string | null;
  displayName: string;
  size?: "sm" | "md" | "lg";
  inverted?: boolean;
  className?: string;
}) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null);

  useEffect(() => {
    setFailedUrl(null);
  }, [logoUrl]);

  const showImage = Boolean(logoUrl && failedUrl !== logoUrl);
  const monogram = monogramFor(displayName);

  if (showImage && logoUrl) {
    const sizeClass =
      size === "lg"
        ? "max-h-40 w-auto max-w-full object-contain"
        : size === "md"
          ? "h-9 w-9 rounded-md object-contain"
          : "h-7 w-7 rounded-md object-contain";

    return (
      <img
        src={logoUrl}
        alt={`${displayName} logo`}
        className={cn(sizeClass, className)}
        onError={() => setFailedUrl(logoUrl)}
      />
    );
  }

  const badgeClass =
    size === "lg"
      ? "h-20 w-20 rounded-lg text-2xl font-bold tracking-tight"
      : size === "md"
        ? "h-9 w-9 rounded-md text-xs font-bold tracking-tight"
        : "h-7 w-7 rounded-md text-[11px] font-bold tracking-tight";

  return (
    <span
      aria-label={`${displayName} monogram`}
      className={cn(
        "inline-flex shrink-0 items-center justify-center select-none",
        inverted
          ? "border border-primary-foreground/25 bg-[color-mix(in_srgb,var(--primary-foreground)_14%,var(--primary))] text-primary-foreground"
          : "bg-primary text-primary-foreground",
        badgeClass,
        className,
      )}
    >
      {monogram}
    </span>
  );
}
