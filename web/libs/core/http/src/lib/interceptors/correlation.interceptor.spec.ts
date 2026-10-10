import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppConfig } from '../config/app-config';
import { CORRELATION_HEADER, correlationInterceptor } from './correlation.interceptor';

describe('correlationInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([correlationInterceptor])),
        provideHttpClientTesting(),
        { provide: AppConfig, useValue: { $apiBaseUrl: () => 'http://api.test' } }
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  it('adds a fresh, short, plain id to every API call', () => {
    http.get('http://api.test/api/a').subscribe();
    http.get('http://api.test/api/b').subscribe();

    const first = backend
      .expectOne('http://api.test/api/a')
      .request.headers.get(CORRELATION_HEADER);
    const second = backend
      .expectOne('http://api.test/api/b')
      .request.headers.get(CORRELATION_HEADER);

    expect(first).toMatch(/^[a-f0-9]{32}$/);
    expect(second).not.toBe(first);
  });

  it('leaves other hosts untouched', () => {
    http.get('https://elsewhere.test/x').subscribe();

    expect(
      backend.expectOne('https://elsewhere.test/x').request.headers.has(CORRELATION_HEADER)
    ).toBe(false);
  });
});
