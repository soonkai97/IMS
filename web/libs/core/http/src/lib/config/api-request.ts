const API_PATH_PREFIX = '/api/';

/** True for requests to the IMS API (the only ones that get a bearer token and a correlation id). */
export function isApiRequest(url: string, apiBaseUrl: string): boolean {
  if (apiBaseUrl.length > 0) {
    return url.startsWith(apiBaseUrl);
  }
  return url.startsWith(API_PATH_PREFIX);
}
