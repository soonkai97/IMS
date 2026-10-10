import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppConfig } from '../config/app-config';
import { CSRF_HEADER, CSRF_HEADER_VALUE } from '../constants/cookie-session.const';
import { credentialsInterceptor } from './credentials.interceptor';
import { csrfHeaderInterceptor } from './csrf-header.interceptor';

const API = 'http://api.test';

describe('cookie session interceptors', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([credentialsInterceptor, csrfHeaderInterceptor])),
        provideHttpClientTesting(),
        { provide: AppConfig, useValue: { $apiBaseUrl: () => API } }
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('sends cookies to the API and to nobody else', () => {
    http.get(`${API}/api/products`).subscribe();
    http.get('https://elsewhere.test/data').subscribe();

    expect(backend.expectOne(`${API}/api/products`).request.withCredentials).toBe(true);
    expect(backend.expectOne('https://elsewhere.test/data').request.withCredentials).toBe(false);
  });

  it('adds the CSRF header to refresh and logout', () => {
    http.post(`${API}/api/auth/refresh`, {}).subscribe();
    http.post(`${API}/api/auth/logout`, {}).subscribe();

    for (const path of ['refresh', 'logout']) {
      const request = backend.expectOne(`${API}/api/auth/${path}`).request;
      expect(request.headers.get(CSRF_HEADER)).toBe(CSRF_HEADER_VALUE);
    }
  });

  it('leaves login and ordinary calls without the CSRF header', () => {
    http.post(`${API}/api/auth/login`, {}).subscribe();
    http.get(`${API}/api/documents`).subscribe();

    expect(backend.expectOne(`${API}/api/auth/login`).request.headers.has(CSRF_HEADER)).toBe(false);
    expect(backend.expectOne(`${API}/api/documents`).request.headers.has(CSRF_HEADER)).toBe(false);
  });
});
