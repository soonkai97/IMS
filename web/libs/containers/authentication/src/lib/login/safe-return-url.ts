import { HOME_ROUTE, LOGIN_ROUTE } from '@ims/core/identity';

function decoded(url: string): string {
  try {
    return decodeURIComponent(url);
  } catch {
    return url;
  }
}

/**
 * Only a path inside this app is a return URL, so a crafted link cannot send the user to another site: it must start with a
 * single "/" (not "//", not "/\", which a browser reads as "//", also not once percent-decoded), and it is never the sign-in
 * page itself (that would be a loop). Anything else goes Home.
 */
export function safeReturnUrl(url: string | undefined): string {
  if (!url?.startsWith('/') || /^\/[/\\]/.test(decoded(url))) {
    return HOME_ROUTE;
  }
  const path = url.split(/[?#]/)[0];
  return path === LOGIN_ROUTE || path.startsWith(`${LOGIN_ROUTE}/`) ? HOME_ROUTE : url;
}
