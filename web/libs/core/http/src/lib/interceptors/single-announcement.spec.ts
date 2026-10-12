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

  run(method: 'GET' | 'POST', context?: HttpContext, showInline = true): void {
    this.http
      .request(method, URL, { responseType: 'blob', context })
      .subscribe({ error: (error: ApiError) => showInline && this.message.set(error.message) });
  }
}

/**
 * What a screen reader is told: the text of every live region (role alert or status, or aria-live) that holds any, outermost only.
 * The toast host's two regions are always in the page and are empty until a toast lands in one.
 */
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

  async function fail(): Promise<void> {
    backend.expectOne(URL).flush(
      new Blob([JSON.stringify({ status: 503, detail: SENTENCE })], {
        type: 'application/problem+json'
      }),
      { status: 503, statusText: 'x' }
    );
    // The error body is read asynchronously (Blob or FileReader) before the interceptor answers.
    await new Promise(resolve => setTimeout(resolve, 50));
    fixture.detectChanges();
  }

  it('a failure the screen shows in a banner is announced once, by the banner', async () => {
    screen.run('GET', handledInline());
    await fail();

    expect(announcements(root)).toEqual([SENTENCE]);
    expect(root.querySelector('app-toast')).toBeNull();
    expect(root.querySelector('app-banner[role="alert"]')).not.toBeNull();
  });

  it('a failure nobody shows in place is announced once, by one toast', async () => {
    screen.run('POST', undefined, false);
    await fail();

    expect(announcements(root)).toEqual([SENTENCE]);
    expect(root.querySelectorAll('app-toast')).toHaveLength(1);
    expect(root.querySelector('app-banner')).toBeNull();
  });

  it('control: without the flag the same screen announces the failure twice (banner and toast)', async () => {
    screen.run('GET');
    await fail();

    expect(announcements(root)).toEqual([SENTENCE, SENTENCE]);
  });

  it('the same failure twice in a row leaves one toast', async () => {
    screen.run('POST', undefined, false);
    await fail();
    screen.run('GET', undefined, false);
    await fail();

    expect(announcements(root)).toEqual([SENTENCE]);
  });
});
