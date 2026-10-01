import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { ChatSignalrService } from '../core/chat-signalr.service';

@Component({
  selector: 'app-chat',
  imports: [ReactiveFormsModule],
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Chat {
  private readonly chatSignalr = inject(ChatSignalrService);

  protected readonly messages = this.chatSignalr.messages;

  protected readonly form = new FormGroup({
    message: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      return;
    }

    const text = this.form.controls.message.value.trim();
    if (!text) {
      return;
    }

    this.form.reset({ message: '' });
    await this.chatSignalr.sendMessage(text);
  }
}
