import { useContext } from "react";
import { AuthCtx, type AuthState } from "./AuthContext";

export function useAuth(): AuthState {
  const ctx = useContext(AuthCtx);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
