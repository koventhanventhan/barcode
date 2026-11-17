/* eslint-disable react-refresh/only-export-components */
import type { ReactNode } from "react";
import type { FieldPath, FieldValues, UseFormReturn } from "react-hook-form";

type FormFieldProps = {
  label: string;
  error?: string;
  hint?: string;
  children: ReactNode;
  htmlFor?: string;
};

export function FormField({ label, error, hint, children, htmlFor }: FormFieldProps) {
  return (
    <div className="grid gap-1">
      <label htmlFor={htmlFor} className="text-[13px] font-medium">
        {label}
      </label>
      {children}
      {error ? (
        <p className="text-xs text-danger" role="alert">
          {error}
        </p>
      ) : hint ? (
        <p className="text-xs text-muted">{hint}</p>
      ) : null}
    </div>
  );
}

export function fieldError<T extends FieldValues>(form: UseFormReturn<T>, name: FieldPath<T>): string | undefined {
  const error = form.formState.errors[name];
  return typeof error?.message === "string" ? error.message : undefined;
}
