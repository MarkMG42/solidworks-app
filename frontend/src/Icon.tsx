import type { CSSProperties, ReactNode } from "react";

const paths: Record<string, ReactNode> = {
  cube: (
    <>
      <path d="m12 3 8 4.5v9L12 21l-8-4.5v-9L12 3Z" />
      <path d="m4 7.5 8 4.5 8-4.5M12 12v9" />
    </>
  ),
  search: (
    <>
      <circle cx="10.5" cy="10.5" r="6.5" />
      <path d="m16 16 4 4" />
    </>
  ),
  star: (
    <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9L12 3Z" />
  ),
  grid: (
    <>
      <rect x="4" y="4" width="6" height="6" rx="1" />
      <rect x="14" y="4" width="6" height="6" rx="1" />
      <rect x="4" y="14" width="6" height="6" rx="1" />
      <rect x="14" y="14" width="6" height="6" rx="1" />
    </>
  ),
  sun: (
    <>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.4 1.4m11.2 11.2L19 19M5 19l1.4-1.4M17.6 6.4 19 5" />
    </>
  ),
  moon: <path d="M20.5 13.5A9 9 0 0 1 10.5 3a9 9 0 1 0 10 10.5Z" />,
  settings: (
    <>
      <path d="m10 3-.7 2.4-2 .9L5 5.7 3 9l1.7 1.8v2.4L3 15l2 3.3 2.3-.6 2 .9.7 2.4h4l.7-2.4 2-.9 2.3.6 2-3.3-1.7-1.8v-2.4L21 9l-2-3.3-2.3.6-2-.9L14 3h-4Z" />
      <circle cx="12" cy="12" r="3" />
    </>
  ),
  play: <path d="m9 5 10 7-10 7V5Z" />,
  chevron: <path d="m9 5 7 7-7 7" />,
  close: <path d="m6 6 12 12M6 18 18 6" />,
  info: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 11v6m0-10v.1" />
    </>
  ),
  refresh: (
    <>
      <path d="M20 8a8 8 0 1 0 .4 7M20 3v5h-5" />
    </>
  ),
  folder: <path d="M3 7V5h7l2 3h9v12H3V7Z" />,
  check: <path d="m5 12 4 4L19 6" />,
  dimensions: (
    <>
      <path d="M7 8h11v11H7zM3 7v13m-1-12h2m-2 11h2M6 3h13M7 2v2m11-2v2" />
      <path d="m7 19 11-11" />
    </>
  ),
  drilling: (
    <>
      <path d="m4 8 12-4 5 4-12 4-5-4Zm0 0v8l5 4 12-4V8M9 12v8" />
      <ellipse cx="13" cy="7.5" rx="2" ry="1" />
      <path d="M16 12v3m-3-2v3" />
    </>
  ),
};

export function Icon({
  name,
  size = 20,
  style,
}: {
  name: string;
  size?: number;
  style?: CSSProperties;
}) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      style={style}
    >
      {paths[name] ?? paths.cube}
    </svg>
  );
}
