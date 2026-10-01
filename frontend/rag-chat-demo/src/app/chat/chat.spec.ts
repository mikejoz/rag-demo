import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { Chat } from './chat';
import { ChatSignalrService } from '../core/chat-signalr.service';

describe('Chat', () => {
  let sendMessage: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    sendMessage = vi.fn().mockResolvedValue(undefined);

    await TestBed.configureTestingModule({
      imports: [Chat],
      providers: [
        {
          provide: ChatSignalrService,
          useValue: { messages: signal([]), sendMessage },
        },
      ],
    }).compileComponents();
  });

  it('does not send a message when the input is empty', async () => {
    const fixture = TestBed.createComponent(Chat);
    const chat = fixture.componentInstance;

    await chat['submit']();

    expect(sendMessage).not.toHaveBeenCalled();
  });

  it('sends the trimmed message text and resets the form', async () => {
    const fixture = TestBed.createComponent(Chat);
    const chat = fixture.componentInstance;

    chat['form'].controls.message.setValue('  How do I reset my VPN password?  ');
    await chat['submit']();

    expect(sendMessage).toHaveBeenCalledWith('How do I reset my VPN password?');
    expect(chat['form'].controls.message.value).toBe('');
  });
});
