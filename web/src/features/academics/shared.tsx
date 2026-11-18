/* eslint-disable react-refresh/only-export-components */
import { NavLink } from "react-router-dom";
import { t } from "@/config/strings";
import { cn } from "@/lib/utils";

export const fieldClass =
  "h-9 w-full rounded-md border border-border bg-card px-3 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring";

export type EducationLevel = {
  id: string;
  code: string;
  name: string;
  description: string;
  sortOrder: number;
  isEnabled: boolean;
};

export type Batch = {
  id: string;
  name: string;
  code: string;
  educationLevelId: string;
  educationLevelName: string;
  branchId: string | null;
  branchName: string | null;
  batchGroupId: string | null;
  batchGroupName: string | null;
  academicYear: string | null;
  isActive: boolean;
  studentCount: number;
};

export type BatchGroup = { id: string; name: string; code: string; isActive: boolean };

export type Subject = {
  id: string;
  name: string;
  code: string;
  educationLevelId: string | null;
  educationLevelName: string | null;
  isActive: boolean;
};

export type ClassSchedule = {
  id: string;
  name: string;
  batchId: string;
  batchName: string;
  subjectId: string;
  subjectName: string;
  teacherUserId: string;
  teacherName: string;
  weekday: number;
  startMinutes: number;
  endMinutes: number;
  room: string | null;
  isActive: boolean;
};

export type TeacherOption = { id: string; fullName: string; roleType: string };

export const WEEKDAYS = [1, 2, 3, 4, 5, 6, 7] as const;

export function minutesToTime(value: number) {
  const hours = Math.floor(value / 60);
  const minutes = value % 60;
  return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}`;
}

export function timeToMinutes(value: string) {
  const [hours, minutes] = value.split(":").map(Number);
  return hours * 60 + minutes;
}

export function AcademicNav() {
  const links = [
    ["/app/academics/levels", t("nav.levels")],
    ["/app/academics/batches", t("nav.batches")],
    ["/app/academics/subjects", t("nav.subjects")],
    ["/app/academics/classes", t("nav.classes")],
    ["/app/academics/timetable", t("nav.timetable")],
  ] as const;

  return (
    <nav className="mb-4 flex gap-1 overflow-x-auto" aria-label={t("nav.academic")}>
      {links.map(([to, label]) => (
        <NavLink
          key={to}
          to={to}
          className={({ isActive }) =>
            cn("whitespace-nowrap rounded-md px-2 py-1 text-sm", isActive ? "bg-secondary font-medium" : "text-muted hover:bg-secondary")
          }
        >
          {label}
        </NavLink>
      ))}
    </nav>
  );
}
