import { createContext, useEffect, useState, type ReactNode } from "react";

export interface User {
  isAuthenticated: boolean;
  name: string;
  roles: string[];
  claims: { type: string; value: string }[];
}

export interface AuthState {
  user: User | null;
  loading: boolean;
  login: () => void;
  logout: () => void;
}

export const AuthCtx = createContext<AuthState | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch("/bff/user", { credentials: "include" })
      .then((r) => (r.ok ? (r.json() as Promise<User>) : null))
      .then(setUser)
      .catch(() => setUser(null))
      .finally(() => setLoading(false));
  }, []);

  const login = () => {
    const returnUrl =
      window.location.pathname + window.location.search + window.location.hash;
    window.location.href = `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`;
  };
  const logout = () => (window.location.href = "/bff/logout");

  return <AuthCtx.Provider value={{ user, loading, login, logout }}>{children}</AuthCtx.Provider>;
}
