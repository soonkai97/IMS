/** What a form control sets when its value is refused (every `app-*` field and the radio group do). */
const INVALID_FIELD =
  'input[aria-invalid="true"], select[aria-invalid="true"], textarea[aria-invalid="true"], fieldset[aria-invalid="true"] input';

/**
 * After a Save that was refused for an empty or wrong field, the focus goes to the first field with an error, so a keyboard or
 * screen reader user lands on what to fix (WCAG 3.3.1). Call it after the next render, when the `aria-invalid` markers are set.
 * Returns whether a field was found.
 */
export function focusFirstInvalid(root: ParentNode): boolean {
  const field = root.querySelector<HTMLElement>(INVALID_FIELD);
  field?.focus();
  return field !== null;
}
