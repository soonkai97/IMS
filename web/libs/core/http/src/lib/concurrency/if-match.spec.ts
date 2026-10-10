import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IF_MATCH_HEADER, ifMatchInterceptor, toEntityTag, withIfMatch } from './if-match';

const URL = 'http://api.test/api/documents/7/submit';

describe('If-Match support', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([ifMatchInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('quotes a bare token and keeps an already quoted one', () => {
    expect(toEntityTag('AAAA')).toBe('"AAAA"');
    expect(toEntityTag('"AAAA"')).toBe('"AAAA"');
  });

  it('sends the token of the call context as a quoted If-Match header', () => {
    http.post(URL, null, { context: withIfMatch('AAAA') }).subscribe();

    expect(backend.expectOne(URL).request.headers.get(IF_MATCH_HEADER)).toBe('"AAAA"');
  });

  it('sends no header when the call has no token', () => {
    http.post(URL, null).subscribe();
    http.post(`${URL}2`, null, { context: withIfMatch(undefined) }).subscribe();

    expect(backend.expectOne(URL).request.headers.has(IF_MATCH_HEADER)).toBe(false);
    expect(backend.expectOne(`${URL}2`).request.headers.has(IF_MATCH_HEADER)).toBe(false);
  });
});
