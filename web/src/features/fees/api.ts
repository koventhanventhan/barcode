export type FeeFrequency = "Monthly" | "OneTime" | "Admission";
export type DiscountType = "Percentage" | "FixedAmount";
export type FeeInvoiceStatus = "Pending" | "PartiallyPaid" | "Paid" | "Voided";
export type PaymentMethod = "Cash" | "BankTransfer" | "PayHere";

export type FeeStructureDto = {
  id: string;
  name: string;
  frequency: FeeFrequency;
  amount: number;
  currency: string;
  educationLevelId: string | null;
  educationLevelName: string | null;
  batchId: string | null;
  batchName: string | null;
  dueDayOfMonth: number;
  description: string | null;
  isActive: boolean;
  createdAt: string;
};

export type StudentConcessionDto = {
  id: string;
  studentId: string;
  studentCode: string;
  studentName: string;
  feeStructureId: string | null;
  feeStructureName: string | null;
  name: string;
  discountType: DiscountType;
  value: number;
  reason: string | null;
  validFrom: string | null;
  validTo: string | null;
  isActive: boolean;
  createdAt: string;
};

export type FeePaymentDto = {
  id: string;
  invoiceId: string;
  invoiceNumber: string;
  studentId: string;
  studentCode: string;
  studentName: string;
  receiptSequence: number;
  receiptNumber: string;
  amount: number;
  refundedAmount: number;
  effectiveAmount: number;
  currency: string;
  method: PaymentMethod;
  status: string;
  referenceNumber: string | null;
  attachmentUrl: string | null;
  paidAt: string;
  notes: string | null;
  voidedOrRefundedAt: string | null;
  voidOrRefundReason: string | null;
  createdAt: string;
};

export type FeeInvoiceDto = {
  id: string;
  invoiceNumber: string;
  studentId: string;
  studentCode: string;
  studentName: string;
  guardianName: string;
  guardianPhone: string;
  feeStructureId: string;
  feeStructureName: string;
  frequency: FeeFrequency;
  batchId: string | null;
  batchName: string | null;
  educationLevelId: string | null;
  educationLevelName: string | null;
  billingPeriod: string;
  title: string;
  grossAmount: number;
  discountAmount: number;
  concessionId: string | null;
  concessionDescription: string | null;
  netAmount: number;
  paidAmount: number;
  balanceAmount: number;
  currency: string;
  issueDate: string;
  dueDate: string;
  status: FeeInvoiceStatus;
  isOverdue: boolean;
  daysOverdue: number;
  paidAt: string | null;
  voidedAt: string | null;
  voidReason: string | null;
  notes: string | null;
  payments: FeePaymentDto[];
  createdAt: string;
};

export type FeeInvoicePageDto = {
  items: FeeInvoiceDto[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export type GenerateInvoicesResultDto = {
  billingPeriod: string;
  createdCount: number;
  skippedExistingCount: number;
  totalNetAmount: number;
  createdInvoices: FeeInvoiceDto[];
};

export type RunFeeRemindersResultDto = {
  evaluatedDate: string;
  checkedInvoices: number;
  beforeDueSent: number;
  onDueSent: number;
  overdueSent: number;
  skippedCount: number;
};

export type OverdueAgingBucketDto = {
  bucket: string;
  invoiceCount: number;
  balanceAmount: number;
};

export type BatchFeeSummaryDto = {
  batchId: string | null;
  batchName: string;
  invoiceCount: number;
  expectedAmount: number;
  collectedAmount: number;
  outstandingAmount: number;
  collectionRatePercent: number;
};

export type DefaulterStudentDto = {
  studentId: string;
  studentCode: string;
  studentName: string;
  guardianName: string;
  guardianPhone: string;
  batchNames: string;
  overdueInvoiceCount: number;
  totalOverdueBalance: number;
  oldestDueDate: string;
  maxDaysOverdue: number;
};

export type FeeDashboardDto = {
  expectedTotal: number;
  collectedTotal: number;
  outstandingTotal: number;
  overdueTotal: number;
  collectionRatePercent: number;
  paidInvoiceCount: number;
  partiallyPaidInvoiceCount: number;
  pendingInvoiceCount: number;
  overdueInvoiceCount: number;
  overdueAging: OverdueAgingBucketDto[];
  byBatch: BatchFeeSummaryDto[];
  topDefaulters: DefaulterStudentDto[];
  recentPayments: FeePaymentDto[];
};

export type PayHereGatewaySettingsDto = {
  merchantId: string;
  hasMerchantSecret: boolean;
  maskedMerchantSecret: string | null;
  isSandbox: boolean;
  isEnabled: boolean;
  notifyUrlOverride: string | null;
  returnUrlOverride: string | null;
  cancelUrlOverride: string | null;
  isOnlinePaymentsFeatureEnabled: boolean;
  isOnlinePaymentsInPlan: boolean;
};

export type PayHereCheckoutSessionDto = {
  sessionId: string;
  orderId: string;
  invoiceId: string;
  invoiceNumber: string;
  invoiceTitle: string;
  studentId: string;
  studentName: string;
  studentCode: string;
  amount: number;
  invoiceNetAmount: number;
  invoiceBalanceAmount: number;
  currency: string;
  isSandbox: boolean;
  checkoutUrl: string;
  status: string;
  payHerePaymentId: string | null;
  feePaymentId: string | null;
  receiptNumber: string | null;
  formFields: Record<string, string>;
  createdAt: string;
  completedAt: string | null;
};

export type PayHereWebhookResultDto = {
  orderId: string;
  outcome: string;
  wasReplay: boolean;
  status: string;
  invoiceId: string | null;
  invoiceStatus: string | null;
  paymentId: string | null;
  receiptNumber: string | null;
};

export type PlatformSubscriptionInvoiceDto = {
  id: string;
  tenantId: string;
  tenantName: string;
  tenantSubdomain: string;
  invoiceNumber: string;
  planId: string;
  planName: string;
  amount: number;
  currency: string;
  periodMonths: number;
  daysToExtend: number;
  issueDate: string;
  dueDate: string;
  status: string;
  paidAt: string | null;
  paymentReference: string | null;
  notes: string | null;
  createdAt: string;
};

export function formatMoney(amount: number, currency = "LKR"): string {
  return `${currency} ${Number(amount || 0).toLocaleString("en-US", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}
