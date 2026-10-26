import {
  PublicClientApplication,
  type AccountInfo,
  type AuthenticationResult,
  type Configuration,
} from '@azure/msal-browser';
import { DEFAULT_ANALYST_AVATAR, getStoredAuthSession, setStoredAuthSession, type AuthSession } from './api';

const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID?.trim() ?? '';
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID?.trim() ?? '';
const apiScope = import.meta.env.VITE_ENTRA_API_SCOPE?.trim() ?? '';
const authMode = import.meta.env.VITE_AUTH_MODE?.trim().toLowerCase() ?? 'local';
const configured = [tenantId, clientId, apiScope].every(Boolean);

const msalConfig: Configuration = {
  auth: {
    clientId: clientId || '00000000-0000-0000-0000-000000000000',
    authority: `https://login.microsoftonline.com/${tenantId || 'organizations'}`,
    redirectUri: typeof window === 'undefined' ? undefined : window.location.origin,
    postLogoutRedirectUri: typeof window === 'undefined' ? undefined : `${window.location.origin}/welcome`,
  },
  cache: { cacheLocation: 'sessionStorage', storeAuthStateInCookie: false },
};

const msal = new PublicClientApplication(msalConfig);
let initialized: Promise<void> | undefined;

export function isEntraAuthMode(): boolean {
  return authMode === 'entra';
}

export function isEntraAuthConfigured(): boolean {
  return configured;
}

export async function initializeEntraAuth(): Promise<void> {
  if (!isEntraAuthMode()) return;
  initialized ??= (async () => {
    await msal.initialize();
    const result = await msal.handleRedirectPromise();
    if (result) applyAuthenticationResult(result);
    const account = result?.account ?? msal.getActiveAccount() ?? msal.getAllAccounts()[0] ?? null;
    if (account) {
      msal.setActiveAccount(account);
      publishAccount(account);
    } else setStoredAuthSession(null);
    if (typeof window !== 'undefined') {
      window.pitchMindAuth = { getAccessToken: acquireApiToken };
    }
  })();
  await initialized;
}

export async function signInWithMicrosoft(): Promise<void> {
  if (!configured) throw new Error('Microsoft sign-in is not configured for this deployment. Add the Entra SPA client, API scope, and redirect URI.');
  await initializeEntraAuth();
  const result = await msal.loginPopup({ scopes: [apiScope], prompt: 'select_account' });
  applyAuthenticationResult(result);
}

export async function signOutMicrosoft(): Promise<void> {
  await initializeEntraAuth();
  const account = msal.getActiveAccount();
  setStoredAuthSession(null);
  await msal.logoutRedirect({ account, postLogoutRedirectUri: `${window.location.origin}/welcome` });
}

export async function acquireApiToken(): Promise<string | null> {
  const session = getStoredAuthSession();
  if (session?.user?.provider === 'google' && session.token) {
    return session.token;
  }
  if (!isEntraAuthMode() || !configured) return null;
  await initializeEntraAuth();
  const account = msal.getActiveAccount() ?? msal.getAllAccounts()[0];
  if (!account) return null;
  try {
    const result = await msal.acquireTokenSilent({ scopes: [apiScope], account });
    return result.accessToken || null;
  } catch {
    // Do not start an unexpected interactive flow from background API polling.
    return null;
  }
}

function applyAuthenticationResult(result: AuthenticationResult): void {
  if (result.account) {
    msal.setActiveAccount(result.account);
    publishAccount(result.account);
  }
}

function publishAccount(account: AccountInfo): void {
  const claims = account.idTokenClaims as { roles?: string[] } | undefined;
  const roles = claims?.roles ?? [];
  const role = roles.includes('admin') ? 'Administrator' : roles.includes('analyst') ? 'Tactical Analyst' : 'Authenticated user';
  const session: AuthSession = {
    success: true,
    // The bearer token is deliberately never copied into localStorage.
    token: '',
    authenticatedAt: new Date().toISOString(),
    user: {
      name: account.name || account.username,
      email: account.username,
      role,
      permissions: roles,
      avatarUrl: DEFAULT_ANALYST_AVATAR,
      provider: 'entra',
    },
  };
  setStoredAuthSession(session);
}

