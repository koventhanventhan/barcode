export type FeatureSlug =
  | "attendance"
  | "fees"
  | "exams"
  | "messaging"
  | "parent-portal"
  | "backup";

export type FeaturePageInfo = {
  slug: FeatureSlug;
  number: string;
  image: string;
  imageAlt: string;
  imagePosition?: string;
  imageFirstOnMobile?: boolean;
  nextSlug: FeatureSlug;
};

export const FEATURE_PAGES: readonly FeaturePageInfo[] = [
  {
    slug: "attendance",
    number: "01",
    image: "/images/barcode-scan.jpg",
    imageAlt: "Student presenting a printed barcode ID card at a Sri Lankan institute reception desk.",
    imagePosition: "center 35%",
    nextSlug: "fees",
  },
  {
    slug: "fees",
    number: "02",
    image: "/images/admin-office.jpg",
    imageAlt: "Tuition institute front desk with receipt books and fee administration workspace.",
    imagePosition: "center center",
    nextSlug: "exams",
  },
  {
    slug: "exams",
    number: "03",
    image: "/images/exam-desk.jpg",
    imageAlt: "Printed O/L mathematics exam papers and mark ledger on a wooden desk.",
    imagePosition: "center center",
    nextSlug: "messaging",
  },
  {
    slug: "messaging",
    number: "04",
    image: "/images/hero-tuition.jpg",
    imageAlt: "Teacher explaining mathematics graphs to a batch of tuition students in Jaffna.",
    imagePosition: "center 30%",
    nextSlug: "parent-portal",
  },
  {
    slug: "parent-portal",
    number: "05",
    image: "/images/parent-phone.jpg",
    imageAlt: "Parent in Colombo reviewing child attendance and fee status on a mobile phone.",
    imagePosition: "center 30%",
    nextSlug: "backup",
  },
  {
    slug: "backup",
    number: "06",
    image: "/images/admin-office.jpg",
    imageAlt: "Organized institute archive and administration desk protected by scheduled backups.",
    imagePosition: "center center",
    nextSlug: "attendance",
  },
];

export function findFeaturePage(slug: string | undefined): FeaturePageInfo | undefined {
  return FEATURE_PAGES.find((item) => item.slug === slug);
}
