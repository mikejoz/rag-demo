import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Chat } from './chat/chat';
import { ActivityLog } from './activity-log/activity-log';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Chat, ActivityLog],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('rag-chat-demo');
}
