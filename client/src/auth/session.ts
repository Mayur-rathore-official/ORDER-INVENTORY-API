import type { AuthResponse } from '../types'

const key = 'oi.session'

export function loadSession(): AuthResponse | null {
  const raw = localStorage.getItem(key)
  if (!raw) return null
  try {
    const parsed = JSON.parse(raw) as AuthResponse
    if (!parsed?.accessToken || !parsed.refreshToken || !parsed.user) return null
    return parsed
  } catch {
    return null
  }
}

export function saveSession(session: AuthResponse | null): void {
  if (!session) localStorage.removeItem(key)
  else localStorage.setItem(key, JSON.stringify(session))
}
