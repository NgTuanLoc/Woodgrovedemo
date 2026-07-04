import { useAuth } from "./auth/useAuth";
import { Profile } from "./components/Profile";
import { AdminSection } from "./components/AdminSection";
import { TokenPanel } from "./components/TokenPanel";

export default function App() {
  const { user, loading, login, logout } = useAuth();
  if (loading) return <p>Loading…</p>;

  return (
    <main style={{ fontFamily: "system-ui", maxWidth: 720, margin: "2rem auto" }}>
      <h1>Woodgrove Auth Demo</h1>
      {user ? (
        <>
          <button onClick={logout}>Log out</button>
          <Profile />
          <AdminSection />
          <TokenPanel />
        </>
      ) : (
        <button onClick={login}>Log in with Keycloak</button>
      )}
      <hr />
      <p>
        {/* Port pinned in src/Intranet/Properties/launchSettings.json */}
        <a href="http://localhost:5262">Open the Intranet app (SSO demo) →</a>
      </p>
    </main>
  );
}
