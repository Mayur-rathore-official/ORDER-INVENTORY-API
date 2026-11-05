import { FormEvent, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Banner } from '../components/States'
import { messageFor } from '../lib/problem'

export function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError('')
    try {
      await register(fullName, email, password)
      navigate('/', { replace: true })
    } catch (caught) {
      setError(messageFor(caught))
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="auth-card">
      <p className="eyebrow">Account</p>
      <h1>Create an account</h1>
      <p className="muted">Password must be at least 8 characters. New accounts are customers.</p>
      {error ? <Banner tone="error">{error}</Banner> : null}
      <form onSubmit={onSubmit} className="stack">
        <label>
          Full name
          <input value={fullName} onChange={(event) => setFullName(event.target.value)} required minLength={2} />
        </label>
        <label>
          Email
          <input type="email" value={email} onChange={(event) => setEmail(event.target.value)} required />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} required minLength={8} />
        </label>
        <button className="button" type="submit" disabled={pending}>
          {pending ? 'Creating…' : 'Create account'}
        </button>
      </form>
      <p>
        Already registered? <Link to="/login">Sign in</Link>
      </p>
    </section>
  )
}
