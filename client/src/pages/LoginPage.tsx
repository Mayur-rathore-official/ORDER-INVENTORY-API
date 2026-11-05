import { FormEvent, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Banner } from '../components/States'
import { messageFor } from '../lib/problem'

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError('')
    try {
      await login(email, password)
      navigate(from, { replace: true })
    } catch (caught) {
      setError(messageFor(caught))
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="auth-card">
      <p className="eyebrow">Account</p>
      <h1>Sign in</h1>
      <p className="muted">Customers and the store admin use the same form. Too many attempts from one address are paused for a minute.</p>
      {error ? <Banner tone="error">{error}</Banner> : null}
      <form onSubmit={onSubmit} className="stack">
        <label>
          Email
          <input type="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} required />
        </label>
        <label>
          Password
          <input type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} required />
        </label>
        <button className="button" type="submit" disabled={pending}>
          {pending ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
      <p>
        New here? <Link to="/register">Create an account</Link>
      </p>
    </section>
  )
}
