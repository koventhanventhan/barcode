import { api } from "@/lib/api";

export const SUPPORT_TICKET_CATEGORIES = [
  "General",
  "Technical",
  "Billing",
  "BackupRestore",
  "FeatureRequest",
] as const;

export const SUPPORT_TICKET_STATUSES = [
  "Open",
  "InProgress",
  "WaitingOnInstitute",
  "Resolved",
  "Closed",
] as const;

export type SupportTicketCategory = (typeof SUPPORT_TICKET_CATEGORIES)[number];
export type SupportTicketStatus = (typeof SUPPORT_TICKET_STATUSES)[number];

export type SupportTicketMessageDto = {
  id: string;
  senderUserId: string;
  senderName: string;
  senderRole: string;
  isFromSuperAdmin: boolean;
  body: string;
  createdAt: string;
};

export type SupportTicketSummaryDto = {
  id: string;
  ticketNumber: string;
  subject: string;
  category: SupportTicketCategory;
  status: SupportTicketStatus;
  isPriority: boolean;
  createdByName: string;
  createdByEmail: string;
  messageCount: number;
  lastMessagePreview: string;
  lastMessageAt: string;
  resolvedAt: string | null;
  closedAt: string | null;
  createdAt: string;
};

export type SupportTicketDetailDto = {
  id: string;
  ticketNumber: string;
  subject: string;
  category: SupportTicketCategory;
  status: SupportTicketStatus;
  isPriority: boolean;
  createdByName: string;
  createdByEmail: string;
  lastMessageAt: string;
  resolvedAt: string | null;
  closedAt: string | null;
  createdAt: string;
  messages: SupportTicketMessageDto[];
};

export type SupportTicketPageDto = {
  items: SupportTicketSummaryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export type SuperAdminSupportTicketSummaryDto = SupportTicketSummaryDto & {
  tenantId: string;
  tenantName: string;
  tenantSubdomain: string;
  planName: string | null;
};

export type SuperAdminSupportTicketDetailDto = SupportTicketDetailDto & {
  tenantId: string;
  tenantName: string;
  tenantSubdomain: string;
  planName: string | null;
};

export type SuperAdminSupportInboxDto = {
  openCount: number;
  priorityOpenCount: number;
  waitingCount: number;
  resolvedCount: number;
  items: SuperAdminSupportTicketSummaryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export function getTenantSupportTickets(params: { status?: string; page: number; pageSize: number }) {
  const qs = new URLSearchParams();
  if (params.status) qs.set("status", params.status);
  qs.set("page", String(params.page));
  qs.set("pageSize", String(params.pageSize));
  return api<SupportTicketPageDto>(`/api/support/tickets?${qs.toString()}`);
}

export function getTenantSupportTicket(id: string) {
  return api<SupportTicketDetailDto>(`/api/support/tickets/${id}`);
}

export function createSupportTicket(body: { subject: string; body: string; category: SupportTicketCategory }) {
  return api<SupportTicketDetailDto>("/api/support/tickets", {
    method: "POST",
    body: JSON.stringify(body),
  });
}

export function replySupportTicket(id: string, body: string) {
  return api<SupportTicketDetailDto>(`/api/support/tickets/${id}/reply`, {
    method: "POST",
    body: JSON.stringify({ body }),
  });
}

export function closeSupportTicket(id: string) {
  return api<SupportTicketDetailDto>(`/api/support/tickets/${id}/close`, {
    method: "POST",
  });
}

export function getSuperAdminSupportInbox(params: {
  status?: string;
  priorityOnly?: boolean;
  tenantId?: string;
  search?: string;
  page: number;
  pageSize: number;
}) {
  const qs = new URLSearchParams();
  if (params.status) qs.set("status", params.status);
  if (params.priorityOnly) qs.set("priorityOnly", "true");
  if (params.tenantId) qs.set("tenantId", params.tenantId);
  if (params.search?.trim()) qs.set("search", params.search.trim());
  qs.set("page", String(params.page));
  qs.set("pageSize", String(params.pageSize));
  return api<SuperAdminSupportInboxDto>(`/api/super-admin/support/tickets?${qs.toString()}`);
}

export function getSuperAdminSupportTicket(id: string) {
  return api<SuperAdminSupportTicketDetailDto>(`/api/super-admin/support/tickets/${id}`);
}

export function replySuperAdminSupportTicket(
  id: string,
  body: { body: string; status?: SupportTicketStatus | null },
) {
  return api<SuperAdminSupportTicketDetailDto>(`/api/super-admin/support/tickets/${id}/reply`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

export function updateSuperAdminSupportTicketStatus(id: string, status: SupportTicketStatus) {
  return api<SuperAdminSupportTicketDetailDto>(`/api/super-admin/support/tickets/${id}/status`, {
    method: "PUT",
    body: JSON.stringify({ status }),
  });
}
