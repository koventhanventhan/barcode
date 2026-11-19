import { forwardRef, useMemo } from "react";

export type QrMatrix = {
  size: number;
  modules: string;
};

export const QrCodeSvg = forwardRef<
  SVGSVGElement,
  {
    qr: QrMatrix;
    label: string;
    className?: string;
  }
>(function QrCodeSvg({ qr, label, className = "h-44 w-44" }, ref) {
  const rects = useMemo(() => {
    const items: { x: number; y: number }[] = [];
    for (let y = 0; y < qr.size; y++) {
      const offset = y * qr.size;
      for (let x = 0; x < qr.size; x++) {
        if (qr.modules[offset + x] === "1") {
          items.push({ x, y });
        }
      }
    }
    return items;
  }, [qr.modules, qr.size]);

  return (
    <svg
      ref={ref}
      viewBox={`0 0 ${qr.size} ${qr.size}`}
      role="img"
      aria-label={label}
      shapeRendering="crispEdges"
      className={className}
    >
      <rect width={qr.size} height={qr.size} fill="#ffffff" />
      {rects.map((cell) => (
        <rect key={`${cell.x}-${cell.y}`} x={cell.x} y={cell.y} width={1} height={1} fill="#000000" />
      ))}
    </svg>
  );
});
