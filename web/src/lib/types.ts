export type RoleName = "SuperAdmin" | "InstituteAdmin" | "Staff" | "Teacher" | "Parent";

export type AuthUser = {
  id: string;
  tenantId: string | null;
  email: string;
  fullName: string;
  role: RoleName;
  permissions: string[];
  mustChangePassword: boolean;
};

export type AuthResponse = {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
};

export type MeResponse = {
  userId: string;
  tenantId: string | null;
  email: string;
  role: RoleName;
  isImpersonating: boolean;
  isReadOnly: boolean;
  permissions: string[];
};

export type Branding = {
  primaryColor: string;
  accentColor: string;
  logoUrl: string | null;
  faviconUrl: string | null;
  loginBackgroundUrl: string | null;
  instituteDisplayName: string;
  tagline: string | null;
  fontPreference: string;
  themeMode: string;
  removeProductBranding: boolean;
  canRemoveProductBranding: boolean;
  subdomain: string | null;
  isPlatformDefault: boolean;
};

export type FeatureStatus = {
  featureKey: string;
  isInPlan: boolean;
  isOverridden: boolean;
  isTenantEnabled: boolean;
  isEffectiveEnabled: boolean;
  updatedAt: string | null;
};

export type LimitUsage = {
  limitKey: string;
  currentCount: number;
  maxAllowed: number | null;
  isUnlimited: boolean;
};

export type SubscriptionSummary = {
  subscriptionId: string;
  planCode: string;
  planName: string;
  status: string;
  startDate: string;
  endDate: string | null;
  trialEndsAt: string | null;
  isReadOnly: boolean;
  features: FeatureStatus[];
  limits: LimitUsage[];
};

export type Branch = {
  id: string;
  name: string;
  code: string;
  isActive: boolean;
};

export type TenantSummary = {
  id: string;
  name: string;
  subdomain: string;
  contactEmail: string | null;
  contactPhone: string | null;
  isActive: boolean;
  isDemo: boolean;
  planCode: string | null;
  planName: string | null;
  subscriptionStatus: string | null;
  subscriptionEndDate: string | null;
  createdAt: string;
};

export type FeatureOverride = {
  id: string;
  featureKey: string;
  isEnabled: boolean;
  reason: string | null;
  createdAt: string;
};

export type Subscription = {
  id: string;
  planId: string;
  planCode: string;
  planName: string;
  status: string;
  startDate: string;
  endDate: string | null;
  trialEndsAt: string | null;
  suspendedReason: string | null;
  extraStudentsAllowed: number;
};

export type TenantDetail = TenantSummary & {
  address: string | null;
  timeZone: string;
  subscription: Subscription | null;
  featureOverrides: FeatureOverride[];
  usersCount: number;
  branchesCount: number;
};

export type Plan = {
  id: string;
  code: string;
  name: string;
  description: string | null;
  priceMonthly: number | null;
  currency: string;
  isActive: boolean;
  sortOrder: number;
  features: string[];
  limits: Record<string, number | null>;
};

export type ApiErrorBody = {
  code?: string;
  message?: string;
  feature?: string;
  title?: string;
  detail?: string;
  limit?: string;
  max?: number;
};
