// Shared widget types — mirror the API contract in docs/04-chat-api.md.

export type Role = "user" | "assistant";

export interface Message {
  role: Role;
  content: string;
}

export interface Source {
  title: string;
  uri: string;
  type: string;
}

export interface WidgetConfig {
  /** POST chat endpoint (data-api-url). */
  apiUrl: string;
  /** Organisation name shown in the panel header (data-org-name). */
  orgName: string;
  /** Optional brand colour, overrides --mid (data-primary-color). */
  primaryColor?: string;
}

/** Result of a non-streaming (?stream=false) JSON response. */
export interface ChatJsonResponse {
  answer: string;
  sources: Source[];
  answered: boolean;
}
