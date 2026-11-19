import type { QrMatrix } from "@/components/qr-code";
import { api } from "@/lib/api";

export type StaffRegistrationLinkDto = {
  hasLink: boolean;
  isEnabled: boolean;
  token: string | null;
  registrationUrl: string | null;
  qr: QrMatrix | null;
  defaultBranchId: string | null;
  updatedAt: string | null;
};

export type StaffRegistrationRequestDto = {
  id: string;
  fullName: string;
  email: string;
  phone: string | null;
  requestedRoleType: "Staff" | "Teacher";
  branchId: string | null;
  branchName: string | null;
  notes: string | null;
  status: "Pending" | "Approved" | "Rejected";
  reviewedByUserId: string | null;
  reviewedAt: string | null;
  reviewNotes: string | null;
  createdUserId: string | null;
  createdAt: string;
};

export type StaffRegistrationPageDto = {
  items: StaffRegistrationRequestDto[];
  totalCount: number;
  pendingCount: number;
  page: number;
  pageSize: number;
};

export type PublicBranchOptionDto = {
  id: string;
  name: string;
};

export type PublicStaffRegistrationInfoDto = {
  instituteName: string;
  tagline: string | null;
  logoUrl: string | null;
  primaryColor: string;
  accentColor: string;
  defaultBranchId: string | null;
  branches: PublicBranchOptionDto[];
};

export type SubmitStaffRegistrationInput = {
  fullName: string;
  email: string;
  password: string;
  phone: string | null;
  requestedRoleType: "Staff" | "Teacher";
  branchId: string | null;
  notes: string | null;
};

export function getStaffRegistrationLink() {
  return api<StaffRegistrationLinkDto>("/api/staff/registration-link");
}

export function updateStaffRegistrationLink(body: { isEnabled: boolean; defaultBranchId: string | null }) {
  return api<StaffRegistrationLinkDto>("/api/staff/registration-link", {
    method: "PUT",
    body: JSON.stringify(body),
  });
}

export function rotateStaffRegistrationLink() {
  return api<StaffRegistrationLinkDto>("/api/staff/registration-link/rotate", {
    method: "POST",
  });
}

export function getStaffRegistrations(params: { status?: string; page: number; pageSize: number }) {
  const qs = new URLSearchParams();
  if (params.status) qs.set("status", params.status);
  qs.set("page", String(params.page));
  qs.set("pageSize", String(params.pageSize));
  return api<StaffRegistrationPageDto>(`/api/staff/registrations?${qs.toString()}`);
}

export function approveStaffRegistration(
  id: string,
  body: {
    templateKey?: string | null;
    roleType?: string | null;
    permissions?: string[] | null;
    branchId?: string | null;
    branchIds?: string[] | null;
    reviewNotes?: string | null;
  },
) {
  return api<StaffRegistrationRequestDto>(`/api/staff/registrations/${id}/approve`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

export function rejectStaffRegistration(id: string, reviewNotes: string | null) {
  return api<StaffRegistrationRequestDto>(`/api/staff/registrations/${id}/reject`, {
    method: "POST",
    body: JSON.stringify({ reviewNotes }),
  });
}

export function getPublicStaffRegistrationInfo(token: string) {
  return api<PublicStaffRegistrationInfoDto>(`/api/public/staff-registration/${encodeURIComponent(token)}`, {}, false);
}

export function submitPublicStaffRegistration(token: string, body: SubmitStaffRegistrationInput) {
  return api<{ requestId: string; status: string; message: string }>(
    `/api/public/staff-registration/${encodeURIComponent(token)}`,
    {
      method: "POST",
      body: JSON.stringify(body),
    },
    false,
  );
}
