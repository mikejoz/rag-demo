import { Injectable, NgZone, inject, signal } from '@angular/core';
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
  private readonly zone = inject(NgZone);
  private connection: signalR.HubConnection | null = null;
  private connecting: Promise<void> | null = null;

  readonly events = signal<ActivityEvent[]>([]);

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    this.connecting ??= this.buildConnection();
    try {
      await this.connecting;
    } finally {
      this.connecting = null;
    }
  }

  private requestFloor: number | null = null;

  /**
   * Starts a new support request: clears the log and ignores everything already buffered,
   * plus ingestion events, so only this request's activity is shown.
   */
  async beginRequest(): Promise<void> {
    let floor = Math.max(0, ...this.events().map((e) => e.sequence));
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      try {
        const recent = await this.connection.invoke<ActivityEvent[]>('GetRecent');
        floor = Math.max(floor, ...(recent ?? []).map((e) => e.sequence));
      } catch (error: unknown) {
        console.warn('Could not read activity buffer to scope the request', error);
      }
    }

    this.requestFloor = floor;
    this.events.set([]);
  }

  /** Pull the server buffer (after chat completes or reconnect). */
  async refresh(): Promise<void> {
    await this.connect();
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    const recent = await this.connection.invoke<ActivityEvent[]>('GetRecent');
    this.mergeEvents(recent ?? []);
  }

  private async buildConnection(): Promise<void> {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/activity')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build();

    connection.on('ActivityEvent', (event: ActivityEvent) => {
      this.zone.run(() => this.mergeEvents([event]));
    });

    connection.onreconnected(() => {
      void this.zone.run(async () => {
        try {
          const recent = await connection.invoke<ActivityEvent[]>('GetRecent');
          this.mergeEvents(recent ?? []);
        } catch (error: unknown) {
          console.error('Failed to refresh activity buffer after reconnect', error);
        }
      });
    });

    await connection.start();
    this.connection = connection;

    try {
      const recent = await connection.invoke<ActivityEvent[]>('GetRecent');
      this.zone.run(() => this.mergeEvents(recent ?? []));
    } catch (error: unknown) {
      console.warn('GetRecent not available yet; relying on OnConnected replay', error);
    }
  }

  private mergeEvents(all: ActivityEvent[]): void {
    const floor = this.requestFloor;
    const incoming =
      floor === null
        ? all
        : all.filter((e) => e.sequence > floor && e.category !== 'Ingestion');
    if (incoming.length === 0) {
      return;
    }

    this.events.update((events) => {
      const byId = new Map(events.map((e) => [e.id, e]));
      for (const event of incoming) {
        byId.set(event.id, event);
      }

      return [...byId.values()].sort((a, b) => a.sequence - b.sequence);
    });
  }
}
