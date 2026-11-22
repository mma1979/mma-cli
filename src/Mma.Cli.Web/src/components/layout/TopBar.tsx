import { useEffect, useState } from "react";
import { Link, NavLink } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { Moon, Sun } from "lucide-react";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queryKeys";
import type { HealthResponse } from "../../api/types";
import { applyTheme, getInitialTheme, persistTheme } from "../../lib/theme";
import type { Theme } from "../../lib/theme";
import { cn } from "../../lib/cn";
import { useAuthStore } from "../../stores/authStore";
import { Button } from "../primitives/button";

const navLinkClass = ({ isActive }: { isActive: boolean }): string =>
  cn(
    "rounded-md px-3 py-1.5 text-xs font-medium transition-colors",
    isActive ? "bg-muted text-foreground" : "text-muted-foreground hover:text-foreground"
  );

export function TopBar() {
  const [theme, setTheme] = useState<Theme>(() => getInitialTheme());
  const displayName = useAuthStore((state) => state.displayName);

  useEffect(() => {
    applyTheme(theme);
    persistTheme(theme);
  }, [theme]);

  const health = useQuery({
    queryKey: queryKeys.health,
    queryFn: ({ signal }) => api.get<HealthResponse>("/api/v1/health", signal),
    retry: 0,
    refetchInterval: 30_000
  });

  const healthy = health.data?.status === "healthy";

  return (
    <header className="flex h-12 shrink-0 items-center justify-between border-b border-border bg-card px-4">
      <div className="flex items-center gap-4">
        <Link to="/" className="text-sm font-semibold tracking-tight">
          MMA <span className="text-muted-foreground">Workspace</span>
        </Link>

        <nav className="flex items-center gap-1" aria-label="Primary">
          <NavLink to="/" end className={navLinkClass}>
            Workspaces
          </NavLink>
          <NavLink to="/settings" className={navLinkClass}>
            Settings
          </NavLink>
        </nav>
      </div>

      <div className="flex items-center gap-3">
        <span
          className="flex items-center gap-1.5 text-[11px] text-muted-foreground"
          title={health.data ? `BFF ${health.data.version} · .NET ${health.data.dotnetVersion ?? "?"}` : "BFF unreachable"}
        >
          <span
            aria-hidden
            className={cn(
              "h-2 w-2 rounded-full",
              healthy ? "bg-success" : health.isError ? "bg-danger" : "bg-warning"
            )}
          />
          {healthy ? "Connected" : health.isError ? "Offline" : "Checking"}
        </span>

        {displayName ? (
          <span className="hidden text-[11px] text-muted-foreground sm:inline">{displayName}</span>
        ) : null}

        <Button
          size="icon"
          variant="ghost"
          aria-label={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
          onClick={() => setTheme((current) => (current === "dark" ? "light" : "dark"))}
        >
          {theme === "dark" ? <Sun size={15} /> : <Moon size={15} />}
        </Button>
      </div>
    </header>
  );
}
