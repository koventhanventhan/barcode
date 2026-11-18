import { Helmet } from "react-helmet-async";
import { ProductName } from "@/config/brand";

type MarketingMetaProps = {
  title: string;
  description: string;
  path: string;
};

export function MarketingMeta({ title, description, path }: MarketingMetaProps) {
  const fullTitle = title.includes(ProductName) ? title : `${title} — ${ProductName}`;
  const canonical = `https://admino.lk${path === "/" ? "/" : path}`;

  return (
    <Helmet>
      <title>{fullTitle}</title>
      <meta name="description" content={description} />
      <link rel="canonical" href={canonical} />
      <meta property="og:title" content={fullTitle} />
      <meta property="og:description" content={description} />
      <meta property="og:type" content="website" />
      <meta property="og:url" content={canonical} />
      <meta property="og:image" content="https://admino.lk/og.svg" />
    </Helmet>
  );
}
