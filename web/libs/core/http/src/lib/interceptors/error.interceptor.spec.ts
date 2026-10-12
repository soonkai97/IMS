import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '@ims/core/notification';
import { API_ERROR_KIND, ApiError } from '@ims/models';
import { withIfMatch } from '../concurrency/if-match';
import { errorInterceptor } from './error.interceptor';

const URL = 'http://api.test/api/documents/00000000-0000-4000-8000-000000000001/post';
const PRODUCT_ID = '00000000-0000-4000-8000-000000000005';

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
        productId: PRODUCT_ID,
        available: 12,
        requested: 30
      })
    );

    expect(error.kind).toBe(API_ERROR_KIND.Conflict);
    expect(error.detail).toBe('Only 12 PCS available.');
    expect(error.correlationId).toBe('abc123');
    expect(error.extensions['productId']).toBe(PRODUCT_ID);
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
});
