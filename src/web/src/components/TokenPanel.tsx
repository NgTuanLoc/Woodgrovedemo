import { useState } from "react";
import { apiGet } from "../api/client";

export function TokenPanel() {
  const [data, setData] = useState<unknown>(null);
  const [error, setError] = useState("");

  const load = async () => {
    setError("");
    try {
      setData(await apiGet("/bff/debug/tokens"));
    } catch (e) {
      setError((e as Error).message);
    }
  };

  return (
    <section>
      <h2>Token inspector (dev)</h2>
      <button onClick={load}>Inspect decoded tokens</button>
      {error && <p>❌ {error}</p>}
      {data != null && <pre style={{ overflow: "auto" }}>{JSON.stringify(data, null, 2)}</pre>}
    </section>
  );
}
