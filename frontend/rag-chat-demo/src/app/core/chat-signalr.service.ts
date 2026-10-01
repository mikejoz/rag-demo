import { Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';

export interface SourceCitation {
  documentTitle: string;
  sourceUrl: string;
}

export interface ChatMessageView {
  id: string;
  role: 'user' | 'assistant';
  text: string;
  citations: SourceCitation[];
  isStreaming: boolean;
  error?: string;
}

/** Wraps the ChatApi's `/hubs/chat` SignalR hub (contracts/signalr-hubs.md). */
@Injectable({ providedIn: 'root' })
export class ChatSignalrService {
  private connection: signalR.HubConnection | null = null;
  private connecting: Promise<void> | null = null;

  readonly messages = signal<ChatMessageView[]>([]);

  async sendMessage(text: string): Promise<void> {
    await this.ensureConnected();

    this.messages.update((messages) => [
      ...messages,
      { id: crypto.randomUUID(), role: 'user', text, citations: [], isStreaming: false },
    ]);

    await this.connection!.invoke('SendMessage', text);
  }

  private async ensureConnected(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    this.connecting ??= this.buildConnection();
    await this.connecting;
  }

  private async buildConnection(): Promise<void> {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat')
      .withAutomaticReconnect()
      .build();

    connection.on('ResponseToken', (messageId: string, token: string) => this.appendToken(messageId, token));
    connection.on('ResponseComplete', (messageId: string, citedSources: SourceCitation[]) =>
      this.completeMessage(messageId, citedSources),
    );
    connection.on('ResponseError', (messageId: string, message: string) => this.failMessage(messageId, message));

    await connection.start();
    this.connection = connection;
  }

  private appendToken(messageId: string, token: string): void {
    this.messages.update((messages) => {
      const existing = messages.find((m) => m.id === messageId);
      if (existing) {
        return messages.map((m) => (m.id === messageId ? { ...m, text: m.text + token } : m));
      }

      return [
        ...messages,
        { id: messageId, role: 'assistant', text: token, citations: [], isStreaming: true },
      ];
    });
  }

  private completeMessage(messageId: string, citedSources: SourceCitation[]): void {
    this.messages.update((messages) =>
      messages.map((m) => (m.id === messageId ? { ...m, isStreaming: false, citations: citedSources } : m)),
    );
  }

  private failMessage(messageId: string, message: string): void {
    this.messages.update((messages) => {
      const existing = messages.find((m) => m.id === messageId);
      if (existing) {
        return messages.map((m) => (m.id === messageId ? { ...m, isStreaming: false, error: message } : m));
      }

      return [
        ...messages,
        { id: messageId, role: 'assistant', text: '', citations: [], isStreaming: false, error: message },
      ];
    });
  }
}
