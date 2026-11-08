import type { Macro } from './catalog';

type HostMessage = { type: string; id?: string; ok?: boolean; message?: string; macros?: Macro[] };
interface WebView {
  postMessage(message: { type: string; id?: string }): void;
  addEventListener(type: 'message', listener: (event: MessageEvent<HostMessage>) => void): void;
  removeEventListener(type: 'message', listener: (event: MessageEvent<HostMessage>) => void): void;
}
declare global { interface Window { chrome?: { webview?: WebView }; } }
export const host = window.chrome?.webview;
export function subscribe(listener: (data: HostMessage) => void) {
  const receive = (event: MessageEvent<HostMessage>) => {
    if (event.data && typeof event.data.type === 'string') listener(event.data);
  };
  host?.addEventListener('message', receive);
  host?.postMessage({ type: 'ready' });
  return () => host?.removeEventListener('message', receive);
}
