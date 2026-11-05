import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { authApi } from '../api/client'
import type { AuthResponse, User } from '../types'
import { loadSession, saveSession } from './session'

interface AuthState {
  user: User | null
  ready: boolean
  login: (email: string, password: string) => Promise<void>
  register: (fullName: string, email: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthResponse | null>(null)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    setSession(loadSession())
    setReady(true)
    const sync = () => setSession(loadSession())
    window.addEventListener('oi:session', sync)
    window.addEventListener('oi:logout', sync)
    return () => {
      window.removeEventListener('oi:session', sync)
      window.removeEventListener('oi:logout', sync)
    }
  }, [])

  const value = useMemo<AuthState>(() => {
    const adopt = (next: AuthResponse) => {
      saveSession(next)
      setSession(next)
    }
    return {
      user: session?.user ?? null,
      ready,
      login: async (email, password) => adopt(await authApi.login(email, password)),
      register: async (fullName, email, password) => adopt(await authApi.register(fullName, email, password)),
      logout: () => {
        saveSession(null)
        setSession(null)
      },
    }
  }, [ready, session])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside AuthProvider')
  return value
}
