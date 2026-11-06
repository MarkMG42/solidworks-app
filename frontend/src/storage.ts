export function readSetting<T>(
  key: string,
  fallback: T,
  valid: (value: unknown) => value is T,
): T {
  try {
    const raw = localStorage.getItem(`swtools.${key}`);
    const value: unknown = raw === null ? fallback : JSON.parse(raw);
    return valid(value) ? value : fallback;
  } catch {
    return fallback;
  }
}

export function writeSetting(key: string, value: unknown): boolean {
  try {
    localStorage.setItem(`swtools.${key}`, JSON.stringify(value));
    return true;
  } catch {
    return false;
  }
}
