import { useAuth } from "../auth/useAuth";

export function Profile() {
  const { user } = useAuth();
  if (!user) return null;
  return (
    <section>
      <h2>Profile</h2>
      <p><strong>Name:</strong> {user.name}</p>
      <p><strong>Roles:</strong> {user.roles.join(", ") || "(none)"}</p>
      <details>
        <summary>All claims</summary>
        <ul>
          {user.claims.map((c, i) => (
            <li key={`${c.type}:${c.value}:${i}`}><code>{c.type}</code>: {c.value}</li>
          ))}
        </ul>
      </details>
    </section>
  );
}
