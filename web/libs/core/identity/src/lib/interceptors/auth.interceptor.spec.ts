import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AppConfig } from '@ims/core/http';
import { AuthService } from '../services/auth.service';
import { authInterceptor } from './auth.interceptor';

const API = 'http://api.test';
const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: { token: string | undefined; refresh: jest.Mock };

  beforeEach(() => {
    auth = { token: 'old', refresh: jest.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
        { provide: AppConfig, useValue: { $apiBaseUrl: () => API } }
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('adds the bearer token to API calls only', () => {
    http.get(`${API}/api/products`).subscribe();
    http.get('https://elsewhere.test/data').subscribe();

    expect(backend.expectOne(`${API}/api/products`).request.headers.get('Authorization')).toBe(
      'Bearer old'
    );
    expect(
      backend.expectOne('https://elsewhere.test/data').request.headers.has('Authorization')
    ).toBe(false);
  });

  it('never sends a token to the login endpoint', () => {
    http.post(`${API}/api/auth/login`, {}).subscribe();

    expect(backend.expectOne(`${API}/api/auth/login`).request.headers.has('Authorization')).toBe(
      false
    );
  });

  it('on 401 refreshes once and replays the request with the new token', () => {
    auth.refresh.mockReturnValue(of('new'));
    const results: unknown[] = [];
    http.get(`${API}/api/stock`).subscribe(value => results.push(value));

    backend.expectOne(`${API}/api/stock`).flush(null, UNAUTHORIZED);
    const retry = backend.expectOne(`${API}/api/stock`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush({ ok: true });

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(results).toEqual([{ ok: true }]);
  });

  it('does not loop: a second 401 after the replay is passed on and refresh ran only once', () => {
    auth.refresh.mockReturnValue(of('new'));
    let failure: unknown;
    http.get(`${API}/api/stock`).subscribe({ error: (error: unknown) => (failure = error) });

    backend.expectOne(`${API}/api/stock`).flush(null, UNAUTHORIZED);
    backend.expectOne(`${API}/api/stock`).flush(null, UNAUTHORIZED);

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(failure).toBeInstanceOf(HttpErrorResponse);
  });

  it('passes the original 401 on when the refresh itself fails', () => {
    auth.refresh.mockReturnValue(throwError(() => new Error('no refresh token')));
    let status: number | undefined;
    http.get(`${API}/api/stock`).subscribe({
      error: (error: HttpErrorResponse) => (status = error.status)
    });

    backend.expectOne(`${API}/api/stock`).flush(null, UNAUTHORIZED);

    expect(status).toBe(401);
  });

  it('refreshes again on the next 401 after a refresh that failed (the session is not ended for it)', () => {
    auth.refresh
      .mockReturnValueOnce(throwError(() => ({ isApiError: true, status: 429, extensions: {} })))
      .mockReturnValueOnce(of('new'));
    const statuses: number[] = [];
    http.get(`${API}/api/stock`).subscribe({
      error: (error: HttpErrorResponse) => statuses.push(error.status)
    });
    backend.expectOne(`${API}/api/stock`).flush(null, UNAUTHORIZED);

    http.get(`${API}/api/stock`).subscribe();
    backend.expectOne(`${API}/api/stock`).flush(null, UNAUTHORIZED);
    const replay = backend.expectOne(`${API}/api/stock`);

    expect(statuses).toEqual([401]);
    expect(replay.request.headers.get('Authorization')).toBe('Bearer new');
    expect(auth.refresh).toHaveBeenCalledTimes(2);
    replay.flush({});
  });

  it('leaves non-401 errors alone', () => {
    http.get(`${API}/api/stock`).subscribe({ error: () => undefined });

    backend.expectOne(`${API}/api/stock`).flush(null, { status: 500, statusText: 'Server Error' });

    expect(auth.refresh).not.toHaveBeenCalled();
  });
});
