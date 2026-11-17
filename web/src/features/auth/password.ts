import { z } from "zod";

export function passwordSchema(message: string) {
  return z
    .string()
    .min(8, message)
    .regex(/[A-Z]/, message)
    .regex(/[a-z]/, message)
    .regex(/[0-9]/, message)
    .regex(/[^A-Za-z0-9]/, message);
}
