/** A role as the user form needs it: the code the user is saved with and the name people read. */
export interface UserRole {
  readonly code: string;
  readonly name: string;
}

export interface UserRoleChoice {
  readonly options: readonly { readonly value: string; readonly label: string }[];
  /** The user holds a role the signed-in person cannot assign: the API refuses every change to them, so the form only shows them. */
  readonly locked: boolean;
}

/**
 * The role choices of the user form. `assignable` is what the API says the signed-in person may give (undefined until it has
 * loaded or when it failed); `current` is the role of the user being edited. The current role is always among the options, so a
 * save never changes it by accident, and is locked when the API does not list it. Nothing here knows a role by name.
 */
export function userRoleChoice(
  assignable: readonly UserRole[] | undefined,
  current: UserRole | undefined
): UserRoleChoice {
  const options = (assignable ?? []).map(role => ({ value: role.code, label: role.name }));
  if (current === undefined || options.some(option => option.value === current.code)) {
    return { options, locked: false };
  }
  return {
    options: [...options, { value: current.code, label: current.name }],
    locked: assignable !== undefined
  };
}
