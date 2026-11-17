import { useState } from "react";
import { ImageOff } from "lucide-react";
import { cn } from "@/lib/utils";

type ImgProps = {
  src: string;
  alt: string;
  width: number;
  height: number;
  className?: string;
  imgClassName?: string;
  position?: string;
  priority?: boolean;
};

export function Img({
  src,
  alt,
  width,
  height,
  className,
  imgClassName,
  position = "center center",
  priority = false,
}: ImgProps) {
  const [failed, setFailed] = useState(false);
  const base = src.replace(/\.(jpe?g|png|webp|avif)$/i, "");
  const isRaster = /\.(jpe?g|png|webp|avif)$/i.test(src);

  if (failed) {
    return (
      <div
        role="img"
        aria-label={alt}
        style={{ aspectRatio: `${width} / ${height}` }}
        className={cn(
          "flex flex-col items-center justify-center gap-2 rounded-lg border border-border bg-secondary/70 p-6 text-center text-muted",
          className,
        )}
      >
        <ImageOff className="h-6 w-6 stroke-[1.5]" aria-hidden="true" />
        <span className="max-w-xs text-xs leading-5">{alt}</span>
      </div>
    );
  }

  return (
    <picture className={cn("block overflow-hidden rounded-lg border border-border bg-secondary/40", className)}>
      {isRaster ? <source srcSet={`${base}.avif`} type="image/avif" /> : null}
      {isRaster ? <source srcSet={`${base}.webp`} type="image/webp" /> : null}
      <img
        src={src}
        alt={alt}
        width={width}
        height={height}
        loading={priority ? "eager" : "lazy"}
        decoding={priority ? "sync" : "async"}
        style={{ objectPosition: position }}
        onError={() => setFailed(true)}
        className={cn("block h-auto w-full object-cover", imgClassName)}
      />
    </picture>
  );
}
