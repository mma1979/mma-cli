import { cn } from "../../lib/cn";

export interface TabItem {
  value: string;
  label: string;
}

export interface TabsProps {
  value: string;
  onValueChange: (value: string) => void;
  items: TabItem[];
  label: string;
}

export function Tabs({ value, onValueChange, items, label }: TabsProps) {
  return (
    <div role="tablist" aria-label={label} className="inline-flex rounded-md border border-border bg-muted p-0.5">
      {items.map((item) => {
        const selected = item.value === value;
        return (
          <button
            key={item.value}
            type="button"
            role="tab"
            aria-selected={selected}
            className={cn(
              "rounded px-3 py-1.5 text-xs font-medium transition-colors",
              selected ? "bg-card text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground"
            )}
            onClick={() => onValueChange(item.value)}
          >
            {item.label}
          </button>
        );
      })}
    </div>
  );
}
