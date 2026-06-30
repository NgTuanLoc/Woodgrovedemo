import { useState } from "react";
import { useAuth } from "../auth/useAuth";
import { apiGet } from "../api/client";

export function AdminSection() {
  const { user } = useAuth();
  const [result, setResult] = useState<string>("");
  const [error, setError] = useState<string>("");

  if (!user?.roles.includes("admin")) {
    return <p><em>Admin section hidden — requires the <code>admin</code> role.</em></p>;
  }

  const callAdmin = async () => {
    setError(""); setResult("");
    try {
      const data = await apiGet<{ message: string }>("/api/admin");
      setResult(data.message);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  return (
    <section>
      <h2>Admin</h2>
      <button onClick={callAdmin}>Call /api/admin</button>
      {result && <p>✅ {result}</p>}
      {error && <p>❌ {error}</p>}
    </section>
  );
}
