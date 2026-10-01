import { Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';

export type ActivityCategory = 'Ingestion' | 'Retrieval' | 'McpTool' | 'Generation';
export type ActivityStatus = 'Started' | 'Succeeded' | 'Failed';

export interface ActivityEvent {
  id: string;
  category: ActivityCategory;
  source: string;
  operation: string;
  target: string | null;
  status: ActivityStatus;
  detail: string | null;
  timestamp: string;
  sequence: number;
}

/** Wraps the ChatApi's `/hubs/activity` SignalR hub (contracts/signalr-hubs.md). */
@Injectable({ providedIn: 'root' })
export class ActivitySignalrService {
  private connection: signalR.HubConnection | null = null;
  private connecting: Promise<void> | null = null;

  readonly events = signal<ActivityEvent[]>([]);

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    this.connecting ??= this.buildConnection();
    await this.connecting;
  }

  private async buildConnection(): Promise<void> {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/activity')
      .withAutomaticReconnect()
      .build();

    connection.on('ActivityEvent', (event: ActivityEvent) => {
      this.events.update((events) => {
        if (events.some((e) => e.id === event.id)) {
          return events;
        }

        return [...events, event].sort((a, b) => a.sequence - b.sequence);
      });
    });

    await connection.start();
    this.connection = connection;
  }
}
