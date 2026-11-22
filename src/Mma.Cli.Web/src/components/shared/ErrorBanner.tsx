import { AlertTriangle } from "lucide-react";
import { Button } from "../primitives/button";

export interface ErrorBannerProps {
  message: string;
  detail?: string;
  onRetry?: () => void;
}

export function ErrorBanner({ message, detail, onRetry }: ErrorBannerProps) {
  return (
    <div
      role="alert"
      className="flex items-start gap-3 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-xs text-foreground"
    >
      <AlertTriangle size={16} className="mt-0.5 shrink-0 text-danger" aria-hidden />
      <div className="flex-1">
        <p className="font-medium">{message}</p>
        {detail ? <p className="mt-1 text-muted-foreground">{detail}</p> : null}
      </div>
      {onRetry ? (
        <Button size="sm" variant="outline" onClick={onRetry}>
          Retry
        </Button>
      ) : null}
    </div>
  );
}
