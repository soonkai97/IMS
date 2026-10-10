/** Run-time configuration. Nothing here is a secret: passwords come from the environment and are never printed. */

export const WEB_URL = (process.env['IMS_WEB_URL'] ?? 'http://localhost:4200').replace(/\/$/, '');
export const API_URL = (process.env['IMS_API_URL'] ?? 'http://localhost:5109').replace(/\/$/, '');

/** The five demo users; the username equals the role key. */
export const ROLES = ['admin', 'owner', 'supervisor', 'staff', 'clerk'] as const;
export type Role = (typeof ROLES)[number];

export const ROLE_LABEL: Record<Role, string> = {
  admin: 'Admin',
  owner: 'Owner',
  supervisor: 'Supervisor',
  staff: 'Warehouse staff',
  clerk: 'Clerk'
};

export const passwordVariable = (role: Role): string => `IMS_E2E_PASSWORD_${role.toUpperCase()}`;

/** The demo password of a role. The error names the variable, never a value. */
export function passwordFor(role: Role): string {
  const value = process.env[passwordVariable(role)];
  if (!value) {
    throw new Error(`Set ${passwordVariable(role)} (see e2e/README.md).`);
  }
  return value;
}

export const missingPasswordVariables = (): string[] =>
  ROLES.map(passwordVariable).filter(name => !process.env[name]);
