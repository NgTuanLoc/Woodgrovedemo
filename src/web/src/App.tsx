import { useAuth } from "./auth/useAuth";
import { Profile } from "./components/Profile";

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
        </>
      ) : (
        <button onClick={login}>Log in with Keycloak</button>
      )}
    </main>
  );
}
