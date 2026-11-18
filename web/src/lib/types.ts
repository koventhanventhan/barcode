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

export type PublicBranding = {
  displayName: string;
  tagline: string | null;
  logoUrl: string | null;
  faviconUrl: string | null;
  primaryColor: string;
  accentColor: string;
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
  smsSenderId?: string | null;
  allowCustomSmsSenderId?: boolean;
};

export type Plan = {
  id: string;
  code: string;
  name: string;
  description: string | null;
  priceMonthlyLkr: number | null;
  priceYearlyLkr: number | null;
  setupFeeLkr: number | null;
  extraSmsPerSegmentLkr: number | null;
  extraStoragePerGbLkr: number | null;
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

export type SmsSegmentCalculation = {
  encoding: string;
  isUnicode: boolean;
  characterCount: number;
  segments: number;
  perSegmentLimit: number;
  remainingInCurrentSegment: number;
};

export type TenantNotificationConfig = {
  quietHoursEnabled: boolean;
  quietHoursStartMinutes: number;
  quietHoursEndMinutes: number;
  timeZone: string;
  feeReminderDaysBefore: number;
  feeReminderOnDueDate: boolean;
  feeReminderDaysAfterOverdue: number;
  examReminderDaysBefore: number;
  examReminderHoursBefore: number;
  smsOverQuotaPolicy: "AllowAndRecordExtra" | "Block";
  defaultLanguage: string;
};

export type NotificationTypeConfig = {
  messageType: string;
  parentFeatureKey: string | null;
  isParentFeatureInPlan: boolean;
  isParentFeatureEnabled: boolean;
  isEnabled: boolean;
  isEffectiveEnabled: boolean;
  smsEnabled: boolean;
  emailEnabled: boolean;
  whatsAppEnabled: boolean;
  exemptFromQuietHours: boolean;
  availableVariables: string[];
  smsCategory?: string;
};

export type NotificationTemplate = {
  messageType: string;
  language: string;
  subject: string;
  body: string;
  isCustom: boolean;
  previewSubject: string;
  previewBody: string;
  smsCalculation: SmsSegmentCalculation;
  availableVariables: string[];
};

export type SmsQuotaSummary = {
  usedSegments: number;
  monthlyQuota: number | null;
  remainingSegments: number;
  extraSegments: number;
  usagePercentage: number;
  isWarning80Percent: boolean;
  isExceeded100Percent: boolean;
  overQuotaPolicy: string;
};

export type AllowedNotificationChannels = {
  sms: boolean;
  email: boolean;
  emailInPlan: boolean;
  whatsApp: boolean;
  whatsAppInPlan: boolean;
};

export type NotificationSettingsWorkspace = {
  config: TenantNotificationConfig;
  quota: SmsQuotaSummary;
  allowedChannels: AllowedNotificationChannels;
  messageTypes: NotificationTypeConfig[];
  templates: NotificationTemplate[];
  sampleVariables: Record<string, string>;
  smsSenderId?: string | null;
  canCustomizeSmsSenderId?: boolean;
  promotionalSenderIdConfigured?: boolean;
};

export type SmsGatewayErrorItem = {
  id: string;
  tenantId: string | null;
  provider: string;
  operation: string;
  recipient: string | null;
  errorCode: string;
  errorMessage: string;
  occurredAt: string;
};

export type SuperAdminSmsSettings = {
  provider: "Fake" | "NotifyLk";
  userId: string;
  hasApiKey: boolean;
  apiKeySource: "Database" | "Config" | "None";
  senderId: string;
  promotionalSenderId: string | null;
  baseUrl: string;
  testMode: boolean;
  testAllowList: string[];
  lowBalanceThreshold: number;
  lastLowBalanceAlertSentAt: string | null;
  recentErrors: SmsGatewayErrorItem[];
};

export type SmsGatewayBalance = {
  succeeded: boolean;
  provider: string;
  isActive: boolean | null;
  balance: number | null;
  currency: string;
  isBelowThreshold: boolean;
  lowBalanceThreshold: number;
  checkedAt: string;
  fromCache: boolean;
  errorCode: string | null;
  errorMessage: string | null;
};

export type SuperAdminTestSmsResult = {
  succeeded: boolean;
  isSkipped: boolean;
  skipReason: string | null;
  provider: string;
  providerMessageId: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  segments: number;
  gatewaySegments: number | null;
  gatewayCost: number | null;
};

export type NotificationMessage = {
  id: string;
  messageType: string;
  channel: string;
  status: string;
  language: string;
  recipient: string;
  recipientName: string | null;
  studentId: string | null;
  studentName: string | null;
  studentCode: string | null;
  subject: string | null;
  body: string;
  idempotencyKey: string;
  smsSegments: number;
  extraSegments: number;
  attemptCount: number;
  maxAttempts: number;
  isDeadLetter: boolean;
  createdAt: string;
  nextAttemptAt: string;
  sentAt: string | null;
  deliveredAt: string | null;
  lastAttemptAt: string | null;
  provider: string | null;
  providerMessageId: string | null;
  statusReason: string | null;
  gatewaySegments?: number | null;
  gatewayCost?: number | null;
};

export type NotificationMessageLogResponse = {
  items: NotificationMessage[];
  totalCount: number;
  page: number;
  pageSize: number;
  quota: SmsQuotaSummary;
};

export type TestSendNotificationResult = {
  enqueuedCount: number;
  sentCount: number;
  skippedCount: number;
  failedCount: number;
  skippedReason: string | null;
  messages: NotificationMessage[];
};

