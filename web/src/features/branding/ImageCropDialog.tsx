import { useEffect, useRef, useState } from "react";
import { t } from "@/config/strings";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";

export function ImageCropDialog({
  file,
  onCancel,
  onConfirm,
}: {
  file: File | null;
  onCancel: () => void;
  onConfirm: (blob: Blob) => void;
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const imageRef = useRef<HTMLImageElement | null>(null);
  const [zoom, setZoom] = useState(1);

  useEffect(() => {
    if (!file) return;
    const url = URL.createObjectURL(file);
    const image = new Image();
    image.onload = () => {
      imageRef.current = image;
      draw(image, zoom);
    };
    image.src = url;
    return () => URL.revokeObjectURL(url);
  }, [file, zoom]);

  function draw(image: HTMLImageElement, scaleFactor: number) {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const size = 320;
    canvas.width = size;
    canvas.height = size;
    const context = canvas.getContext("2d");
    if (!context) return;
    const scale = Math.max(size / image.width, size / image.height) * scaleFactor;
    const width = image.width * scale;
    const height = image.height * scale;
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, size, size);
    context.drawImage(image, (size - width) / 2, (size - height) / 2, width, height);
  }

  return (
    <Dialog open={Boolean(file)} onOpenChange={(open) => !open && onCancel()} title={t("branding.cropTitle")}>
      <canvas ref={canvasRef} className="mx-auto h-56 w-56 rounded-md border border-border" />
      <label className="mt-3 grid gap-1 text-[13px]">
        {t("branding.zoom")}
        <input
          type="range"
          min={1}
          max={3}
          step={0.05}
          value={zoom}
          onChange={(event) => {
            const next = Number(event.target.value);
            setZoom(next);
            if (imageRef.current) draw(imageRef.current, next);
          }}
        />
      </label>
      <div className="mt-4 flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>
          {t("common.cancel")}
        </Button>
        <Button
          type="button"
          onClick={() => {
            canvasRef.current?.toBlob((blob) => {
              if (blob) onConfirm(blob);
            }, "image/png");
          }}
        >
          {t("branding.useCrop")}
        </Button>
      </div>
    </Dialog>
  );
}
