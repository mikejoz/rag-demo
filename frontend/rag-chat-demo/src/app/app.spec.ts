import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { ActivitySignalrService } from './core/activity-signalr.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        {
          provide: ActivitySignalrService,
          useValue: {
            events: () => [],
            connect: async () => {},
            beginRequest: async () => {},
            refresh: async () => {},
          },
        },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render title', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.navbar-brand')?.textContent).toContain('RAG Support Chatbot');
  });
});