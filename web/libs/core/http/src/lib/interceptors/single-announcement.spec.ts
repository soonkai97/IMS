import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ToastHostComponent } from '@ims/core/notification';
import { ApiError } from '@ims/models';
import { UiBannerComponent } from '@ims/ui';
import { handledInline } from '../errors/handled-inline';
import { errorInterceptor } from './error.interceptor';

const URL = 'http://api.test/api/things';
const SENTENCE = 'The database is not available.';

/** A screen that, like the document editor, shows a failed load in a banner. */
@Component({
  selector: 'app-test-screen',
  imports: [ToastHostComponent, UiBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-toast-host />
    @if (message(); as text) {
      <app-banner kind="danger">{{ text }}</app-banner>
    }
  `
})
class TestScreenComponent {
  private readonly http = inject(HttpClient);

  readonly message = signal<string | undefined>(undefined);

  /** Resolves once the call has failed and the screen has been told, so a test never guesses how long that takes. */
  run(method: 'GET' | 'POST', context?: HttpContext, showInline = true): Promise<void> {
    return new Promise(resolve =>
      this.http.request(method, URL, { responseType: 'blob', context }).subscribe({
        error: (error: ApiError) => {
          if (showInline) {
            this.message.set(error.message);
          }
          resolve();
        }
      })
    );
  }
}

/** What a screen reader is told: the text of every live region (alert, status or aria-live) that holds any, outermost only. */
function announcements(root: HTMLElement): string[] {
  return Array.from(
    root.querySelectorAll<HTMLElement>('[role="alert"], [role="status"], [aria-live]')
  )
    .filter(
      region => !region.parentElement?.closest('[role="alert"], [role="status"], [aria-live]')
    )
    .map(region => region.textContent?.trim() ?? '')
    .filter(text => text.length > 0);
}

describe('one failure, one announcement', () => {
  let root: HTMLElement;
  let screen: TestScreenComponent;
  let backend: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<TestScreenComponent>>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    });
    fixture = TestBed.createComponent(TestScreenComponent);
    screen = fixture.componentInstance;
    root = fixture.nativeElement as HTMLElement;
    backend = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  /** Answers the one pending call with a 503, then waits until the interceptor and the screen have both handled it. */
  async function fail(handled: Promise<void>): Promise<void> {
    backend.expectOne(URL).flush(
      new Blob([JSON.stringify({ status: 503, detail: SENTENCE })], {
        type: 'application/problem+json'
      }),
      { status: 503, statusText: 'x' }
    );
    // The error body is read asynchronously (Blob or FileReader) before the interceptor answers; wait for that, not for a delay.
    await handled;
    fixture.detectChanges();
  }

  it('a failure the screen shows in a banner is announced once, by the banner', async () => {
    await fail(screen.run('GET', handledInline()));

    expect(announcements(root)).toEqual([SENTENCE]);
    expect(root.querySelector('app-toast')).toBeNull();
  });

  it('a failure nobody shows in place is announced once, by one toast', async () => {
    await fail(screen.run('POST', undefined, false));

    expect(announcements(root)).toEqual([SENTENCE]);
    expect(root.querySelectorAll('app-toast')).toHaveLength(1);
  });

  it('control: without the flag the same screen announces the failure twice (banner and toast)', async () => {
    await fail(screen.run('GET'));

    expect(announcements(root)).toEqual([SENTENCE, SENTENCE]);
  });
});
