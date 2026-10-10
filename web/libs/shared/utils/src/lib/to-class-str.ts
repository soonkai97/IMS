type ClassPart = string | false | null | undefined;

/** Joins the truthy class names: toClassStr(['btn', isPrimary && '--primary']) -> 'btn --primary'. */
export function toClassStr(parts: readonly ClassPart[]): string {
  return parts
    .filter((part): part is string => typeof part === 'string' && part.length > 0)
    .join(' ');
}
