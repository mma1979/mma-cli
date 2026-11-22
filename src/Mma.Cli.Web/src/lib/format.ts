export function shortHash(hash: string, length = 8): string {
  if (!hash) {
    return "—";
  }
  const value = hash.startsWith("sha256:") ? hash.slice("sha256:".length) : hash;
  return value.slice(0, length);
}

export function truncateMiddle(value: string, max = 64): string {
  if (value.length <= max) {
    return value;
  }
  const keep = Math.floor((max - 1) / 2);
  return `${value.slice(0, keep)}…${value.slice(value.length - keep)}`;
}

export function formatDateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return date.toLocaleString();
}

export function pluralize(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

export function fileName(path: string): string {
  const segments = path.split(/[\\/]/);
  return segments[segments.length - 1] ?? path;
}
