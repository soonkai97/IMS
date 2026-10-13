import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '@ims/core/notification';
import { API_ERROR_KIND, ApiError } from '@ims/models';
import { withIfMatch } from '../concurrency/if-match';
import { handledInline } from '../errors/handled-inline';
import { errorInterceptor } from './error.interceptor';

const URL = 'http://api.test/api/documents/1/post';

function problem(status: number, body: Record<string, unknown>): Blob {
  return new Blob([JSON.stringify({ status, ...body })], { type: 'application/problem+json' });
}

describe('errorInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let toast: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastService);
  });

  /** Mirrors the generated NSwag client, which asks for blobs. */
  async function failWith(status: number, body: Blob | null): Promise<ApiError> {
    const result = new Promise<ApiError>(resolve => {
      http
        .get(URL, { responseType: 'blob' })
        .subscribe({ error: (error: ApiError) => resolve(error) });
    });
    backend.expectOne(URL).flush(body, { status, statusText: 'x' });
    return result;
  }

  it('EA-15: a gateway timeout with an HTML body says the server cannot be reached, not "something went wrong"', async () => {
    await failWith(504, null);

    expect(toast.$toasts()[0].message).toBe(
      "Can't reach the server. Check your connection and try again."
    );
  });

  it('EA-15: a 503 that carries the API sentence keeps it', async () => {
    await failWith(503, problem(503, { detail: 'The database is not available.' }));

    expect(toast.$toasts()[0].message).toBe('The database is not available.');
  });

  it('EA-24: too many requests shows the API sentence with the wait', async () => {
    await failWith(
      429,
      problem(429, { detail: 'Too many requests. Try again in 12 seconds.', code: 'rate_limited' })
    );

    expect(toast.$toasts()[0].message).toBe('Too many requests. Try again in 12 seconds.');
  });

  it('EA-24: a 429 without a sentence (a proxy) gets ours', async () => {
    await failWith(429, null);

    expect(toast.$toasts()[0].message).toBe('Too many attempts. Wait a minute and try again.');
  });

  it('maps ProblemDetails to an ApiError and shows the server detail as a toast', async () => {
    const error = await failWith(
      409,
      problem(409, {
        title: 'Conflict',
        detail: 'Only 12 PCS available.',
        correlationId: 'abc123',
        productId: 5,
        available: 12,
        requested: 30
      })
    );

    expect(error.kind).toBe(API_ERROR_KIND.Conflict);
    expect(error.detail).toBe('Only 12 PCS available.');
    expect(error.correlationId).toBe('abc123');
    expect(error.extensions['productId']).toBe(5);
    expect(toast.$toasts()[0]).toMatchObject({ kind: 'error', message: 'Only 12 PCS available.' });
  });

  it('keeps field validation errors for the form and shows no toast', async () => {
    const error = await failWith(
      400,
      problem(400, {
        detail: 'Invalid.',
        errors: { Sku: ['SKU is required.'], 'lines[0].qty': ['Too big.'] }
      })
    );

    expect(error.fieldErrors).toEqual({ sku: ['SKU is required.'], 'lines[0].qty': ['Too big.'] });
    expect(toast.$toasts()).toHaveLength(0);
  });

  it('does not toast a 401 (the auth interceptor and the login page deal with it)', async () => {
    const error = await failWith(401, problem(401, { detail: 'Invalid username or password.' }));

    expect(error.kind).toBe(API_ERROR_KIND.Unauthorized);
    expect(toast.$toasts()).toHaveLength(0);
  });

  it('leaves a 409 on a call that sent If-Match to the screen (no raw toast), but not other 409s', async () => {
    const stale = new Promise<ApiError>(resolve => {
      http
        .post(URL, null, { responseType: 'blob', context: withIfMatch('AAAA') })
        .subscribe({ error: (error: ApiError) => resolve(error) });
    });
    backend.expectOne(URL).flush(problem(409, { detail: 'X was changed by someone else.' }), {
      status: 409,
      statusText: 'x'
    });

    expect((await stale).kind).toBe(API_ERROR_KIND.Conflict);
    expect(toast.$toasts()).toHaveLength(0);

    await failWith(409, problem(409, { detail: 'A supplier named X already exists.' }));
    expect(toast.$toasts()[0].message).toBe('A supplier named X already exists.');
  });

  it('shows a network message when the server cannot be reached', async () => {
    const result = new Promise<ApiError>(resolve => {
      http.get(URL).subscribe({ error: (error: ApiError) => resolve(error) });
    });
    backend.expectOne(URL).error(new ProgressEvent('error'));

    const error = await result;

    expect(error.kind).toBe(API_ERROR_KIND.Network);
    expect(toast.$toasts()[0].message).toContain("Can't reach the server");
  });

  it('falls back to a generic message for an error without a body', async () => {
    const error = await failWith(500, null);

    expect(error.kind).toBe(API_ERROR_KIND.Server);
    expect(toast.$toasts()[0].message).toBe('Something went wrong. Try again.');
  });
  describe('one failure, one announcement', () => {
    function failWithContext(
      status: number,
      context: HttpContext,
      detail: string
    ): Promise<ApiError> {
      const result = new Promise<ApiError>(resolve => {
        http
          .get(URL, { responseType: 'blob', context })
          .subscribe({ error: (error: ApiError) => resolve(error) });
      });
      backend.expectOne(URL).flush(problem(status, { detail }), { status, statusText: 'x' });
      return result;
    }

    it('a call the screen shows in place raises no toast, but its error carries the sentence to show', async () => {
      const error = await failWithContext(500, handledInline(), 'The report broke.');

      expect(toast.$toasts()).toHaveLength(0);
      expect(error.message).toBe('The report broke.');
    });

    it('a call flagged with statuses is silent only for those statuses', async () => {
      await failWithContext(409, handledInline(409), 'A supplier named X already exists.');
      expect(toast.$toasts()).toHaveLength(0);

      await failWithContext(500, handledInline(409), 'Down.');
      expect(toast.$toasts().map(item => item.message)).toEqual(['Down.']);
    });

    it('the sentence on the error is the one the toast shows', async () => {
      const error = await failWith(504, null);

      expect(error.message).toBe(toast.$toasts()[0].message);
    });

    it('an error toast from the last failure goes when the user starts something new (a write), not on a read', async () => {
      await failWith(500, null);
      expect(toast.$toasts()).toHaveLength(1);

      http.get(URL).subscribe();
      backend.expectOne(URL).flush(null);
      expect(toast.$toasts()).toHaveLength(1);

      http.post(URL, null).subscribe();
      expect(toast.$toasts()).toHaveLength(0);
      backend.expectOne(URL).flush(null);
    });

    it('the silent token refresh and logout are not the user starting something: an unread error stays', async () => {
      await failWith(500, null);

      http.post('http://api.test/api/auth/refresh', null).subscribe();
      backend.expectOne('http://api.test/api/auth/refresh').flush(null);

      expect(toast.$toasts()).toHaveLength(1);
    });
  });
});
