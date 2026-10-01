import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { ActivitySignalrService } from '../core/activity-signalr.service';

@Component({
  selector: 'app-activity-log',
  templateUrl: './activity-log.html',
  styleUrl: './activity-log.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityLog implements OnInit {
  private readonly activitySignalr = inject(ActivitySignalrService);

  protected readonly events = this.activitySignalr.events;

  ngOnInit(): void {
    this.activitySignalr.connect().catch((error: unknown) => {
      console.error('Failed to connect to the activity hub', error);
    });
  }
}
