/// <reference types="vitest/globals" />
import "@testing-library/jest-dom";
import { render, screen, waitFor } from "@testing-library/react";
import { AuthProvider } from "./AuthContext";
import { useAuth } from "./useAuth";

function Probe() {
  const { user, loading } = useAuth();
  if (loading) return <div>loading</div>;
  return <div>{user ? `hi ${user.name}` : "anon"}</div>;
}

afterEach(() => vi.restoreAllMocks());

test("shows authenticated user from /bff/user", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    new Response(JSON.stringify({ isAuthenticated: true, name: "alice", roles: ["admin"], claims: [] }),
      { status: 200, headers: { "Content-Type": "application/json" } }));

  render(<AuthProvider><Probe /></AuthProvider>);
  await waitFor(() => expect(screen.getByText("hi alice")).toBeInTheDocument());
});

test("shows anon when /bff/user returns 401", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));
  render(<AuthProvider><Probe /></AuthProvider>);
  await waitFor(() => expect(screen.getByText("anon")).toBeInTheDocument());
});
