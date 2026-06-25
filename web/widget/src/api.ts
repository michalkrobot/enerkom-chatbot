// Chat API client: SSE streaming with a non-streaming JSON fallback.
// Contract: docs/04-chat-api.md.

import type { ChatJsonResponse, Message, Source } from "./types";

/** Generic friendly Czech error shown when the server gives nothing better. */
export const GENERIC_ERROR =
  "Omlouvám se, něco se pokazilo. Zkuste to prosím za chvíli.";

/** Callbacks driving the UI as a streamed answer arrives. */
export interface StreamHandlers {
  onToken: (text: string) => void;
  onSources: (sources: Source[]) => void;
  onDone: (answered: boolean) => void;
}

/** Thrown for HTTP errors so the caller can surface a friendly message. */
export class ChatApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ChatApiError";
  }
}

interface RequestBody {
  question: string;
  history: Message[];
}

/**
 * Send a question. Tries SSE streaming first; on any streaming failure
 * (unsupported body stream, parse error, network) falls back to ?stream=false
 * and replays the JSON answer through the same handlers.
 */
export async function sendChat(
  apiUrl: string,
  body: RequestBody,
  handlers: StreamHandlers,
  signal?: AbortSignal,
): Promise<void> {
  try {
    await streamChat(apiUrl, body, handlers, signal);
  } catch (err) {
    if (err instanceof ChatApiError || (err instanceof DOMException && err.name === "AbortError")) {
      // A real HTTP error (e.g. 429/503) or a user abort — do not retry.
      throw err;
    }
    // Streaming itself failed (unsupported / parse / transport). Fall back.
    await jsonChat(apiUrl, body, handlers, signal);
  }
}

async function streamChat(
  apiUrl: string,
  body: RequestBody,
  handlers: StreamHandlers,
  signal?: AbortSignal,
): Promise<void> {
  const res = await fetch(apiUrl, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "text/event-stream" },
    body: JSON.stringify(body),
    signal,
  });

  if (!res.ok) {
    throw await toApiError(res);
  }
  if (!res.body) {
    throw new Error("No response body to stream.");
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let sawDone = false;

  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      // SSE events are separated by a blank line.
      let sep: number;
      while ((sep = indexOfEventBoundary(buffer)) !== -1) {
        const raw = buffer.slice(0, sep);
        buffer = buffer.slice(sep).replace(/^(\r?\n){2}/, "");
        if (handleEvent(raw, handlers)) sawDone = true;
      }
    }
  } finally {
    reader.releaseLock();
  }

  // Flush any trailing event without a terminating blank line.
  if (buffer.trim().length > 0) {
    if (handleEvent(buffer, handlers)) sawDone = true;
  }

  if (!sawDone) {
    // No `done` event — treat as a streaming failure so the caller falls back.
    throw new Error("Stream ended without a done event.");
  }
}

function indexOfEventBoundary(buffer: string): number {
  const lf = buffer.indexOf("\n\n");
  const crlf = buffer.indexOf("\r\n\r\n");
  if (lf === -1) return crlf;
  if (crlf === -1) return lf;
  return Math.min(lf, crlf);
}

/** Parse one SSE event block. Returns true if it was the `done` event. */
function handleEvent(block: string, handlers: StreamHandlers): boolean {
  let event = "message";
  const dataLines: string[] = [];

  for (const line of block.split(/\r?\n/)) {
    if (line.startsWith("event:")) {
      event = line.slice(6).trim();
    } else if (line.startsWith("data:")) {
      dataLines.push(line.slice(5).replace(/^ /, ""));
    }
  }

  const data = dataLines.join("\n");
  if (data.length === 0 && event === "message") return false;

  switch (event) {
    case "token": {
      const parsed = safeParse<{ text?: string }>(data);
      if (parsed && typeof parsed.text === "string") handlers.onToken(parsed.text);
      return false;
    }
    case "sources": {
      const parsed = safeParse<Source[]>(data);
      if (Array.isArray(parsed)) handlers.onSources(parsed);
      return false;
    }
    case "done": {
      const parsed = safeParse<{ answered?: boolean }>(data);
      handlers.onDone(parsed?.answered ?? true);
      return true;
    }
    default:
      return false;
  }
}

async function jsonChat(
  apiUrl: string,
  body: RequestBody,
  handlers: StreamHandlers,
  signal?: AbortSignal,
): Promise<void> {
  const url = appendQuery(apiUrl, "stream", "false");
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(body),
    signal,
  });

  if (!res.ok) {
    throw await toApiError(res);
  }

  const data = (await res.json()) as ChatJsonResponse;
  if (typeof data.answer === "string") handlers.onToken(data.answer);
  if (Array.isArray(data.sources)) handlers.onSources(data.sources);
  handlers.onDone(data.answered ?? true);
}

/** Build a ChatApiError, preferring the ProblemDetails `detail` message. */
async function toApiError(res: Response): Promise<ChatApiError> {
  let detail = GENERIC_ERROR;
  try {
    const text = await res.text();
    if (text) {
      const problem = safeParse<{ detail?: string; title?: string }>(text);
      if (problem?.detail) detail = problem.detail;
      else if (problem?.title) detail = problem.title;
    }
  } catch {
    // ignore — keep the generic message
  }
  return new ChatApiError(detail, res.status);
}

function appendQuery(url: string, key: string, value: string): string {
  const sep = url.includes("?") ? "&" : "?";
  return `${url}${sep}${encodeURIComponent(key)}=${encodeURIComponent(value)}`;
}

function safeParse<T>(text: string): T | null {
  try {
    return JSON.parse(text) as T;
  } catch {
    return null;
  }
}
